using System.Threading;

namespace Racks.Services
{
    /// <summary>
    /// Lightweight atomic counters for developer diagnostics. Incremented at the call site;
    /// read by the DevDiagnosticsOverlay at its own polling rate. Thread-safe, zero-cost
    /// when the overlay is not open.
    /// </summary>
    public static class DiagnosticCounters
    {
        private static int _thumbnailCacheHits;
        private static int _thumbnailCacheMisses;
        private static int _thumbnailCacheCount;

        public static int ThumbnailCacheHits => _thumbnailCacheHits;
        public static int ThumbnailCacheMisses => _thumbnailCacheMisses;
        public static int ThumbnailCacheCount => _thumbnailCacheCount;

        public static void IncrementThumbnailHit() => Interlocked.Increment(ref _thumbnailCacheHits);
        public static void IncrementThumbnailMiss() => Interlocked.Increment(ref _thumbnailCacheMisses);
        public static void SetThumbnailCount(int count) => Volatile.Write(ref _thumbnailCacheCount, count);
    }
}
