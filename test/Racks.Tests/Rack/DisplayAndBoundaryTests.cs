using System;
using System.IO;
using Racks.Core;
using Racks.Services;
using Racks.Util;
using Xunit;

namespace Racks.Tests.Rack
{
    public class DisplayAndBoundaryTests
    {
        [Fact]
        public void SafeMove_rejects_moving_protected_special_folders()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string tempTarget = Path.Combine(Path.GetTempPath(), "target_test");

            var result = SafeMove.TryMove(desktop, tempTarget, out string reason);
            Assert.Equal(SafeMove.Result.Rejected, result);
            Assert.NotEmpty(reason);
        }

        [Fact]
        public void SafeMove_handles_long_paths_gracefully()
        {
            string longDir = Path.Combine(Path.GetTempPath(), new string('a', 150));
            string longFile = Path.Combine(longDir, new string('b', 120) + ".txt");
            string destFile = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                Directory.CreateDirectory(longDir);
                File.WriteAllText(longFile, "long path content");

                var result = SafeMove.TryMove(longFile, destFile, out string reason);
                Assert.True(result == SafeMove.Result.Moved, $"Failed to move long path: {reason}");
                Assert.True(File.Exists(destFile));
            }
            finally
            {
                if (File.Exists(destFile)) File.Delete(destFile);
                if (Directory.Exists(longDir)) Directory.Delete(longDir, recursive: true);
            }
        }

        [Theory]
        [InlineData(1.0)] // 100% scale (96 DPI)
        [InlineData(1.25)] // 125% scale (120 DPI)
        [InlineData(1.5)] // 150% scale (144 DPI)
        [InlineData(1.75)] // 175% scale (168 DPI)
        [InlineData(2.0)] // 200% scale (192 DPI)
        public void MagicOrganizeLayout_Grid_scales_properly_with_dpi(double scale)
        {
            int screenWidthPx = (int)(1920 * scale);
            int screenHeightPx = (int)(1080 * scale);

            var slots = MagicOrganizeLayout.Grid(4, 0, 0, screenWidthPx, screenHeightPx, scale);
            Assert.Equal(4, slots.Count);

            foreach (var slot in slots)
            {
                Assert.True(slot.X >= 0, $"Slot X {slot.X} should be >= 0 for scale {scale}");
                Assert.True(slot.Y >= 0, $"Slot Y {slot.Y} should be >= 0 for scale {scale}");
                Assert.True(slot.X + MagicOrganizeLayout.RackWidth <= 1920 + 1, $"Slot exceeds screen width at scale {scale}");
                Assert.True(slot.Y + MagicOrganizeLayout.RackHeight <= 1080 + 1, $"Slot exceeds screen height at scale {scale}");
            }
        }

        [Fact]
        public void ThumbnailCache_handles_nonexistent_and_corrupt_paths()
        {
            string fakePath = @"C:\Nonexistent_Directory_12345\fake_file.exe";
            bool found = ThumbnailCache.TryGet(fakePath, 48, DateTime.UtcNow, out var thumb);
            Assert.False(found);
            Assert.Null(thumb);
        }

        [Fact]
        public void DiagnosticCounters_increment_and_reset_safely()
        {
            int beforeHits = DiagnosticCounters.ThumbnailCacheHits;
            DiagnosticCounters.IncrementThumbnailHit();
            Assert.Equal(beforeHits + 1, DiagnosticCounters.ThumbnailCacheHits);

            int beforeMisses = DiagnosticCounters.ThumbnailCacheMisses;
            DiagnosticCounters.IncrementThumbnailMiss();
            Assert.Equal(beforeMisses + 1, DiagnosticCounters.ThumbnailCacheMisses);

            DiagnosticCounters.SetThumbnailCount(42);
            Assert.Equal(42, DiagnosticCounters.ThumbnailCacheCount);
        }
    }
}
