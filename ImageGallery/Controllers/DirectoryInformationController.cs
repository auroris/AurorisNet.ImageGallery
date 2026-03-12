using System;
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
        // GET: /api/dir
        [HttpGet]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
        public Task<IActionResult> Get()
        {
            return Get("");
        }

        // GET: /api/dir/*
        [HttpGet("{*requestPath}")]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
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

        // GET: /api/dir/status
        [HttpGet("status")]
        public IActionResult Status()
        {
            return Status("");
        }

        // GET: /api/dir/status/*
        [HttpGet("status/{*requestPath}")]
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
