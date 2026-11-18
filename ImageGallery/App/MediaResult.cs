using System;
using System.IO;

namespace ImageGallery
{
    /// <summary>
    /// Data transfer object returned by <see cref="Workers.ImageWorker"/> and
    /// <see cref="Workers.VideoWorker"/> after processing a media file. Carries both the
    /// extracted metadata and the dimensions needed to build the directory index.
    /// </summary>
    public class MediaResult
    {
        /// <summary>Title read from the file's IPTC "Object Name" tag, or the filename if absent.</summary>
        public string Title;

        /// <summary>Caption read from the file's IPTC "Caption/Abstract" tag.</summary>
        public string Caption;

        /// <summary>The source file that was processed.</summary>
        public FileInfo File;

        /// <summary>Width of the source media in pixels.</summary>
        public int Width;

        /// <summary>Height of the source media in pixels.</summary>
        public int Height;

        /// <summary>Duration in seconds; 0 for images.</summary>
        public double Duration;

        /// <summary>Last-write timestamp of the source file at the time of processing.</summary>
        public DateTime Updated;

        /// <summary>Whether this job produced a thumbnail or an icon.</summary>
        public Media.Type Type;

        /// <summary>The original video can be served, but its preview could not be decoded.</summary>
        public bool PreviewUnavailable;
    }
}
