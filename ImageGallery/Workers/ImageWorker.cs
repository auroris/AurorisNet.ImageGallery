using ImageMagick;
using System.IO;
using Trinet.Core.IO.Ntfs;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ImageGallery.Workers
{
	/// <summary>
	/// Generates thumbnails and icons for image files using ImageMagick.
	/// <list type="bullet">
	///   <item><description>
	///     <b>Thumbnail</b> — the image is cropped to the best-fit aspect ratio (landscape 4:3 or
	///     portrait 8:9) centred on the subject, then scaled to the configured thumbnail dimensions.
	///     Written to the file's NTFS alternate data stream.
	///   </description></item>
	///   <item><description>
	///     <b>Icon</b> — the image is scaled down to icon dimensions without cropping.
	///     Written to the file's NTFS alternate data stream.
	///   </description></item>
	/// </list>
	/// </summary>
	public class ImageWorker
	{
		public ImageWorker() { }

		/// <summary>
		/// Processes an image file asynchronously, generating either a cropped thumbnail or a
		/// scaled icon depending on <paramref name="type"/>, and writing it to the file's NTFS
		/// alternate data stream. Also extracts IPTC metadata (title, caption) from the file.
		/// </summary>
		/// <param name="file">The image file to process.</param>
		/// <param name="progress">Progress tracker for this directory's processing run.</param>
		/// <param name="type">Whether to produce a thumbnail or an icon.</param>
		/// <returns>A <see cref="MediaResult"/> populated with metadata and dimension information.</returns>
		public async Task<MediaResult> ProcessAsync(FileInfo file, ProgressWorker progress, Media.Type type)
		{
			MediaResult result = new MediaResult();
			result.File = file;
			result.Updated = file.LastWriteTime;
			result.Duration = 0;

			Dictionary<string, string> metaDic = Media.GetMeta(file);
			result.Title = metaDic.ContainsKey("Object Name") ? metaDic["Object Name"] : file.Name;
			result.Caption = metaDic.ContainsKey("Caption/Abstract") ? metaDic["Caption/Abstract"] : result.Caption;
			result.Type = type;

			// Make the thumbnail
			if (type == Media.Type.Thumb)
			{
				await Task.Run(() =>
				{
					using MagickImage image = new MagickImage(file.FullName);
					result.Width = (int)image.Width;
					result.Height = (int)image.Height;

					// Compute new dimensions
					Media.Dimension imageCrop = Media.GetCroppedThumbnailDimensions((int)image.Width, (int)image.Height, true);

					// Crop image
					image.Crop(new MagickGeometry(imageCrop.CropLeft, imageCrop.CropTop, (uint)imageCrop.CropWidth, (uint)imageCrop.CropHeight));

					// Resize image to thumbnail
					MagickGeometry thumbSize = new MagickGeometry((uint)imageCrop.Width, (uint)imageCrop.Height);
					thumbSize.IgnoreAspectRatio = false;
					image.Resize(thumbSize);

					// Write the thumbnail to disk
					using (FileStream writeStream = file.GetAlternateDataStream(Media.GetThumbName(file), FileMode.Create).OpenWrite())
					{
						image.Write(writeStream);
					}

					progress.FinishedWork(Media.GetThumbName(file));
				});
			}

			if (type == Media.Type.Icon)
			{
				await Task.Run(() =>
				{
					using MagickImage image = new MagickImage(file.FullName);
					result.Width = (int)image.Width;
					result.Height = (int)image.Height;

					// Resize image to icon
					MagickGeometry iconSize = new MagickGeometry((uint)Configuration.Get.Image.Icon.Width, (uint)Configuration.Get.Image.Icon.Height);
					iconSize.IgnoreAspectRatio = false;
					image.Resize(iconSize);

					// Write the icon to disk
					using (FileStream writeStream = file.GetAlternateDataStream(Media.GetIconName(file), FileMode.Create).OpenWrite())
					{
						image.Write(writeStream);
					}

					progress.FinishedWork(Media.GetIconName(file));
				});
			}

			return result;
		}
    }
}
