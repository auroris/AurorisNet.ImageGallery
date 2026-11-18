using MetadataExtractor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace ImageGallery
{
    /// <summary>
    /// Static utility class for media file operations: computing NTFS alternate data stream paths,
    /// reading EXIF/IPTC metadata, and calculating crop/resize dimensions for thumbnails.
    /// </summary>
    public class Media
    {
        /// <summary>Distinguishes between the two derivative types produced for each media file.</summary>
        public enum Type
        {
            /// <summary>Small square preview used in directory listings (100×100 by default).</summary>
            Icon,
            /// <summary>Larger cropped preview shown in the gallery grid (260×260 by default).</summary>
            Thumb
        }

        /// <summary>
        /// Returns the full NTFS alternate data stream path for the thumbnail, in the form
        /// <c>C:\path\to\file.jpg:thumbnail.jpg</c>.
        /// </summary>
        public static string GetThumbPath(FileInfo file)
        {
            if (file == null) { return null; }
            return file.FullName + ":" + GetThumbName(file);
        }

        /// <summary>
        /// Returns the ADS stream name for the thumbnail, e.g. <c>thumbnail.jpg</c>.
        /// The stream keeps the source extension, including for video previews encoded as MP4.
        /// </summary>
        public static string GetThumbName(FileInfo file)
        {
            if (file == null) { return null; }
            return Configuration.Get.Image.Thumbnail.FileName + file.Extension;
        }

        /// <summary>
        /// Returns the full NTFS alternate data stream path for the icon, in the form
        /// <c>C:\path\to\file.jpg:icon.jpg</c>.
        /// </summary>
        public static string GetIconPath(FileInfo file)
        {
            if (file == null) { return null; }
            return file.FullName + ":" + GetIconName(file);
        }

        /// <summary>
        /// Returns the ADS stream name for the icon. Video icons are always PNG (extracted as a
        /// still frame); image icons preserve the source extension.
        /// </summary>
        public static string GetIconName(FileInfo file)
        {
            if (file == null) { return null; }
            String ext = file.Extension.ToLower().Trim('.');

            if (Configuration.Get.Video.Ext.Contains(ext))
            {
                return Configuration.Get.Image.Icon.FileName + ".png";
            }

            return Configuration.Get.Image.Icon.FileName + file.Extension;
        }

        /// <summary>
        /// Reads all EXIF and IPTC tags from <paramref name="file"/> and returns them as a
        /// flat dictionary keyed by tag name. Duplicate tag names are silently ignored
        /// (first occurrence wins). Returns an empty dictionary if no metadata is found.
        /// </summary>
        public static Dictionary<string, string> GetMeta(FileInfo file, ILogger logger)
        {
            Dictionary<string, string> metaDic = new Dictionary<string, string>();

            try
            {
                foreach (var directory in ImageMetadataReader.ReadMetadata(file.FullName))
                    foreach (var tag in directory.Tags)
                        metaDic.TryAdd(tag.Name, tag.Description);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read metadata from {Path}", file.FullName);
            }

            return metaDic;
        }

        /// <summary>
        /// Computes the crop rectangle and final display dimensions needed to produce a thumbnail
        /// from an image of the given size.
        /// <para>
        /// The image's aspect ratio is compared to the two target ratios — landscape (4:3) and
        /// portrait (8:9) — and the closer match is chosen. When <paramref name="crop"/> is
        /// <c>true</c>, the crop rectangle is centred on the image; otherwise the crop covers
        /// the full image and only the resize dimensions are calculated.
        /// </para>
        /// </summary>
        /// <param name="width">Source image width in pixels.</param>
        /// <param name="height">Source image height in pixels.</param>
        /// <param name="crop">
        ///   <c>true</c> to calculate a centred crop rectangle; <c>false</c> to use the full image.
        /// </param>
        /// <returns>
        ///   A <see cref="Dimension"/> describing the crop rectangle (CropTop/Left/Width/Height)
        ///   and the resized output size (Width/Height).
        /// </returns>
        public static Dimension GetCroppedThumbnailDimensions(int width, int height, bool crop)
        {
            Dimension d = new Dimension() {
                Width = width,
                Height = height,
                CropTop = 0,
                CropLeft = 0,
                CropWidth = width,
                CropHeight = height
            };
            Ratio targetRatio;
            Ratio landscape = new Ratio() { Width = 4, Height = 3 };
            Ratio portrait = new Ratio() { Width = 8, Height = 9 };
            Ratio imageRatio = new Ratio() { Width = width, Height = height };
            imageRatio.Simplify();

            // Score how close the image ratio is to each target; lower absolute score = closer match
            double closenessScoreLandscape = 1 - (imageRatio.Value / landscape.Value);
            double closenessScorePortrait = 1 - (imageRatio.Value / portrait.Value);

            if (Math.Abs(closenessScoreLandscape) <= Math.Abs(closenessScorePortrait))
            {
                targetRatio = landscape;
            }
            else
            {
                targetRatio = portrait;
            }

            if (crop)
            {
                // Scale the target ratio to the largest rectangle that fits within the source image
                double cropFactor = Math.Min(Math.Floor((double)width / targetRatio.Width), Math.Floor((double)height / targetRatio.Height));
                d.CropWidth = (int)(targetRatio.Width * cropFactor);
                d.CropHeight = (int)(targetRatio.Height * cropFactor);
                // Centre the crop rectangle
                d.CropTop = (int)(((double)height - (double)d.CropHeight) / 2);
                d.CropLeft = (int)(((double)width - (double)d.CropWidth) / 2);
            }

            // Scale the cropped area down to the configured thumbnail width, preserving aspect ratio
            double resizeFactor = (double)d.CropWidth / (double)Configuration.Get.Image.Thumbnail.Width;
            d.Width = Configuration.Get.Image.Thumbnail.Width;
            d.Height = (int)((double)d.CropHeight / resizeFactor);

            return d;
        }

        /// <summary>
        /// Describes a crop rectangle and the final output dimensions for a thumbnail or icon.
        /// All values are in pixels.
        /// </summary>
        public class Dimension
        {
            /// <summary>Top edge of the crop rectangle within the source image.</summary>
            public int CropTop;
            /// <summary>Left edge of the crop rectangle within the source image.</summary>
            public int CropLeft;
            /// <summary>Width of the crop rectangle.</summary>
            public int CropWidth;
            /// <summary>Height of the crop rectangle.</summary>
            public int CropHeight;
            /// <summary>Width of the resized output image.</summary>
            public int Width;
            /// <summary>Height of the resized output image.</summary>
            public int Height;
        }

        /// <summary>Represents an aspect ratio as a simplified Width:Height fraction.</summary>
        private class Ratio
        {
            public double Width;
            public double Height;
            public double Value => Width / Height;

            /// <summary>
            /// Simplify the fraction so the GCD(width, height) = 1
            /// </summary>
            public void Simplify()
            {
                uint gcd = GCD((uint)Width, (uint)Height);
                Width /= gcd;
                Height /= gcd;
            }

            /// <summary>
            /// Compute the greatest common divisor of two numbers
            /// </summary>
            private uint GCD(uint a, uint b)
            {
                while (a != 0 && b != 0)
                {
                    if (a > b)
                        a %= b;
                    else
                        b %= a;
                }

                return a | b;
            }
        }
    }
}
