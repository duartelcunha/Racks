using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Racks.Core;
using Racks.Services;
using Racks.Util;
using Xunit;

namespace Racks.Tests.Rack
{
    public class RackDeletionTests
    {
        [Fact]
        public void ArrangeInGrid_does_not_throw_on_empty_collection()
        {
            var exception = Record.Exception(() => DesktopIconPositioner.ArrangeInGrid(Array.Empty<string>()));
            Assert.Null(exception);
        }

        [Fact]
        public void ArrangeInGrid_does_not_throw_on_nonexistent_paths()
        {
            var fakePaths = new[]
            {
                Path.Combine(Path.GetTempPath(), "nonexistent_file_1.txt"),
                Path.Combine(Path.GetTempPath(), "nonexistent_file_2.txt")
            };
            var exception = Record.Exception(() => DesktopIconPositioner.ArrangeInGrid(fakePaths));
            Assert.Null(exception);
        }

        [Fact]
        public void GetKeyLocation_returns_empty_when_name_is_null_or_empty()
        {
            var inst = new Instance("", false);
            Assert.Equal("", inst.GetKeyLocation());
        }

        [Fact]
        public void GetKeyLocation_returns_expected_path_when_name_is_set()
        {
            var inst = new Instance("TestRack_42", false);
            string expected = @$"SOFTWARE\{InstanceController.appName}\Instances\TestRack_42";
            Assert.Equal(expected, inst.GetKeyLocation());
        }

        [Fact]
        public void DeleteSubKeyTree_with_throwOnMissingSubKey_false_does_not_throw_on_missing_key()
        {
            string nonExistentKey = @$"SOFTWARE\{InstanceController.appName}\Instances\DefinitelyDoesNotExist_{Guid.NewGuid():N}";
            var exception = Record.Exception(() =>
            {
                Registry.CurrentUser.DeleteSubKeyTree(nonExistentKey, throwOnMissingSubKey: false);
            });
            Assert.Null(exception);
        }

        [Fact]
        public void FileWatcherService_Dispose_can_be_called_multiple_times_without_throwing()
        {
            var service = new FileWatcherService();
            var ex1 = Record.Exception(() => service.Dispose());
            var ex2 = Record.Exception(() => service.Dispose());
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void FileWatcherService_Dispose_prevents_raising_events_after_folder_deletion()
        {
            // A private parent: the service watches the parent of the rack folder, and %TEMP% itself is busy with
            // other tests creating and deleting folders, which used to raise events from outside this test.
            string root = Path.Combine(Path.GetTempPath(), "RackDeletionTest_" + Guid.NewGuid().ToString("N"));
            string tempDir = Path.Combine(root, "rack");
            Directory.CreateDirectory(tempDir);

            try
            {
                var service = new FileWatcherService();
                service.Initialize(tempDir, tempDir);

                // Control: a live service on the same folder. Its event proves the deletion was delivered to
                // watchers, so waiting on it replaces a blind sleep.
                var control = new FileWatcherService();
                using var controlSeen = new System.Threading.ManualResetEventSlim(false);
                control.ParentChanged += (_, _) => controlSeen.Set();
                control.Initialize(tempDir, tempDir);

                bool eventFired = false;
                service.ParentChanged += (_, _) => eventFired = true;
                service.FileChanged += (_, _) => eventFired = true;

                // Dispose first (simulating rack deletion teardown order)
                service.Dispose();

                // Delete the watched folder
                Directory.Delete(tempDir, recursive: true);

                Assert.True(controlSeen.Wait(TimeSpan.FromSeconds(10)), "the live control watcher never saw the deletion");
                control.Dispose();

                Assert.False(eventFired, "Disposed FileWatcherService should not raise events after directory deletion.");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }
        }

        [Fact]
        public void ThumbnailCache_stores_and_retrieves_frozen_bitmap()
        {
            string dummyPath = Path.Combine(Path.GetTempPath(), "thumb_test_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllText(dummyPath, "test content");

            try
            {
                DateTime lastWrite = File.GetLastWriteTimeUtc(dummyPath);
                int size = 48;

                // Create a 1x1 dummy BitmapSource
                var dummyBitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                    1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr24,
                    null,
                    new byte[] { 0, 0, 0 },
                    3);
                dummyBitmap.Freeze();

                ThumbnailCache.Put(dummyPath, size, lastWrite, dummyBitmap);

                bool found = ThumbnailCache.TryGet(dummyPath, size, lastWrite, out var cached);
                Assert.True(found);
                Assert.NotNull(cached);
                Assert.True(cached!.IsFrozen);
            }
            finally
            {
                try { File.Delete(dummyPath); } catch { }
            }
        }
    }
}
