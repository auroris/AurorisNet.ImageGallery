# ImageGallery

ASP.NET Core 10 web gallery for a Windows NTFS folder of photos and videos. Stores generated thumbnails, icons, and IPTC captions in NTFS Alternate Data Streams (`photo.jpg:thumbnail.jpg`, etc.)—no database to maintain, and files keep their cache when moved or renamed on NTFS.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Windows](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/windows.svg)

Generates photo thumbnails/icons on first visit with ImageMagick and silent MP4 video previews/still icons with the bundled `AurorisNet.ImageGallery.FFmpeg` tools (H.264, HEVC, VP8, VP9, AV1).

## Install

Requires Windows x64, an NTFS gallery folder writable by the app account, and the .NET 10 ASP.NET Core runtime (or IIS Hosting Bundle).

1. Download and extract `ImageGallery-<version>-win-x64.zip` from [Releases](https://github.com/auroris/AurorisNet.ImageGallery/releases).
2. Set `GalleryPath` in `appsettings.json`.
3. Run `ImageGallery.exe` (or point an IIS site at the folder) and open `Default.htm`.

> **Access control:** ImageGallery has no built-in authentication—put private folders behind IIS authentication or a network boundary.

## Settings (`appsettings.json`)

| Setting | Default | Effect |
|---|---|---|
| `GalleryPath` | `DemoGallery` | Root media folder (local or UNC path on NTFS). |
| `TempDir` | `temp` | Writable scratch directory for ImageMagick/FFmpeg. |
| `Images` / `Videos` | `["jpg", "png"]` / `["mp4"]` | Extensions to scan (e.g. add `"mov"`, `"webm"`, `"mkv"`). |
| `Thumbnail:width` | `260` | Thumbnail width in px (cropped to 4:3 or 8:9). |
| `Icon:width`, `Icon:height` | `100`, `100` | Max icon dimensions in px. |
| `Thumbnail:name`, `Icon:name`, `MetaFile` | `thumbnail`, `icon`, `_meta.json` | Stream and folder index names. |
| `MediaClipStart`, `MediaClipLength` | `10`, `15` | Video preview start offset and duration (seconds). |
| `RegenerateThumbnails` | `false` | Forces regeneration on folder visit (reset to `false` afterward). |

## Documentation

- [API routes, NTFS stream notes, developing, and releases](docs/developing.md)

## License

[MIT](LICENSE). Bundled FFmpeg tools are GPL-2.0-or-later ([third-party notices](THIRD-PARTY-NOTICES.md)).
