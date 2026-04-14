using System;
using System.Collections.Generic;
using System.IO;
using ImageGallery.Workers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ImageGallery.Controllers
{
    /// <summary>
    /// Returns directory listings and thumbnail-generation progress for the gallery.
    /// <para>
    /// <b>GET /api/dir/{path}</b> — triggers <see cref="Workers.DirectoryWorker"/> to scan the
    /// directory, generate any missing thumbnails, and return the resulting media index as JSON.
    /// Responses are cached for 7 days.
    /// </para>
    /// <para>
    /// <b>GET /api/dir/status/{path}</b> — returns the current completion percentage and last
    /// completed filename from the <see cref="ProgressWorker"/> memory-mapped file. Not cached;
    /// intended to be polled by the frontend while thumbnail generation is in progress.
    /// </para>
    /// </summary>
    [Route("api/dir")]
    [ApiController]
    public class DirectoryInformationController : ControllerBase
    {
        private readonly ILogger<DirectoryInformationController> _logger;

        public DirectoryInformationController(ILogger<DirectoryInformationController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Returns the media index for a gallery directory. Scans the directory, generates any
        /// missing thumbnails and icons, then returns the complete index. This request blocks until
        /// generation is complete; poll <c>/api/dir/status/{path}</c> for progress.
        /// Omit <paramref name="requestPath"/> to retrieve the gallery root.
        /// </summary>
        /// <param name="requestPath">Gallery-relative path to the directory (e.g. <c>2024/Spring</c>).</param>
        /// <response code="200">Dictionary of filename → <see cref="DirectoryWorker.DirItem"/> for all media files and subdirectories.</response>
        /// <response code="404">Directory not found or outside the gallery root.</response>
        /// <response code="409">Thumbnail generation is already in progress for this directory; retry shortly.</response>
        [HttpGet]
        [HttpGet("{*requestPath}")]
        [Produces("application/json")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(SortedDictionary<string, DirectoryWorker.DirItem>), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> Get(string requestPath = "")
        {
            String reqPath = requestPath.Replace('/', Path.DirectorySeparatorChar);
            // Normalize both paths to prevent traversal via ../ sequences
            String galleryRoot = Path.GetFullPath(Configuration.Get.GalleryPath);
            String realPath = Path.GetFullPath(Path.Combine(Configuration.Get.GalleryPath, reqPath));

            if ((realPath.Equals(galleryRoot, StringComparison.OrdinalIgnoreCase)
                || realPath.StartsWith(galleryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && Directory.Exists(realPath))
            {
                try
                {
                    DirectoryWorker worker = new DirectoryWorker(new DirectoryInfo(realPath));
                    var result = await worker.ProcessAsync(Configuration.Get.RegenerateThumbnails);
                    if (result == null)
                    {
                        // Generation already in progress for this directory
                        return Conflict();
                    }
                    return Ok(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing directory: {Path}", realPath);
                    return StatusCode(500, new { error = "Failed to process directory.", detail = ex.Message });
                }
            }

            return NotFound();
        }

        /// <summary>
        /// Returns the thumbnail-generation progress for a gallery directory.
        /// Reads the completion percentage and last completed filename from the
        /// <see cref="ProgressWorker"/> memory-mapped file. Both fields are zero/empty when idle.
        /// Omit <paramref name="requestPath"/> to query the gallery root.
        /// </summary>
        /// <param name="requestPath">Gallery-relative path to the directory (e.g. <c>2024/Spring</c>).</param>
        /// <response code="200">Object with <c>percent</c> (0–100) and <c>currentWork</c> (last completed filename, or empty string when idle).</response>
        /// <response code="404">Directory not found or outside the gallery root.</response>
        [HttpGet("status")]
        [HttpGet("status/{*requestPath}")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ProgressWorker.ProgressStatus), 200)]
        [ProducesResponseType(404)]
        public IActionResult Status(string requestPath = "")
        {
            String reqPath = requestPath.Replace('/', Path.DirectorySeparatorChar);
            // Normalize both paths to prevent traversal via ../ sequences
            String galleryRoot = Path.GetFullPath(Configuration.Get.GalleryPath);
            String realPath = Path.GetFullPath(Path.Combine(Configuration.Get.GalleryPath, reqPath));

            if ((realPath.Equals(galleryRoot, StringComparison.OrdinalIgnoreCase)
                || realPath.StartsWith(galleryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && Directory.Exists(realPath))
            {
                try
                {
                    using ProgressWorker progress = new ProgressWorker(new DirectoryInfo(realPath));
                    return Ok(progress.GetStatus());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading progress for directory: {Path}", realPath);
                    return StatusCode(500, new { error = "Failed to read progress.", detail = ex.Message });
                }
            }

            return NotFound();
        }
    }
}
