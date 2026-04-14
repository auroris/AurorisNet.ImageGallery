using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Trinet.Core.IO.Ntfs;
using Xabe.FFmpeg;

namespace ImageGallery.Workers
{
	/// <summary>
	/// Generates thumbnails and icons for video files using FFmpeg.
	/// <list type="bullet">
	///   <item><description>
	///     <b>Thumbnail</b> — a short H.264 MP4 clip taken from the configured offset into the
	///     video, scaled to the thumbnail dimensions, stored in the file's NTFS alternate data stream.
	///     When producing a thumbnail the icon is also generated in the same pass to avoid a
	///     second FFmpeg invocation.
	///   </description></item>
	///   <item><description>
	///     <b>Icon</b> — a single PNG frame extracted from the first frame, scaled to icon dimensions,
	///     stored in the file's NTFS alternate data stream.
	///   </description></item>
	/// </list>
	/// Both derivatives are written to temporary files first, then copied into the ADS, so
	/// the source file is never modified.
	/// </summary>
	public class VideoWorker
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(VideoWorker));

		public VideoWorker()
		{}

		/// <summary>
		/// Processes a video file asynchronously, generating a thumbnail clip or an icon frame
		/// depending on <paramref name="type"/>, and writing it to the file's NTFS alternate
		/// data stream. When <paramref name="type"/> is <see cref="Media.Type.Thumb"/>, the icon
		/// is also generated in the same pass. Also extracts IPTC metadata (title, caption).
		/// </summary>
		/// <param name="file">The video file to process.</param>
		/// <param name="progress">Progress tracker for this directory's processing run.</param>
		/// <param name="type">Whether to produce a thumbnail clip or an icon frame.</param>
		/// <returns>A <see cref="MediaResult"/> populated with metadata and dimension information.</returns>
		public async Task<MediaResult> ProcessAsync(FileInfo file, ProgressWorker progress, Media.Type type)
		{
			MediaResult result = new MediaResult();
			IMediaInfo mediaInfo;
			try
			{
				mediaInfo = await FFmpeg.GetMediaInfo(file.FullName);
			}
			catch (Exception ex)
			{
				Log.Error($"FFmpeg failed to read media info for: {file.FullName}", ex);
				throw;
			}

			IVideoStream videoStream = mediaInfo.VideoStreams.FirstOrDefault();
			if (videoStream == null)
			{
				var ex = new InvalidOperationException("No video stream found in " + file.FullName);
				Log.Error(ex.Message);
				throw ex;
			}

			// Temp file is GUID-prefixed to avoid collisions when multiple workers run in parallel
			string thumbTempFile = Path.Combine(Configuration.Get.TempDir, Guid.NewGuid() + "_" + Media.GetThumbName(file));

			// Fill out metadata
			Dictionary<string, string> metaDic = Media.GetMeta(file);
			result.Title = metaDic.ContainsKey("Object Name") ? metaDic["Object Name"] : file.Name;
			result.Caption = metaDic.ContainsKey("Caption/Abstract") ? metaDic["Caption/Abstract"] : result.Caption;
			result.File = file;
			result.Updated = file.LastWriteTime;
			result.Width = videoStream.Width;
			result.Height = videoStream.Height;
			result.Duration = videoStream.Duration.TotalSeconds;
			result.Type = type;

			// Calculate the thumbnail clip start and length, clamping if the video is too short
			double clipStart = Configuration.Get.Video.ClipStart;
			double clipLength = Configuration.Get.Video.ClipLength;
			double duration = videoStream.Duration.TotalSeconds;
			if (clipStart + clipLength > duration)
			{
				clipStart = 0; // Requested clip exceeds video file's duration
			}
			if (clipStart + clipLength > duration)
			{
				clipLength = duration; // Extremely short video file
			}

			if (type == Media.Type.Thumb)
			{
				try
				{
					Media.Dimension d = Media.GetCroppedThumbnailDimensions(videoStream.Width, videoStream.Height, false);

					IConversion thumbConversion = FFmpeg.Conversions.New()
						.AddStream(videoStream)
						.SetSeek(TimeSpan.FromSeconds(clipStart))
						.SetOutputTime(TimeSpan.FromSeconds(clipLength))
						.SetOutputFormat(Format.mp4)
						.SetVideoBitrate(60000)
						.AddParameter("-s " + ((int)d.Width).ToString() + "x" + ((int)d.Height).ToString())
						.AddParameter("-vcodec libx264")
						.AddParameter("-pix_fmt yuv420p")
						.SetOutput(thumbTempFile);

					await thumbConversion.Start();
					using (FileStream input = File.OpenRead(thumbTempFile))
					using (FileStream output = file.GetAlternateDataStream(Media.GetThumbName(file), FileMode.Create).OpenWrite())
					{
						await input.CopyToAsync(output);
					}
				}
				catch (IOException ex) when (IsDiskFull(ex))
				{
					Log.Error($"Disk full while generating thumbnail for video: {file.FullName} " +
						$"(temp dir: {Configuration.Get.TempDir}, target volume: {Path.GetPathRoot(file.FullName)})", ex);
					throw;
				}
				catch (IOException ex)
				{
					Log.Error($"I/O error generating thumbnail for video: {file.FullName} " +
						$"(temp file: {thumbTempFile}) — " +
						"check temp dir write permissions and that the target volume supports NTFS alternate data streams", ex);
					throw;
				}
				catch (Exception ex)
				{
					Log.Error($"Failed to generate thumbnail for video: {file.FullName} " +
						$"(temp dir: {Configuration.Get.TempDir})", ex);
					throw;
				}
				finally
				{
					if (File.Exists(thumbTempFile)) File.Delete(thumbTempFile);
				}

				// Generate the icon in the same pass; videoStream is no longer needed for the thumb
				await GenerateIconAsync(file, videoStream);
				progress.FinishedWork(file.FullName);
			}
			else if (type == Media.Type.Icon)
			{
				await GenerateIconAsync(file, videoStream);
				progress.FinishedWork(file.FullName);
			}

			return result;
		}

		/// <summary>
		/// Extracts a single PNG frame from <paramref name="videoStream"/> and writes it to the
		/// icon alternate data stream on <paramref name="file"/>. Skipped if the icon ADS already
		/// exists, so repeated calls for the same file are cheap.
		/// </summary>
		private async Task GenerateIconAsync(FileInfo file, IVideoStream videoStream)
		{
			if (file.AlternateDataStreamExists(Media.GetIconName(file))) { return; }

			string iconTempFile = Path.Combine(Configuration.Get.TempDir, Guid.NewGuid() + "_" + Media.GetIconName(file));
			Func<string, string> iconTempFileFunc = (number) => "\"" + iconTempFile.Replace("\\", "\\\\") + "\"";

			try
			{
				videoStream.SetCodec(VideoCodec.png);
				IConversion iconConversion = FFmpeg.Conversions.New()
					.AddStream(videoStream)
					.ExtractNthFrame(10, iconTempFileFunc)
					.AddParameter("-s " + Configuration.Get.Image.Icon.Width.ToString() + "x" +
						((int)(((double)Configuration.Get.Image.Icon.Width / (double)videoStream.Width) * (double)videoStream.Height)).ToString());

				await iconConversion.Start();
				using (FileStream input = File.OpenRead(iconTempFile))
				using (FileStream output = file.GetAlternateDataStream(Media.GetIconName(file), FileMode.Create).OpenWrite())
				{
					await input.CopyToAsync(output);
				}
			}
			catch (IOException ex) when (IsDiskFull(ex))
			{
				Log.Error($"Disk full while generating icon for video: {file.FullName} " +
					$"(temp dir: {Configuration.Get.TempDir}, target volume: {Path.GetPathRoot(file.FullName)})", ex);
				throw;
			}
			catch (IOException ex)
			{
				Log.Error($"I/O error generating icon for video: {file.FullName} " +
					$"(temp file: {iconTempFile}) — " +
					"check temp dir write permissions and that the target volume supports NTFS alternate data streams", ex);
				throw;
			}
			catch (Exception ex)
			{
				Log.Error($"Failed to generate icon for video: {file.FullName} " +
					$"(temp dir: {Configuration.Get.TempDir})", ex);
				throw;
			}
			finally
			{
				if (File.Exists(iconTempFile)) File.Delete(iconTempFile);
			}
		}

		/// <summary>
		/// Returns true if the IOException represents a disk-full condition
		/// (ERROR_DISK_FULL 0x70 or ERROR_HANDLE_DISK_FULL 0x27).
		/// </summary>
		private static bool IsDiskFull(IOException ex)
		{
			const int ERROR_DISK_FULL = unchecked((int)0x80070070);
			const int ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
			return ex.HResult == ERROR_DISK_FULL || ex.HResult == ERROR_HANDLE_DISK_FULL;
		}
	}
}
