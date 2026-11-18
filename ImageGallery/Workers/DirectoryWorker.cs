using ImageMagick;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Trinet.Core.IO.Ntfs;

namespace ImageGallery.Workers
{
    /// <summary>
    /// Orchestrates thumbnail and icon generation for a single gallery directory.
    /// <para>
    /// On each call to <see cref="ProcessAsync"/>, the worker compares the directory contents against
    /// the cached <c>_meta.json</c> index, queues <see cref="ImageWorker"/> or
    /// <see cref="VideoWorker"/> jobs for any new or changed files (or all files when
    /// <c>overwrite</c> is true), waits for all jobs to complete, then writes an updated index.
    /// </para>
    /// <para>
    /// Progress is tracked through <see cref="ProgressWorker"/> so the frontend can poll
    /// <c>/api/dir/status</c> while generation is in progress.
    /// </para>
    /// </summary>
    public class DirectoryWorker(ILogger<DirectoryWorker> logger, ImageWorker imageWorker, VideoWorker videoWorker)
    {
		/// <summary>
		/// Scans the directory, generates missing thumbnails and icons, and returns the updated
		/// media index. Returns <c>null</c> if generation is already in progress for this directory.
		/// </summary>
		/// <param name="workDirectory">The gallery directory to scan.</param>
		/// <param name="overwrite">
		///   When <c>true</c>, regenerates thumbnails and icons for all files even if they already
		///   exist. Controlled by the <c>RegenerateThumbnails</c> appsetting.
		/// </param>
		public async Task<SortedDictionary<string, DirItem>> ProcessAsync(DirectoryInfo workDirectory, bool overwrite)
		{
			bool dirty = false;
			using ProgressWorker progress = new ProgressWorker(workDirectory);
			List<Task<MediaResult>> mediaProcessingTasks = new List<Task<MediaResult>>();
			SortedDictionary<string, DirItem> mediaList = new SortedDictionary<string, DirItem>(); // New metadata
			SortedDictionary<string, DirItem> mediaListOld = new SortedDictionary<string, DirItem>(); // Old cached metadata

			List<FileInfo> iconWork = new List<FileInfo>();
			List<FileInfo> thumbWork = new List<FileInfo>();

			if (progress.GetProgress() > 0) { return null; }

			progress.AddWork(workDirectory.FullName);

			// Retrieve existing media list if any
			if (File.Exists(Path.Combine(workDirectory.FullName, Configuration.Get.MetaFile)))
			{
				mediaListOld = JsonSerializer.Deserialize<SortedDictionary<string, DirItem>>(File.ReadAllText(Path.Combine(workDirectory.FullName, Configuration.Get.MetaFile)));
			}

			// List directories in the dir
			foreach (DirectoryInfo dir in workDirectory.GetDirectories())
			{
				// If the dir starts with a $ then we ignore it
				if (dir.Name.StartsWith('$')) { continue; }

				// Try to identify the icon for a directory. It's basically whatever the first media file found is.
				FileInfo iconFullPath = FindFirst(dir);
				if (iconFullPath == null) { continue; }

				string iconRelativePath = Path.GetRelativePath(Configuration.Get.GalleryPath, iconFullPath.FullName).Replace('\\', '/');

				if (!mediaListOld.TryGetValue(iconRelativePath, out DirItem cachedFolder) || overwrite
					|| (cachedFolder.PreviewUnavailable && cachedFolder.Updated != iconFullPath.LastWriteTimeUtc))
				{
					progress.AddWork(iconFullPath.FullName);
					iconWork.Add(iconFullPath);

					mediaList.Add(iconRelativePath, new DirItem
					{
						Title = dir.Name,
						Type = "dir",
						Updated = DateTime.Now
					});
				}
				else
                {
					// No updates, simply copy the old data to the new
					mediaList.Add(iconRelativePath, cachedFolder);
                }
			}

			// List files in the dir
			foreach (FileInfo file in workDirectory.GetFiles())
			{
				if (!mediaListOld.TryGetValue(file.Name, out DirItem cachedItem) || overwrite
					|| (cachedItem.PreviewUnavailable && cachedItem.Updated != file.LastWriteTimeUtc))
				{
					String ext = file.Extension.ToLower().Trim('.');
					if (Configuration.Get.Image.Ext.Contains(ext) || Configuration.Get.Video.Ext.Contains(ext))
					{
						progress.AddWork(file.FullName);
						thumbWork.Add(file);
					}
				}
				else
				{
					// Older indexes have no source dimensions. Read just the image header.
					if (cachedItem.Type == "image" && (cachedItem.Width <= 0 || cachedItem.Height <= 0))
					{
						try
						{
							using MagickImage image = new MagickImage();
							image.Ping(file);
							(cachedItem.Width, cachedItem.Height) = ImageWorker.GetDisplayDimensions(image);
							dirty = true;
						}
						catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException)
						{
							logger.LogWarning(ex, "Unable to read image dimensions for {Path}", file.FullName);
						}
					}
					mediaList.Add(file.Name, cachedItem);
				}
			}

			// Queue the icon-generating jobs for folders
			foreach (FileInfo file in iconWork)
            {
				String ext = file.Extension.ToLower().Trim('.');
				if (Configuration.Get.Image.Ext.Contains(ext))
				{
					mediaProcessingTasks.Add(imageWorker.ProcessAsync(file, progress, Media.Type.Icon));
				}
				else if (Configuration.Get.Video.Ext.Contains(ext))
				{
					mediaProcessingTasks.Add(videoWorker.ProcessAsync(file, progress, Media.Type.Icon));
				}
			}

			// Queue the thumbnail-generating jobs for files
			foreach (FileInfo file in thumbWork)
			{
				String ext = file.Extension.ToLower().Trim('.');
				if (Configuration.Get.Image.Ext.Contains(ext))
				{
					mediaProcessingTasks.Add(imageWorker.ProcessAsync(file, progress, Media.Type.Thumb));
				}
				else if (Configuration.Get.Video.Ext.Contains(ext))
				{
					mediaProcessingTasks.Add(videoWorker.ProcessAsync(file, progress, Media.Type.Thumb));
				}
			}

			foreach (MediaResult mediaResult in await Task.WhenAll(mediaProcessingTasks))
			{
				dirty = true;

				string ext = mediaResult.File.Extension.ToLower().Trim('.');
				if (mediaResult.Type == Media.Type.Thumb)
				{
					DirItem item = new DirItem
					{
						Title = mediaResult.Title,
						Updated = mediaResult.PreviewUnavailable ? mediaResult.File.LastWriteTimeUtc : DateTime.Now,
						PreviewUnavailable = mediaResult.PreviewUnavailable
					};
					if (Configuration.Get.Image.Ext.Contains(ext))
					{
						item.Type = "image";
						item.Width = mediaResult.Width;
						item.Height = mediaResult.Height;
					}
					else if (Configuration.Get.Video.Ext.Contains(ext))
					{
						item.Type = "video";
					}
					else
					{
						item.Type = "unknown";
					}

					mediaList.Add(mediaResult.File.Name, item);
				}
				else if (mediaResult.PreviewUnavailable)
				{
					string key = Path.GetRelativePath(Configuration.Get.GalleryPath, mediaResult.File.FullName).Replace('\\', '/');
					mediaList[key].PreviewUnavailable = true;
					mediaList[key].Updated = mediaResult.File.LastWriteTimeUtc;
				}

				if (mediaResult.PreviewUnavailable) continue;

				try
				{
					using (FileStream fs = mediaResult.File.GetAlternateDataStream(Configuration.Get.MetaFile, FileMode.OpenOrCreate).OpenWrite())
					{
						fs.SetLength(0);
						fs.Flush(); // delete the contents of the file first
						byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new FileMeta()
						{
							Caption = mediaResult.Caption
						}));
						fs.Write(bytes, 0, bytes.Length);
					}
				}
				catch (IOException ioEx) when (IsDiskFull(ioEx))
				{
					logger.LogError(ioEx, "Disk full while writing metadata ADS for {Path} on volume {Volume}",
						mediaResult.File.FullName, Path.GetPathRoot(mediaResult.File.FullName));
				}
				catch (IOException ioEx)
				{
					logger.LogError(ioEx, "I/O error writing metadata ADS for {Path}. Verify the volume supports " +
						"NTFS alternate data streams and the app has write permission", mediaResult.File.FullName);
				}
			}

			// Save our dir list to a file
			if (dirty)
			{
				string metaFilePath = Path.Combine(workDirectory.FullName, Configuration.Get.MetaFile);
				try
                {
					File.WriteAllText(metaFilePath, JsonSerializer.Serialize(mediaList));
				}
				catch (IOException e) when (IsDiskFull(e))
                {
					logger.LogError(e, "Disk full while writing directory index {Path} on volume {Volume}",
						metaFilePath, Path.GetPathRoot(metaFilePath));
				}
				catch (IOException e)
                {
					logger.LogError(e, "Failed to write directory index {Path}. Check that the app " +
						"has write permission to the gallery directory", metaFilePath);
				}
			}

			progress.FinishedWork(workDirectory.FullName);

			return mediaList;
		}

		/// <summary>
		/// Finds the first media file in a directory or its subdirectories; returns null if nothing found
		/// </summary>
		/// <param name="directory"></param>
		/// <returns></returns>
		public FileInfo FindFirst(DirectoryInfo directory)
		{
			foreach (FileInfo file in directory.GetFiles())
			{
				String ext = file.Extension.ToLower().Trim('.');
				if (Configuration.Get.Image.Ext.Contains(ext) || Configuration.Get.Video.Ext.Contains(ext))
				{
					return file;
				}
			}

			foreach (DirectoryInfo dir in directory.GetDirectories())
			{
				FileInfo file = FindFirst(dir);
				if (file != null)
				{
					return file;
				}
			}

			return null;
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

		/// <summary>
		/// JSON Data for a directory object
		/// </summary>
		public class DirItem
		{
			public string Type { get; set; }
			public string Title { get; set; }
			public DateTime Updated { get; set; }
			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
			public bool PreviewUnavailable { get; set; }
			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
			public int Width { get; set; }
			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
			public int Height { get; set; }
		}

		/// <summary>
		/// JSON Data for a file's metadata
		/// </summary>
		public class FileMeta
        {
			public string Caption { get; set; }
        }
	}
}
