using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace ImageGallery.Workers
{
    /// <summary>
    /// Tracks thumbnail-generation progress for a single directory and exposes it via a
    /// Windows memory-mapped file so the progress can be read from any request without
    /// shared in-process state. The MMF is named after the directory and holds a completion
    /// percentage byte followed by a length-prefixed UTF-8 string of the current work item.
    /// <para>
    /// Callers register each unit of work with <see cref="AddWork"/> before starting it,
    /// then call <see cref="FinishedWork"/> when it completes. The instance disposes itself
    /// automatically once all registered work reaches 100%.
    /// </para>
    /// </summary>
    public class ProgressWorker : IDisposable
    {
        // Layout: [0] percent byte  [1-4] int32 LE string-byte-length  [5…] UTF-8 work item
        private const int MmfCapacity = 2048;
        private const int StringDataOffset = 5; // 1 (percent) + 4 (int32 length)

        /// <summary>Snapshot of progress exposed via the MMF.</summary>
        /// <param name="Percent">Completion percentage (0–100).</param>
        /// <param name="CurrentWork">The first pending work item, or an empty string when idle.</param>
        public record ProgressStatus(int Percent, string CurrentWork);

        /// <summary>Names of work items that have been registered but not yet completed.</summary>
        private List<string> works = new List<string>();
        private readonly object _lock = new object();
        private MemoryMappedFile mmf;
        private int workInitiated = 0;
        private int workCompleted = 0;
        private bool disposedValue;
        private string _lastFinishedWork = string.Empty;

        /// <summary>
        /// Opens or creates the memory-mapped file for <paramref name="workDirectory"/>.
        /// The MMF is keyed by directory name, so two requests for the same directory share state.
        /// </summary>
        public ProgressWorker(DirectoryInfo workDirectory)
        {
            mmf = MemoryMappedFile.CreateOrOpen(workDirectory.Name, MmfCapacity);
        }

        /// <summary>
        /// Writes the current completion percentage and first pending work item to the MMF.
        /// Must be called under <see cref="_lock"/> (or during single-threaded setup via AddWork).
        /// </summary>
        private void WriteProgress()
        {
            int initiated = workInitiated; // set before tasks start; safe to read without lock
            int completed = Volatile.Read(ref workCompleted);
            byte percent = initiated == 0 ? (byte)0 : (byte)((float)completed / (float)initiated * 100);

            string currentWork = _lastFinishedWork;
            byte[] workBytes = Encoding.UTF8.GetBytes(currentWork);
            int workLength = Math.Min(workBytes.Length, MmfCapacity - StringDataOffset);

            using (MemoryMappedViewStream stream = mmf.CreateViewStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(percent);                      // byte   [0]
                writer.Write(workLength);                   // int32  [1–4]
                writer.Write(workBytes, 0, workLength);     // UTF-8  [5…]
            }
        }

        /// <summary>
        /// Reads the current completion percentage (0–100) from the MMF.
        /// Returns 0 when no work has started yet.
        /// </summary>
        public int GetProgress()
        {
            using (MemoryMappedViewStream stream = mmf.CreateViewStream())
            {
                return stream.ReadByte();
            }
        }

        /// <summary>
        /// Reads the current progress percentage and first pending work item from the MMF.
        /// Returns <see cref="ProgressStatus.CurrentWork"/> as an empty string when idle or
        /// when no work item has been written yet.
        /// </summary>
        public ProgressStatus GetStatus()
        {
            using (MemoryMappedViewStream stream = mmf.CreateViewStream())
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                int percent = reader.ReadByte();
                int workLength = reader.ReadInt32();
                if (workLength > 0 && workLength <= MmfCapacity - StringDataOffset)
                {
                    byte[] workBytes = reader.ReadBytes(workLength);
                    return new ProgressStatus(percent, Encoding.UTF8.GetString(workBytes));
                }
                return new ProgressStatus(percent, string.Empty);
            }
        }

        /// <summary>
        /// Registers a new unit of work and updates the progress percentage.
        /// Must be called before the corresponding <see cref="FinishedWork"/> call.
        /// </summary>
        /// <param name="work">A descriptive name for the work item (e.g. a file path).</param>
        public void AddWork(string work)
        {
            // AddWork is only called during setup before any tasks start, so no concurrent access here
            workInitiated++;
            lock (_lock) { works.Add(work); }
            WriteProgress();
        }

        /// <summary>
        /// Marks a unit of work as complete, updates the progress percentage, and disposes
        /// this instance if all registered work is now finished.
        /// </summary>
        /// <param name="work">The name that was passed to <see cref="AddWork"/>.</param>
        public void FinishedWork(string work)
        {
            int completed = Interlocked.Increment(ref workCompleted);
            lock (_lock)
            {
                works?.Remove(work);
                if (!disposedValue)
                {
                    _lastFinishedWork = Path.GetFileName(work);
                    WriteProgress();
                }
            }

            if (completed == workInitiated)
            {
                Dispose();
            }
        }

        /// <summary>
        /// Returns a snapshot of the work items that have been registered but not yet completed.
        /// </summary>
        public List<string> GetPendingWork()
        {
            lock (_lock) { return new List<string>(works ?? new List<string>()); }
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        works?.Clear();
                        works = null;
                        mmf.Dispose();
                    }

                    disposedValue = true;
                }
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
