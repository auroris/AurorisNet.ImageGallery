using System;
using System.Collections.Generic;
using System.IO;
using ImageGallery.Workers;
using Microsoft.AspNetCore.Mvc;
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
    /// <b>GET /api/dir/status/{path}</b> — returns the current completion percentage (0–100) from
    /// the <see cref="ProgressWorker"/> memory-mapped file. Not cached; intended to be polled by
    /// the frontend while thumbnail generation is in progress.
    /// </para>
    /// </summary>
    [Route("api/dir")]
    [ApiController]
    public class DirectoryInformationController : ControllerBase
    {
        /// <summary>Returns the media index for the gallery root directory.</summary>
        /// <response code="200">Dictionary of relative path → <see cref="DirectoryWorker.DirItem"/> for all media files and subdirectories.</response>
        /// <response code="404">Gallery root not found.</response>
        /// <response code="409">Thumbnail generation is already in progress for this directory; retry shortly.</response>
        [HttpGet]
        [Produces("application/json")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(SortedDictionary<string, DirectoryWorker.DirItem>), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        public Task<IActionResult> Get()
        {
            return Get("");
        }

        /// <summary>
        /// Returns the media index for a gallery subdirectory. Scans the directory, generates any
        /// missing thumbnails and icons, then returns the complete index. This request blocks until
        /// generation is complete; poll <c>/api/dir/status/{path}</c> for progress.
        /// </summary>
        /// <param name="requestPath">Gallery-relative path to the directory (e.g. <c>2024/Spring</c>).</param>
        /// <response code="200">Dictionary of filename → <see cref="DirectoryWorker.DirItem"/> for all media files and subdirectories.</response>
        /// <response code="404">Directory not found or outside the gallery root.</response>
        /// <response code="409">Thumbnail generation is already in progress for this directory; retry shortly.</response>
        [HttpGet("{*requestPath}")]
        [Produces("application/json")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [ProducesResponseType(typeof(SortedDictionary<string, DirectoryWorker.DirItem>), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> Get(String requestPath)
        {
            String reqPath = requestPath.Replace('/', Path.DirectorySeparatorChar);
            // Normalize both paths to prevent traversal via ../ sequences
            String galleryRoot = Path.GetFullPath(Configuration.Get.GalleryPath);
            String realPath = Path.GetFullPath(Path.Combine(Configuration.Get.GalleryPath, reqPath));

            if ((realPath.Equals(galleryRoot, StringComparison.OrdinalIgnoreCase)
                || realPath.StartsWith(galleryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && Directory.Exists(realPath))
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

            return NotFound();
        }

        /// <summary>Returns the thumbnail-generation progress percentage for the gallery root directory.</summary>
        /// <response code="200">Integer 0–100 representing the completion percentage.</response>
        /// <response code="404">Gallery root directory not found.</response>
        [HttpGet("status")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(int), 200)]
        [ProducesResponseType(404)]
        public IActionResult Status()
        {
            return Status("");
        }

        /// <summary>
        /// Returns the thumbnail-generation progress percentage for a gallery subdirectory.
        /// Reads the completion byte from the <see cref="ProgressWorker"/> memory-mapped file.
        /// Returns 0 when no generation is currently running.
        /// </summary>
        /// <param name="requestPath">Gallery-relative path to the directory (e.g. <c>2024/Spring</c>).</param>
        /// <response code="200">Integer 0–100 representing the completion percentage.</response>
        /// <response code="404">Directory not found or outside the gallery root.</response>
        [HttpGet("status/{*requestPath}")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(int), 200)]
        [ProducesResponseType(404)]
        public IActionResult Status(String requestPath)
        {
            String reqPath = requestPath.Replace('/', Path.DirectorySeparatorChar);
            // Normalize both paths to prevent traversal via ../ sequences
            String galleryRoot = Path.GetFullPath(Configuration.Get.GalleryPath);
            String realPath = Path.GetFullPath(Path.Combine(Configuration.Get.GalleryPath, reqPath));

            if ((realPath.Equals(galleryRoot, StringComparison.OrdinalIgnoreCase)
                || realPath.StartsWith(galleryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && Directory.Exists(realPath))
            {
                using ProgressWorker progress = new ProgressWorker(new DirectoryInfo(realPath));
                return Ok(progress.GetProgress());
            }

            return NotFound();
        }
    }
}
