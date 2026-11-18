# Third-party components

ImageGallery's own source is under the MIT license in [LICENSE](LICENSE). A release also
contains the components below, each under its own license. This file is an index and does
not replace those licenses.

| Component | Where it is | License |
|---|---|---|
| [AurorisNet.ImageGallery.FFmpeg](https://github.com/auroris/AurorisNet.ImageGallery.FFmpeg): FFmpeg for Windows x64, built with x264, dav1d, zlib and the MinGW-w64 runtime | `ffmpeg/ffmpeg.exe`, `ffmpeg/ffprobe.exe` and the DLLs beside them | GPL version 2 or later, because the build includes x264. `ffmpeg/LICENSE.txt` holds the license texts and notices of FFmpeg and each bundled library. Each [package release](https://github.com/auroris/AurorisNet.ImageGallery.FFmpeg/releases) has the matching sources and build scripts as `ffmpeg-<version>-sources.zip`. |
| [PhotoSwipe 5.4.4](https://github.com/dimsemenov/PhotoSwipe/releases/tag/v5.4.4) | `wwwroot/lib/photoswipe-5.4.4` | MIT. The release includes the [upstream license](https://github.com/dimsemenov/PhotoSwipe/blob/v5.4.4/LICENSE) beside the scripts and stylesheet. |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) and ImageMagick | `Magick.NET-Q16-x64.dll` and its native library | Apache-2.0 (Magick.NET) and the ImageMagick license. |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | `MetadataExtractor.dll` | Apache-2.0. |
| [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet), which MetadataExtractor uses | `XmpCore.dll` | BSD 3-Clause. |
| [Trinet.Core.IO.Ntfs](https://github.com/RichardD2/NTFS-Streams) | `Trinet.Core.IO.Ntfs.dll` | BSD 2-Clause, one of the three licenses its author offers. |
| [NReco.Logging.File](https://github.com/nreco/logging) | `NReco.Logging.File.dll` | MIT, copyright (c) 2017 NReco. The MIT terms are reproduced in [LICENSE](LICENSE). |
| [Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore) | `Swashbuckle.AspNetCore.*.dll` | MIT. The Swagger UI files inside it are under Apache-2.0. |
| [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET), which Swashbuckle uses | `Microsoft.OpenApi.dll` | MIT. |

FFmpeg runs as a separate program that ImageGallery starts for each video. Its executables
and libraries are redistributed as the package supplies them.
