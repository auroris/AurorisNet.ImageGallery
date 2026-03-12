using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace ImageGallery.Workers
{
    /// <summary>
    /// Tracks thumbnail-generation progress for a single directory and exposes it via a
    /// Windows memory-mapped file so the progress can be read from any request without
    /// shared in-process state. The MMF is named after the directory and holds one byte
    /// representing the completion percentage (0–100).
    /// <para>
    /// Callers register each unit of work with <see cref="AddWork"/> before starting it,
    /// then call <see cref="FinishedWork"/> when it completes. The instance disposes itself
    /// automatically once all registered work reaches 100%.
    /// </para>
    /// </summary>
    public class ProgressWorker : IDisposable
    {
        /// <summary>Names of work items that have been registered but not yet completed.</summary>
        private List<string> works = new List<string>();
        private readonly object _lock = new object();
        private MemoryMappedFile mmf;
        private int workInitiated = 0;
        private int workCompleted = 0;
        private bool disposedValue;

        /// <summary>
        /// Opens or creates the memory-mapped file for <paramref name="workDirectory"/>.
        /// The MMF is keyed by directory name, so two requests for the same directory share state.
        /// </summary>
        public ProgressWorker(DirectoryInfo workDirectory)
        {
            mmf = MemoryMappedFile.CreateOrOpen(workDirectory.Name, 1);
        }

        /// <summary>
        /// Writes the current completion percentage to the first byte of the MMF.
        /// </summary>
        private void WriteProgress()
        {
            int initiated = workInitiated; // set before tasks start; safe to read without lock
            int completed = Volatile.Read(ref workCompleted);
            byte percent = initiated == 0 ? (byte)0 : (byte)((float)completed / (float)initiated * 100);
            using (MemoryMappedViewStream stream = mmf.CreateViewStream())
            {
                stream.WriteByte(percent);
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
                if (!disposedValue) { WriteProgress(); }
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
