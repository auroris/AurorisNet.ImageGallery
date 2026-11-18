using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Trinet.Core.IO.Ntfs;

namespace ImageGallery.Workers
{
	/// <summary>Generates a silent MP4 thumbnail and a PNG icon in the video's NTFS alternate data streams.</summary>
	public class VideoWorker(ILogger<VideoWorker> logger)
	{
		public async Task<MediaResult> ProcessAsync(FileInfo file, ProgressWorker progress, Media.Type type)
		{
			try
			{
				var (width, height, duration) = await ProbeAsync(file);
				var metadata = Media.GetMeta(file, logger);
				var result = new MediaResult
				{
					File = file,
					Updated = file.LastWriteTime,
					Title = metadata.TryGetValue("Object Name", out var title) ? title : file.Name,
					Caption = metadata.TryGetValue("Caption/Abstract", out var caption) ? caption : null,
					Width = width,
					Height = height,
					Duration = duration,
					Type = type
				};

				if (type == Media.Type.Thumb)
				{
					double start = Configuration.Get.Video.ClipStart;
					double length = Configuration.Get.Video.ClipLength;
					// If duration is unknown, start at the beginning and let FFmpeg stop at EOF.
					if (start + length > duration)
						start = 0;

					// H.264 with yuv420p needs even dimensions. FFmpeg calculates the proportional height.
					int thumbWidth = Math.Max(2, Configuration.Get.Image.Thumbnail.Width / 2 * 2);
					await WriteDerivativeAsync(file, Media.GetThumbName(file),
						"-ss", start.ToString(CultureInfo.InvariantCulture), "-i", file.FullName,
						"-map", "0:V:0", "-t", length.ToString(CultureInfo.InvariantCulture),
						"-an", "-vf", $"scale={thumbWidth}:-2", "-c:v", "libx264", "-pix_fmt", "yuv420p",
						"-b:v", "60k", "-f", "mp4");
				}

				string iconName = Media.GetIconName(file);
				if (!file.AlternateDataStreamExists(iconName))
				{
					// Use the tenth frame, or the last available frame on a shorter video.
					string filter = $"trim=end_frame=10,scale={Configuration.Get.Image.Icon.Width}:-1,reverse";
					await WriteDerivativeAsync(file, iconName, "-i", file.FullName, "-map", "0:V:0",
						"-vf", filter, "-frames:v", "1", "-c:v", "png", "-f", "image2", "-update", "1");
				}

				progress.FinishedWork(file.FullName);
				return result;
			}
			catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070) || ex.HResult == unchecked((int)0x80070027))
			{
				logger.LogError(ex, "Disk full while processing video {Path} (temporary directory {TempDir})",
					file.FullName, Configuration.Get.TempDir);
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to process video {Path} (temporary directory {TempDir})",
					file.FullName, Configuration.Get.TempDir);
				throw;
			}
		}

		private static async Task<(int Width, int Height, double Duration)> ProbeAsync(FileInfo file)
		{
			string json = await RunToolAsync("ffprobe", "-select_streams", "V:0",
				"-show_entries", "stream=width,height,duration:format=duration", "-of", "json", "-i", file.FullName);
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			var streams = root.GetProperty("streams");
			if (streams.GetArrayLength() == 0)
				throw new InvalidDataException($"No video stream found in {file.FullName}.");

			var stream = streams[0];
			int width = stream.GetProperty("width").GetInt32();
			int height = stream.GetProperty("height").GetInt32();
			if (width <= 0 || height <= 0)
				throw new InvalidDataException($"Invalid video dimensions in {file.FullName}.");

			double duration = ReadDuration(stream);
			if (duration == 0 && root.TryGetProperty("format", out var format))
				duration = ReadDuration(format);

			return (width, height, duration);
		}

		private static double ReadDuration(JsonElement element)
		{
			if (element.TryGetProperty("duration", out var value)
				&& double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
				&& double.IsFinite(duration) && duration > 0)
				return duration;

			return 0;
		}

		private static async Task WriteDerivativeAsync(FileInfo file, string streamName, params string[] arguments)
		{
			string tempPath = Path.Combine(Configuration.Get.TempDir, Guid.NewGuid().ToString("N"));
			try
			{
				await RunToolAsync("ffmpeg", ["-nostdin", "-abort_on", "empty_output", .. arguments, tempPath]);
				using var input = File.OpenRead(tempPath);
				var stream = file.GetAlternateDataStream(streamName, FileMode.OpenOrCreate);
				var output = stream.Open(FileMode.Create, FileAccess.Write, FileShare.None);
				try
				{
					using (output)
					{
						await input.CopyToAsync(output);
					}
				}
				catch
				{
					stream.Delete();
					throw;
				}
			}
			finally
			{
				File.Delete(tempPath);
			}
		}

		private static async Task<string> RunToolAsync(string tool, params string[] arguments)
		{
			var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ffmpeg", tool + ".exe"))
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			startInfo.ArgumentList.Add("-v");
			startInfo.ArgumentList.Add("error");
			foreach (string argument in arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo);
			var output = process.StandardOutput.ReadToEndAsync();
			var errorOutput = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			string stdout = await output;
			string stderr = await errorOutput;
			if (process.ExitCode != 0)
				throw new InvalidOperationException($"{tool} exited with code {process.ExitCode}: {stderr.Trim()}");

			return stdout;
		}
	}
}
