using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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
    public class DirectoryWorker
    {
        private readonly DirectoryInfo workDirectory;

		public DirectoryWorker(DirectoryInfo path)
        {
			workDirectory = path;
		}

		/// <summary>
		/// Scans the directory, generates missing thumbnails and icons, and returns the updated
		/// media index. Returns <c>null</c> if generation is already in progress for this directory.
		/// </summary>
		/// <param name="overwrite">
		///   When <c>true</c>, regenerates thumbnails and icons for all files even if they already
		///   exist. Controlled by the <c>RegenerateThumbnails</c> appsetting.
		/// </param>
		public async Task<SortedDictionary<string, DirItem>> ProcessAsync(bool overwrite)
		{
			bool dirty = false;
			ProgressWorker progress = new ProgressWorker(workDirectory);
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

				string iconRelativePath = iconFullPath.FullName.Substring(Configuration.Get.GalleryPath.Length + 1).Replace('\\', '/');

				if (!mediaListOld.ContainsKey(iconRelativePath) // If the old media list doesn't contain this dir (because it's new), or
					|| overwrite) // We're instructed to re-generate all data
				{
					progress.AddWork(Media.GetIconPath(iconFullPath));
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
					mediaList.Add(iconRelativePath, mediaListOld[iconRelativePath]);
                }
			}

			// List files in the dir
			foreach (FileInfo file in workDirectory.GetFiles())
			{
				if (!mediaListOld.ContainsKey(file.Name) // If the old media list doesn't contain this file (because it's new), or
					|| overwrite) // We're instructed to re-generate all data
				{
					String ext = file.Extension.ToLower().Trim('.');
					if (Configuration.Get.Image.Ext.Contains(ext) || Configuration.Get.Video.Ext.Contains(ext))
					{
						progress.AddWork(Media.GetIconPath(file));
						thumbWork.Add(file);
					}
				}
				else
				{
					// No updates, simply copy the old data to the new
					mediaList.Add(file.Name, mediaListOld[file.Name]);
				}
			}

			// Queue the icon-generating jobs for folders
			foreach (FileInfo file in iconWork)
            {
				String ext = file.Extension.ToLower().Trim('.');
				if (Configuration.Get.Image.Ext.Contains(ext))
				{
					ImageWorker worker = new ImageWorker();
					mediaProcessingTasks.Add(worker.ProcessAsync(file, progress, Media.Type.Icon));
				}
				else if (Configuration.Get.Video.Ext.Contains(ext))
				{
					VideoWorker worker = new VideoWorker();
					mediaProcessingTasks.Add(worker.ProcessAsync(file, progress, Media.Type.Icon));
				}
			}

			// Queue the thumbnail-generating jobs for files
			foreach (FileInfo file in thumbWork)
			{
				String ext = file.Extension.ToLower().Trim('.');
				if (Configuration.Get.Image.Ext.Contains(ext))
				{
					ImageWorker worker = new ImageWorker();
					mediaProcessingTasks.Add(worker.ProcessAsync(file, progress, Media.Type.Thumb));
				}
				else if (Configuration.Get.Video.Ext.Contains(ext))
				{
					VideoWorker worker = new VideoWorker();
					mediaProcessingTasks.Add(worker.ProcessAsync(file, progress, Media.Type.Thumb));
				}
			}

			// Result of jobs processing
			try
            {
				// Wait for all media processing tasks to complete
				await Task.WhenAll(mediaProcessingTasks);

				foreach (Task<MediaResult> task in mediaProcessingTasks)
                {
					dirty = true;

                    MediaResult mediaResult = task.Result;
					string ext = mediaResult.File.Extension.ToLower().Trim('.');
					string path = "";
					string type = "";

					if (mediaResult.Type == Media.Type.Thumb)
					{
						path = mediaResult.File.Name;
						if (Configuration.Get.Image.Ext.Contains(ext))
						{
							type = "image";
						}
						else if (Configuration.Get.Video.Ext.Contains(ext))
						{
							type = "video";
						}
						else
						{
							type = "unknown";
						}

						mediaList.Add(path, new DirItem
						{
							Title = mediaResult.Title,
							Type = type,
							Updated = DateTime.Now
						});
					}

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
			}
			catch (AggregateException e)
            {
				// Error handling: Log all errors generated by the workers
				StringBuilder sb = new StringBuilder();

				for (int j = 0; j < e.InnerExceptions.Count; j++)
				{
					progress.FinishedWork("Error");
					sb.AppendLine("\n-------------------------------------------------\n" + e.InnerExceptions[j].ToString());
				}

				LogManager.GetLogger("DirectoryWorker").Error(sb.ToString());
			}

			// Save our dir list to a file
			if (dirty)
			{
				try
                {
					File.WriteAllText(Path.Combine(workDirectory.FullName, Configuration.Get.MetaFile), JsonSerializer.Serialize(mediaList));
				}
				catch (IOException e)
                {
					LogManager.GetLogger("DirectoryWorker").Error(e.ToString());
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
		/// JSON Data for a directory object
		/// </summary>
		public class DirItem
		{
			public string Type { get; set; }
			public string Title { get; set; }
			public DateTime Updated { get; set; }
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
