using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ImageGallery
{
    /// <summary>
    /// Lazy singleton that reads all application settings from <see cref="Program.AppConfiguration"/>
    /// (appsettings.json) on first access. Relative paths are resolved against the application base
    /// directory; <see cref="GalleryPath"/> falls back to the bundled <c>DemoGallery</c> if the
    /// configured path does not exist.
    /// </summary>
    public class Configuration
    {
        private static Configuration me = null;
        /// <summary>Returns the singleton instance, creating it on first call.</summary>
        public static Configuration Get {
            get {
                if (me != null) { return me; }
                me = new Configuration();
                return me;
            }
        }

        private Configuration ()
        {
            RegenerateThumbnails = Program.AppConfiguration.GetValue<bool>("RegenerateThumbnails");
            MetaFile = Program.AppConfiguration.GetValue<String>("MetaFile");
            TempDir = ResolvePath(Program.AppConfiguration.GetValue<String>("TempDir"));
            LogDir = ResolvePath(Program.AppConfiguration.GetValue<String>("LogDir"));

            string galleryPath = ResolvePath(Program.AppConfiguration.GetValue<String>("GalleryPath"));
            if (!Directory.Exists(galleryPath))
            {
                string fallback = ResolvePath("DemoGallery");
                if (Directory.Exists(fallback))
                    galleryPath = fallback;
            }
            GalleryPath = galleryPath;

            Image = new ImageType()
            {
                Ext = new List<string>(Program.AppConfiguration.GetSection("Images").Get<string[]>() ?? Array.Empty<string>()),
                Thumbnail = new ImageSettings()
                {
                    FileName = Program.AppConfiguration.GetValue<String>("Thumbnail:name"),
                    Width = Program.AppConfiguration.GetValue<int>("Thumbnail:width"),
                    Height = Program.AppConfiguration.GetValue<int>("Thumbnail:height")
                },
                Icon = new ImageSettings()
                {
                    FileName = Program.AppConfiguration.GetValue<String>("Icon:name"),
                    Width = Program.AppConfiguration.GetValue<int>("Icon:width"),
                    Height = Program.AppConfiguration.GetValue<int>("Icon:height")
                }
            };
            Video = new VideoType()
            {
                Ext = new List<string>(Program.AppConfiguration.GetSection("Videos").Get<string[]>() ?? Array.Empty<string>()),
                ClipStart = Program.AppConfiguration.GetValue<int>("MediaClipStart"),
                ClipLength = Program.AppConfiguration.GetValue<int>("MediaClipLength")
            };
        }

        /// <summary>
        /// If path is relative, resolves it against the application base directory.
        /// Absolute paths (including UNC) are returned unchanged.
        /// </summary>
        private static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!Path.IsPathFullyQualified(path))
                return Path.GetFullPath(path, AppDomain.CurrentDomain.BaseDirectory);
            return path;
        }

        /// <summary>When true, thumbnails and icons are regenerated even if they already exist in the ADS.</summary>
        public bool RegenerateThumbnails;
        /// <summary>Absolute path to the gallery root directory (resolved from appsettings, with DemoGallery fallback).</summary>
        public string GalleryPath;
        /// <summary>Filename of the per-directory JSON index file (e.g. <c>_meta.json</c>).</summary>
        public string MetaFile;
        /// <summary>Image processing settings including supported extensions and thumbnail/icon dimensions.</summary>
        public ImageType Image;
        /// <summary>Video processing settings including supported extensions and clip timing.</summary>
        public VideoType Video;
        /// <summary>Absolute path to the directory used for temporary files during thumbnail generation.</summary>
        public string TempDir;
        /// <summary>Absolute path to the directory used by log4net for log output.</summary>
        public string LogDir;

        public class ImageType
        {
            /// <summary>File extensions (without dot) that are treated as images, e.g. <c>["jpg","png"]</c>.</summary>
            public List<string> Ext;
            public ImageSettings Thumbnail;
            public ImageSettings Icon;
        }

        public class ImageSettings
        {
            /// <summary>Base name of the ADS stream, e.g. <c>thumbnail</c> or <c>icon</c>.</summary>
            public string FileName;
            public int Width;
            public int Height;
        }

        public class VideoType
        {
            /// <summary>File extensions (without dot) that are treated as videos, e.g. <c>["mp4"]</c>.</summary>
            public List<string> Ext;
            /// <summary>Seconds into the video at which the thumbnail clip begins.</summary>
            public int ClipStart;
            /// <summary>Duration in seconds of the thumbnail clip.</summary>
            public int ClipLength;
        }
    }
}
