using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Racks.Services
{
    /// <summary>
    /// Thread-safe in-memory thumbnail cache keyed by (path, size, lastWriteTimeUtc).
    /// Freezes BitmapSource instances so they can be consumed on any thread.
    /// Also caches the Windows shortcut arrow overlay icon so it is only extracted once.
    /// </summary>
    public static class ThumbnailCache
    {
        private record CacheKey(string Path, int Size, DateTime LastWrite);

        private static readonly ConcurrentDictionary<CacheKey, BitmapSource> s_cache = new();
        private static readonly LinkedList<CacheKey> s_lru = new();
        private static readonly object s_lruLock = new();
        private const int MaxCacheSize = 600;

        private static BitmapSource? s_shortcutOverlay;
        private static readonly object s_overlayLock = new();

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static BitmapSource? GetShortcutOverlay()
        {
            if (s_shortcutOverlay != null) return s_shortcutOverlay;

            lock (s_overlayLock)
            {
                if (s_shortcutOverlay != null) return s_shortcutOverlay;

                try
                {
                    IntPtr[] overlayIcons = new IntPtr[1];
                    int extracted = ExtractIconEx(
                        Path.Combine(Environment.SystemDirectory, "shell32.dll"),
                        29,
                        overlayIcons,
                        null,
                        1);

                    if (extracted > 0 && overlayIcons[0] != IntPtr.Zero)
                    {
                        var overlay = Imaging.CreateBitmapSourceFromHIcon(
                            overlayIcons[0],
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        DestroyIcon(overlayIcons[0]);
                        overlay.Freeze();
                        s_shortcutOverlay = overlay;
                    }
                }
                catch
                {
                    // Fall back to null if shell32 extraction fails
                }
                return s_shortcutOverlay;
            }
        }

        public static bool TryGet(string path, int size, DateTime lastWrite, out BitmapSource? thumbnail)
        {
            var key = new CacheKey(path, size, lastWrite);
            if (s_cache.TryGetValue(key, out thumbnail))
            {
                DiagnosticCounters.IncrementThumbnailHit();
                lock (s_lruLock)
                {
                    s_lru.Remove(key);
                    s_lru.AddLast(key);
                }
                return true;
            }
            DiagnosticCounters.IncrementThumbnailMiss();
            thumbnail = null;
            return false;
        }

        public static void Put(string path, int size, DateTime lastWrite, BitmapSource thumbnail)
        {
            if (!thumbnail.IsFrozen && thumbnail.CanFreeze)
                thumbnail.Freeze();

            var key = new CacheKey(path, size, lastWrite);
            s_cache[key] = thumbnail;

            lock (s_lruLock)
            {
                s_lru.Remove(key);
                s_lru.AddLast(key);

                if (s_lru.Count > MaxCacheSize)
                {
                    var oldest = s_lru.First?.Value;
                    if (oldest != null)
                    {
                        s_lru.RemoveFirst();
                        s_cache.TryRemove(oldest, out _);
                    }
                }
                DiagnosticCounters.SetThumbnailCount(s_cache.Count);
            }
        }

        public static void Clear()
        {
            lock (s_lruLock)
            {
                s_cache.Clear();
                s_lru.Clear();
                DiagnosticCounters.SetThumbnailCount(0);
            }
        }
    }
}
