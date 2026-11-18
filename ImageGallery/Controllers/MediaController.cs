using System;
using System.IO;
using ImageGallery.Workers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<MediaController> _logger;

        public MediaController(ILogger<MediaController> logger)
        {
            _logger = logger;
        }

        /// <summary>Returns the image thumbnail or silent MP4 preview stored in the file's NTFS ADS.</summary>
        /// <param name="requestPath">Gallery-relative path to the file (e.g. <c>2024/photo.jpg</c>).</param>
        /// <response code="200">The thumbnail video stream.</response>
        /// <response code="404">File not found, outside the gallery root, or thumbnail not yet generated.</response>
        [HttpGet("thumb/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetThumbnail(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            if (file == null) return NotFound();
            string extension = file.Extension.TrimStart('.').ToLowerInvariant();
            string contentType = Configuration.Get.Video.Ext.Contains(extension) ? "video/mp4" : Mime.GetType(file.Name);
            return GetFile(file, Media.GetThumbName(file), contentType);
        }

        /// <summary>Returns the static PNG icon for a media file, read from its NTFS ADS.</summary>
        /// <param name="requestPath">Gallery-relative path to the file (e.g. <c>2024/photo.jpg</c>).</param>
        /// <response code="200">The icon image stream.</response>
        /// <response code="404">File not found, outside the gallery root, or icon not yet generated.</response>
        [HttpGet("icon/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetIcon(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            return GetFile(file, Media.GetIconName(file));
        }

        /// <summary>
        /// Returns the per-file JSON metadata (IPTC caption) stored in the file's NTFS ADS.
        /// The response body is a <c>FileMeta</c> object: <c>{ "Caption": "..." }</c>.
        /// </summary>
        /// <param name="requestPath">Gallery-relative path to the file (e.g. <c>2024/photo.jpg</c>).</param>
        /// <response code="200">JSON metadata for the file.</response>
        /// <response code="404">File not found or metadata ADS not yet written.</response>
        [HttpGet("meta/{*requestPath}")]
        [Produces("application/json")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public IActionResult GetMeta(String requestPath)
        {
            FileInfo file = GetFileInfo(requestPath);
            return GetFile(file, Configuration.Get.MetaFile);
        }

        /// <summary>Returns the original source file (image or video) directly from the gallery share.</summary>
        /// <param name="requestPath">Gallery-relative path to the file (e.g. <c>2024/photo.jpg</c>).</param>
        /// <response code="200">The raw source file stream.</response>
        /// <response code="404">File not found or outside the gallery root.</response>
        [HttpGet("file/{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(PhysicalFileResult), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetFile(String requestPath)
        {
            return GetFile(GetFileInfo(requestPath));
        }

        // Serves either an NTFS alternate data stream (when dataStream is provided) or the file
        // itself. Returns 404 when the file or stream does not exist.
        private IActionResult GetFile(FileInfo file, string dataStream = null, string contentType = null)
        {
            if (file == null) { return NotFound(); }

            try
            {
                if (dataStream != null && file.Exists && file.AlternateDataStreamExists(dataStream))
                {
                    return File(file.GetAlternateDataStream(dataStream, FileMode.Open).OpenRead(), contentType ?? Mime.GetType(dataStream));
                }

                if (dataStream == null && file.Exists)
                {
                    return PhysicalFile(file.FullName, Mime.GetType(file.Name), enableRangeProcessing: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file: {File}, stream: {Stream}", file.FullName, dataStream);
                return StatusCode(500, new { error = "Failed to read file.", detail = ex.Message });
            }

            return NotFound();
        }

        // Resolves a URL-style request path to a FileInfo within the configured gallery root.
        // Returns null if the path escapes the gallery root, preventing directory traversal.
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
