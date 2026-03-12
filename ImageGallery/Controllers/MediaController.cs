using System;
using System.IO;
using ImageGallery.Workers;
using Microsoft.AspNetCore.Mvc;
using Trinet.Core.IO.Ntfs;

namespace ImageGallery.Controllers
{
    /// <summary>
    /// Serves individual media files and their derivatives (thumbnails, icons, per-file metadata)
    /// from NTFS alternate data streams on the source files. All responses except metadata are
    /// cached for 7 days.
    /// </summary>
    [Route("api/media")]
    [ApiController]
    public class MediaController : ControllerBase
    {
        public MediaController()
        {
        }

        // GET: /api/media/thumb/path
        [HttpGet("thumb/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IActionResult GetThumbnail(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            return GetFile(file, Media.GetThumbName(file));
        }

        // GET: /api/media/icon/path
        [HttpGet("icon/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IActionResult GetIcon(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            return GetFile(file, Media.GetIconName(file));
        }

        // GET: /api/media/meta/path
        [HttpGet("meta/{*requestPath}")]
        public IActionResult GetMeta(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            return GetFile(file, Configuration.Get.MetaFile);
        }

        // GET: /api/media/file/path
        [HttpGet("file/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IActionResult GetFile(String requestPath)
        {
            return GetFile(GetFileInfo(requestPath));
        }

        /// <summary>
        /// Dispatcher that serves either an NTFS alternate data stream or the file itself.
        /// When <paramref name="dataStream"/> is provided the named ADS is opened; otherwise
        /// the file is served directly via <see cref="PhysicalFile"/>.
        /// </summary>
        private IActionResult GetFile(FileInfo file, string dataStream = null)
        {
            if (file == null) { return NotFound(); }

            if (dataStream != null && file.Exists && file.AlternateDataStreamExists(dataStream))
            {
                return File(file.GetAlternateDataStream(dataStream, FileMode.Open).OpenRead(), Mime.GetType(dataStream));
            }

            if (dataStream == null && file.Exists)
            {
                return PhysicalFile(file.FullName, Mime.GetType(file.Name));
            }

            return NotFound();
        }

        /// <summary>
        /// Resolves a URL-style request path to a <see cref="FileInfo"/> within the configured
        /// gallery root. Returns <c>null</c> if the path resolves outside the gallery root or
        /// the file does not exist, preventing directory traversal attacks.
        /// </summary>
        private FileInfo GetFileInfo(string requestPath)
        {
            String reqPath = requestPath.Replace('/', Path.DirectorySeparatorChar);
            // Normalize both paths to prevent traversal via ../ sequences
            String galleryRoot = Path.GetFullPath(Configuration.Get.GalleryPath) + Path.DirectorySeparatorChar;
            String realPath = Path.GetFullPath(Path.Combine(Configuration.Get.GalleryPath, reqPath));

            if (realPath.StartsWith(galleryRoot, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(realPath))
            {
                return new FileInfo(realPath);
            }

            return null;
        }
    }
}
