# ImageGallery

An ASP.NET Core web application that serves a self-indexing photo and video gallery from a local directory or network share. Photo technicians populate EXIF/IPTC metadata and drop files onto the share; the app generates thumbnails automatically on first request and caches everything as NTFS Alternate Data Streams (ADS) on the source files — no separate database or sidecar files required.

## Features

- **Zero-copy thumbnails** — thumbnails, icons, and per-file metadata are stored as NTFS ADS on the original files, leaving the source tree clean
- **On-demand generation** — thumbnails are generated the first time a directory is requested; subsequent requests are served from cache
- **Image support** — crop-centred thumbnails (landscape 4:3 or portrait 8:9), static PNG icons via ImageMagick
- **Video support** — short animated MP4 clips as thumbnails, static PNG frame as icon, via FFmpeg
- **IPTC metadata** — reads Object Name (title) and Caption/Abstract from EXIF/IPTC tags; falls back to filename
- **Live progress** — frontend polls `/api/dir/status` while generation is in progress
- **Swagger UI** — full OpenAPI documentation available at `/swagger` in Development

## Requirements

| Requirement | Version |
|---|---|
| .NET | 10.0 |
| Windows | Required (NTFS ADS, Memory-Mapped Files) |
| ffmpeg / ffprobe | Bundled in `ffmpeg\` directory |

## Getting Started

### 1. Configure `appsettings.json`

```json
{
  "GalleryPath": "\\\\server\\share\\Photos",
  "TempDir":     "C:\\Temp\\ImageGallery",
  "LogDir":      "C:\\Logs\\ImageGallery"
}
```

| Key | Default | Description |
|---|---|---|
| `GalleryPath` | `DemoGallery` | Root directory of the photo share. Relative paths resolve from the app base directory. Falls back to the bundled `DemoGallery` if the path does not exist. |
| `TempDir` | `temp` | Working directory for FFmpeg and ImageMagick during thumbnail generation. |
| `LogDir` | `.` | Directory for log4net output. |
| `MetaFile` | `_meta.json` | ADS stream name used for per-file metadata and the per-directory index. |
| `RegenerateThumbnails` | `false` | Set to `true` to force regeneration of all thumbnails and icons on next request. |
| `Images` | `["jpg","png"]` | File extensions (without dot) treated as images. |
| `Videos` | `["mp4"]` | File extensions (without dot) treated as videos. |
| `MediaClipStart` | `10` | Seconds into the video where the thumbnail clip begins. |
| `MediaClipLength` | `15` | Duration in seconds of the thumbnail clip. |
| `Thumbnail:width` / `height` | `260` / `260` | Pixel dimensions of generated thumbnail images. |
| `Icon:width` / `height` | `100` / `100` | Pixel dimensions of generated icon images. |

### 2. Run

```bash
dotnet run --project ImageGallery
```

Navigate to `http://localhost:5000` (or the configured port). The gallery root is served from `Default.htm`.

In Development, the Swagger UI is available at `http://localhost:5000/swagger`.

## Project Structure

```
ImageGallery/
├── App/
│   ├── Configuration.cs       # Singleton config accessor (reads appsettings.json)
│   ├── Media.cs               # Crop math, dimension helpers, IPTC metadata reading
│   ├── MediaResult.cs         # DTO returned by image/video workers
│   └── Mime.cs                # Extension → MIME type lookup
├── Controllers/
│   ├── DirectoryInformationController.cs  # GET /api/dir, GET /api/dir/status
│   └── MediaController.cs                 # GET /api/media/{thumb,icon,meta,file}
├── Workers/
│   ├── DirectoryWorker.cs     # Orchestrates scanning and processing for a directory
│   ├── ImageWorker.cs         # Thumbnail/icon generation via ImageMagick
│   ├── VideoWorker.cs         # Thumbnail/icon generation via FFmpeg
│   └── ProgressWorker.cs      # Tracks generation progress via Memory-Mapped File
├── wwwroot/
│   ├── Default.htm            # Single-page gallery frontend
│   ├── App.js                 # Gallery logic (jQuery + Fancybox 6)
│   └── style.css              # Gallery styles
├── ffmpeg/
│   ├── ffmpeg.exe             # Bundled FFmpeg binary
│   └── ffprobe.exe            # Bundled FFprobe binary
├── DemoGallery/               # Sample images used as fallback gallery in Development
├── appsettings.json
└── log4net.xml                # log4net configuration
```

## API Reference

Full interactive documentation is available at `/swagger` when running in Development. A brief summary:

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/dir` | Returns the media index for the gallery root |
| `GET` | `/api/dir/{path}` | Returns the media index for a subdirectory; generates missing thumbnails |
| `GET` | `/api/dir/status` | Returns generation progress (0–100) for the gallery root |
| `GET` | `/api/dir/status/{path}` | Returns generation progress (0–100) for a subdirectory |
| `GET` | `/api/media/thumb/{path}` | Serves the animated MP4 thumbnail clip from the file's ADS |
| `GET` | `/api/media/icon/{path}` | Serves the static PNG icon from the file's ADS |
| `GET` | `/api/media/meta/{path}` | Returns per-file JSON metadata `{ "Caption": "..." }` |
| `GET` | `/api/media/file/{path}` | Serves the original source file |

### Directory index response

`GET /api/dir/{path}` returns a JSON object keyed by filename (or subdirectory representative path):

```json
{
  "2024/holiday.jpg": {
    "type":    "image",
    "title":   "Sunset at the beach",
    "updated": "2024-08-15T18:30:00"
  },
  "2024/clip.mp4": {
    "type":    "video",
    "title":   "Diving footage",
    "updated": "2024-08-16T09:00:00"
  },
  "Archive/first-file.jpg": {
    "type":    "dir",
    "title":   "Archive",
    "updated": "2024-01-01T00:00:00"
  }
}
```

Responds `409 Conflict` if thumbnail generation is already in progress for the directory; the frontend retries automatically.

## How Thumbnails Work

1. The first `GET /api/dir/{path}` for a directory triggers `DirectoryWorker.ProcessAsync`.
2. Files new since the last scan are queued for `ImageWorker` or `VideoWorker`.
3. Workers write output into named **NTFS Alternate Data Streams** on the source file:
   - `thumbnail` stream — animated MP4 clip (video) or cropped JPEG (image)
   - `icon` stream — static PNG frame at icon dimensions
   - `_meta.json` stream — `{ "Caption": "..." }` from IPTC metadata
4. A per-directory `_meta.json` index file is written to the directory on completion.
5. Subsequent requests read directly from ADS; no regeneration occurs unless `RegenerateThumbnails: true`.

## Dependencies

| Package | Purpose |
|---|---|
| `Magick.NET-Q16-x64` 14.x | Image thumbnail/icon generation |
| `Xabe.FFmpeg` 6.x | Video thumbnail/icon generation |
| `MetadataExtractor` 2.x | EXIF/IPTC metadata reading |
| `Trinet.Core.IO.Ntfs` 4.x | NTFS Alternate Data Stream access |
| `log4net` 3.x | Logging |
| `Swashbuckle.AspNetCore` 10.x | OpenAPI / Swagger documentation |

## Known Limitations

- **Windows only** — NTFS ADS and Memory-Mapped File progress tracking are Windows-specific.
- **Same-machine progress** — the `ProgressWorker` MMF is only accessible from the same machine as the server.
- **Path security** — the gallery root check uses `StartsWith` on the normalized path; UNC paths and symlinks should be tested in your environment.
- **Concurrent directory requests** — a second request for a directory already being processed returns `409`; the frontend polls and retries.
