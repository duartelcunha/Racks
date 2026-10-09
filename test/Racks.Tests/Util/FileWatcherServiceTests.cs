using System.IO;
using System.Reflection;
using Racks.Services;
using Racks.Tests.Fakes;

namespace Racks.Tests.Util;

public class FileWatcherServiceTests
{
    private static FileSystemEventArgs Changed(string path)
        => new(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path));

    private static (FileWatcherService svc, ManualScheduler clock, List<string> raised) Make()
    {
        var clock = new ManualScheduler();
        var svc = new FileWatcherService(clock);
        var raised = new List<string>();
        svc.FileChanged += (_, e) => raised.Add(e.FullPath);
        return (svc, clock, raised);
    }

    // The handler is private; reach it the way FileSystemWatcher does.
    private static void RaiseContentChanged(FileWatcherService svc, FileSystemEventArgs e)
        => typeof(FileWatcherService)
            .GetMethod("OnFileContentChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(svc, new object?[] { null, e });

    [Fact]
    public void Fifty_writes_to_one_file_raise_one_event_after_it_goes_quiet()
    {
        var (svc, clock, raised) = Make();
        string file = Path.Combine(Path.GetTempPath(), "download.bin");

        for (int i = 0; i < 50; i++)
        {
            RaiseContentChanged(svc, Changed(file));
            clock.Advance(TimeSpan.FromMilliseconds(100));   // still writing: gaps shorter than the quiet period
        }
        Assert.Empty(raised);

        clock.Advance(FileWatcherService.ChangedQuietPeriod);
        Assert.Equal(new[] { file }, raised);
    }

    [Fact]
    public void Different_files_are_tracked_independently()
    {
        var (svc, clock, raised) = Make();
        string a = Path.Combine(Path.GetTempPath(), "a.bin"), b = Path.Combine(Path.GetTempPath(), "b.bin");
        RaiseContentChanged(svc, Changed(a));
        RaiseContentChanged(svc, Changed(b));
        clock.Advance(FileWatcherService.ChangedQuietPeriod);
        Assert.Equal(new[] { a, b }.Order(), raised.Order());
    }

    [Fact]
    public void Nothing_is_raised_after_dispose()
    {
        var (svc, clock, raised) = Make();
        RaiseContentChanged(svc, Changed(Path.Combine(Path.GetTempPath(), "x.bin")));
        svc.Dispose();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(raised);
    }

    [Fact]
    public void A_watcher_error_asks_for_one_rescan_after_a_short_delay()
    {
        var (svc, clock, raised) = Make();
        string folder = Path.Combine(Path.GetTempPath(), "racks-fw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            svc.Initialize(folder, folder);
            svc.RecoverFromWatcherError();
            svc.RecoverFromWatcherError();                  // an error burst must not stack restarts
            Assert.Empty(raised);

            clock.Advance(FileWatcherService.RestartDelay);
            Assert.Single(raised);
        }
        finally { svc.Dispose(); Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_real_watcher_reports_a_created_file_without_the_quiet_period()
    {
        string folder = Path.Combine(Path.GetTempPath(), "racks-fw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var svc = new FileWatcherService();
        using var seen = new ManualResetEventSlim();
        svc.FileChanged += (_, e) => { if (e.Name == "new.txt") seen.Set(); };
        try
        {
            svc.Initialize(folder, folder);
            File.WriteAllText(Path.Combine(folder, "new.txt"), "x");
            Assert.True(seen.Wait(TimeSpan.FromSeconds(10)), "Created must reach the owner");
        }
        finally { svc.Dispose(); Directory.Delete(folder, true); }
    }

    // A desktop rack lists the Desktop but keeps its files in the workspace. Dragging an item out
    // deletes it from the workspace, and the rack only refreshes if that folder is watched too.
    [Fact]
    public void A_second_watched_folder_raises_events_too()
    {
        string main = Path.Combine(Path.GetTempPath(), "racks-watch-a-" + Guid.NewGuid().ToString("N"));
        string extra = Path.Combine(Path.GetTempPath(), "racks-watch-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(extra);
        string file = Path.Combine(extra, "Holiday.jpg");
        File.WriteAllText(file, "x");
        try
        {
            using var svc = new FileWatcherService();
            using var raised = new ManualResetEventSlim();
            svc.FileChanged += (_, e) => { if (e.FullPath == file) raised.Set(); };
            svc.Initialize(main, main, extra);

            File.Delete(file);

            Assert.True(raised.Wait(TimeSpan.FromSeconds(5)), "no event from the second folder");
        }
        finally
        {
            try { Directory.Delete(main, true); } catch { }
            try { Directory.Delete(extra, true); } catch { }
        }
    }
}
