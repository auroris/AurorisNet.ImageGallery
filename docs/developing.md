# API, NTFS streams, and developing

## API

Swagger UI is available at `/swagger` in the `Development` environment.

| Route | Returns |
|---|---|
| `GET /api/dir` and `GET /api/dir/{path}` | Folder index (generates missing streams; `409` while generating, `404` outside gallery). |
| `GET /api/dir/status` and `GET /api/dir/status/{path}` | `{ "percent": 0-100, "currentWork": "..." }`. |
| `GET /api/media/thumb/{path}` / `icon/{path}` / `meta/{path}` | Thumbnail stream, icon stream, or `{ "Caption": "..." }` (`404` if not yet generated). |
| `GET /api/media/file/{path}` | Original file with HTTP range support for video seeking. |

Videos that fail preview generation stay clickable with `"previewUnavailable": true` in `_meta.json`.

## NTFS stream notes

- Inspect streams in PowerShell with `Get-Item .\photo.jpg -Stream *`.
- Copying files to non-NTFS volumes, ZIPs, or cloud sync drops streams (they regenerate on next visit).
- Replacing a file with the same name keeps its old thumbnail unless `RegenerateThumbnails` is enabled or streams are cleared.
- `CleanImageGallery.ps1` deletes `_meta.json` and **all alternate data streams** under the current folder (including streams from other apps).
- Responses send a 7-day cache header; logs write to console and `application.log` (1 MiB rolling, 1 backup).

## Developing

Requires Windows x64 and the .NET 10 SDK.

```powershell
dotnet build ImageGallery -c Release
dotnet run --project ImageGallery
dotnet publish ImageGallery/ImageGallery.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
./tests/Test-VideoFallback.ps1 -PublishDirectory artifacts/publish
```

`dotnet run` listens on `http://localhost:5000`; set `GalleryPath` before running.

## Releases

Dependabot tracks `Magick.NET` and `AurorisNet.ImageGallery.FFmpeg`; passing updates publish as `ImageGallery-<version>-win-x64.zip` + `.sha256` on GitHub Releases. See the shared [release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md).
