# ImageGallery

A web gallery for a folder of photos and videos, which keeps each file's thumbnail, icon and caption in NTFS Alternate Data Streams.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Windows](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/windows.svg)

ImageGallery is an ASP.NET Core 10 application that serves a folder as a gallery in the browser. People manage the folder with their own tools: they copy files in, rename them, move them and delete them. The gallery follows, because it stores what it generates in NTFS alternate data streams on the media files:

- **Each file carries its own cache.** The thumbnail, the icon and the caption are named streams of the photo or video, such as `photo.jpg:thumbnail.jpg`. A file that is renamed or moved on NTFS keeps them, and a file that is deleted takes them with it. There is no database to keep in step with the folder.
- **Thumbnails appear on the first visit.** Opening a folder in the gallery generates what its new files lack, and shows progress while it works. Later visits read the streams.
- **Photos and videos.** A photo gets a cropped thumbnail and a small icon, made with ImageMagick. A video gets a short silent clip as its thumbnail and a still frame as its icon, made with FFmpeg. A video that FFmpeg cannot decode still appears as a link to the original file.
- **Titles and captions from the files.** The gallery reads the IPTC **Object Name** as the title and **Caption/Abstract** as the caption, and uses the file name when a file has no title.

## Install

Download `ImageGallery-<version>-win-x64.zip` from the [releases](https://github.com/auroris/AurorisNet.ImageGallery/releases) and extract it. The server needs:

- Windows, with the gallery folder on an NTFS volume;
- the .NET 10 ASP.NET Core runtime, or the .NET 10 Hosting Bundle under IIS;
- an account that can write to the gallery folder, because the streams and each folder's index are written there.

Set `GalleryPath` in `appsettings.json` to the folder to show, then start `ImageGallery.exe`, or point an IIS site at the extracted folder. The gallery is the site's `Default.htm`.

The gallery has no sign-in of its own. Everyone who can reach the site can open every file under `GalleryPath`, so put it behind IIS authentication or a network boundary when the folder is private.

## Settings

The settings are top-level keys of `appsettings.json`.

| Setting | Default | What it does |
|---|---|---|
| `GalleryPath` | `DemoGallery` | The folder the gallery shows. A relative path starts at the application folder. A UNC path such as `\\server\share\Photos` works. When the folder does not exist, the gallery shows a `DemoGallery` folder beside the application if there is one. |
| `TempDir` | `temp` | The working folder for ImageMagick and FFmpeg. A relative path starts at the application folder. The application creates it and stops at startup if it cannot write there. |
| `Images` | `["jpg", "png"]` | The file extensions, without the dot, shown as photos. |
| `Videos` | `["mp4"]` | The file extensions, without the dot, shown as videos. |
| `Thumbnail:width` | `260` | The thumbnail's width in pixels. Its height follows from the crop, described under [How it works](#how-it-works). `Thumbnail:height` is read and not used. |
| `Icon:width`, `Icon:height` | `100`, `100` | A photo's icon fits within these dimensions. A video frame uses the configured width and keeps its aspect ratio. |
| `Thumbnail:name`, `Icon:name` | `thumbnail`, `icon` | The names of the two streams, before the extension. |
| `MetaFile` | `_meta.json` | The name of each file's caption stream and of each folder's index file. |
| `MediaClipStart` | `10` | The second of a video at which its thumbnail clip starts. A video shorter than the clip's end starts at 0. |
| `MediaClipLength` | `15` | The length of the thumbnail clip in seconds. |
| `RegenerateThumbnails` | `false` | `true` generates every thumbnail and icon again each time a folder is opened. Set it back to `false` afterwards. |

The bundled FFmpeg tools can generate previews from H.264, HEVC, VP8, VP9 and AV1 video in MP4, MOV, WebM and Matroska containers. Add the extensions you want the gallery to scan to `Videos`, for example `["mp4", "mov", "webm", "mkv"]`. If the gallery cannot generate a video preview, it still shows a link to the original. Playback uses the browser's installed codecs.

The application logs to the console and writes `application.log` beside the executable. At 1 MiB it rolls the file to `application1.log` and keeps one backup. The `Logging` section controls log levels.

## How it works

1. The browser asks for a folder with `GET /api/dir/{path}`.
2. The application compares the folder's files with the folder's index, `_meta.json`. Each file the index does not list is new.
3. For each new photo it writes two streams on the file: a thumbnail, cropped around the centre to 4:3 or to 8:9, whichever is nearer the photo's shape, and an icon. For each new video it writes a thumbnail clip encoded as H.264 and a PNG icon of a frame.
4. It writes the file's caption to the file's `_meta.json` stream. The folder's `_meta.json` records titles, media types and original photo dimensions. On the first visit after upgrading, the gallery reads image headers to fill in missing dimensions in an older index.
5. The gallery shows each subfolder with the icon of the first photo or video found inside it. Folders whose names start with `$` are left out.

While a folder is being generated, a second request for it answers `409 Conflict`. The page polls `GET /api/dir/status/{path}` each second, shows the percentage and the last finished file, and asks again.

Streams look like this in PowerShell:

```powershell
Get-Item .\photo.jpg -Stream *      # :$DATA, thumbnail.jpg, icon.jpg, _meta.json
Get-Item .\clip.mp4 -Stream *       # :$DATA, thumbnail.mp4, icon.png, _meta.json
```

### What to know about the streams

- **They exist on NTFS only.** Copying a file to a FAT or exFAT drive, into a ZIP, or through most cloud-sync tools drops them. The gallery generates them again the next time the folder is opened.
- **A new file is recognised by its name.** Replacing a photo with another of the same name keeps the old thumbnail. Set `RegenerateThumbnails`, or remove the streams as shown under [Developing](#developing).
- **Progress is tracked per folder name.** Two folders with the same name in different places share one progress record, so opening one while the other is generating answers `409` until it finishes.
- **Responses are cached for a week.** The folder index, thumbnails and icons are sent with a seven-day cache header, so a browser may show a folder as it was until its cache is refreshed.

## API

Swagger UI at `/swagger` lists the full API when the application runs in the Development environment.

| Route | Returns |
|---|---|
| `GET /api/dir` and `GET /api/dir/{path}` | The index of the gallery root or of a folder, after generating what is missing. `404` for a path outside the gallery, `409` while the folder is generating. |
| `GET /api/dir/status` and `GET /api/dir/status/{path}` | `{ "percent": 0-100, "currentWork": "<last finished file>" }` for the folder. |
| `GET /api/media/thumb/{path}` | The file's thumbnail stream. |
| `GET /api/media/icon/{path}` | The file's icon stream. |
| `GET /api/media/meta/{path}` | The file's caption: `{ "Caption": "..." }`. |
| `GET /api/media/file/{path}` | The file itself. The route answers range requests, which video players use to seek. |

The thumbnail, icon and caption routes answer `404` for a file whose stream has not been written yet. The index is an object keyed by file name, or by the path of a subfolder's first media file:

```json
{
  "holiday.jpg": { "type": "image", "title": "Sunset at the beach", "updated": "2024-08-15T18:30:00", "width": 1920, "height": 1080 },
  "clip.mp4": { "type": "video", "title": "clip.mp4", "updated": "2024-08-16T09:00:00" },
  "Archive/first.jpg": { "type": "dir", "title": "Archive", "updated": "2024-01-01T00:00:00" }
}
```

A video or folder whose preview could not be generated has `"previewUnavailable": true` in the index. The gallery retries when the source video changes or `RegenerateThumbnails` is enabled.

## Supported versions

| Component | Version |
|---|---|
| .NET | 10 |
| Operating system | Windows x64 |
| File system of the gallery folder | NTFS |
| Browser | A modern browser with JavaScript enabled. The gallery serves its scripts and styles locally. Video playback depends on the browser's support for the original file. |

## Developing

You need the .NET 10 SDK on Windows.

The `AurorisNet.ImageGallery.FFmpeg` NuGet package supplies the Windows x64 executables, codec libraries and license notices. Build and publish copy them into the application's `ffmpeg` directory.

```powershell
dotnet build ImageGallery -c Release
dotnet run --project ImageGallery
# The release build, as the workflows make it
dotnet publish ImageGallery/ImageGallery.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
```

`dotnet run` listens on `http://localhost:5000`. Point `GalleryPath` at a folder of photos first; the default, `DemoGallery`, is a folder beside the application that the repository does not include.

After publishing, run `./tests/Test-VideoFallback.ps1 -PublishDirectory artifacts/publish` in PowerShell 7.4 or later on Windows. CI runs this check against a folder of playable, truncated and encrypted videos.

`CleanImageGallery.ps1` resets a gallery folder. Run it in that folder: it deletes every `_meta.json` file below it and **every alternate data stream of every file below it**, including streams other software wrote.

| Folder | Contents |
|---|---|
| `ImageGallery/App` | Settings, crop arithmetic and metadata reading. |
| `ImageGallery/Controllers` | The `/api/dir` and `/api/media` routes. |
| `ImageGallery/Workers` | Folder scanning, photo and video generation, and the progress record, a memory-mapped file named for the folder. |
| `ImageGallery/wwwroot` | The gallery page and its bundled PhotoSwipe viewer. |

## Releases

ImageGallery follows two packages: Magick.NET, which reads and writes every photo and carries ImageMagick's security fixes, and [AurorisNet.ImageGallery.FFmpeg](https://github.com/auroris/AurorisNet.ImageGallery.FFmpeg), which supplies the FFmpeg tools. Dependabot checks NuGet each week and gives each of the two a pull request of its own. `ci.yml` publishes the application, runs the video check and merges the pull request. Each week `weekly-release.yml` starts `release.yml` once for the updates merged since the last release, and it publishes `ImageGallery-<version>-win-x64.zip` and its `.sha256` on a GitHub release. A new major version of either package, such as FFmpeg 10, stays open for a person to review. Versions are `MAJOR.MINOR.PATCH`. The application ships no NuGet package.

To release by hand, run `release.yml` with a version, or leave the version empty to raise the newest one. The [release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md) describes the workflows.

## License

ImageGallery is under the [MIT license](LICENSE). The bundled FFmpeg tools are distributed under the GPL, version 2 or later. The [third-party notices](THIRD-PARTY-NOTICES.md) list the licenses for the components in a release.
