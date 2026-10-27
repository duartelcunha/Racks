using System.Collections.Concurrent;
using System.IO;
using Racks.UseCases.AutoRoute;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Shell;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.AutoRoute;

// The thin watcher: debounce, de-duplication and recovery from watcher errors. Time is a ManualScheduler, the UI
// thread is "run it now", and the FileSystemWatcher comes from a factory, so nothing here waits or watches the
// real Desktop. Events of those watchers are parked, never run (see Post).
public sealed class DesktopRouterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-router-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly ManualScheduler _clock = new();
    private readonly List<string> _routed = [];
    private readonly List<FileSystemWatcher> _watchers = [];
    // The real watchers raise Created/Renamed on thread-pool threads. Production posts them to the UI thread, so the router is
    // single-threaded; here "post" only parks them (nobody drains the queue), because every test drives Enqueue and the clock
    // itself. Running them inline made a late real event re-queue a file from a pool thread while the test thread was asserting.
    private readonly ConcurrentQueue<Action> _posted = new();
    private void Post(Action action) => _posted.Enqueue(action);

    private int _factoryCalls;
    private Func<string, FileSystemWatcher>? _factoryOverride;

    public DesktopRouterTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(_desktop);
    }

    public void Dispose()
    {
        foreach (var w in _watchers) try { w.Dispose(); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private FileSystemWatcher Factory(string folder)
    {
        _factoryCalls++;
        if (_factoryOverride != null) return _factoryOverride(folder);
        var w = new FileSystemWatcher(folder) { InternalBufferSize = 64 * 1024 };
        _watchers.Add(w);
        return w;
    }

    private DesktopRouter NewRouter()
        => new(batch => _routed.AddRange(batch), _desktop, _clock, Post, Factory);

    private static readonly TimeSpan Debounce = DesktopRouter.DebounceWindow;
    private static readonly TimeSpan Restart = DesktopRouter.RestartDelay;

    [Fact]
    public void The_watcher_is_started_once_with_the_big_buffer()
    {
        using var router = NewRouter();
        Assert.Equal(1, _factoryCalls);
        Assert.True(_watchers[0].EnableRaisingEvents);
        Assert.True(_watchers[0].InternalBufferSize >= 64 * 1024);
    }

    [Fact]
    public void Nothing_is_routed_before_the_debounce_window_ends()
    {
        using var router = NewRouter();
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        _clock.Advance(Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Empty(_routed);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([Path.Combine(_desktop, "a.pdf")], _routed);
    }

    [Fact]
    public void Duplicate_events_for_one_path_in_a_window_route_it_once()
    {
        using var router = NewRouter();
        string p = Path.Combine(_desktop, "a.pdf");
        for (int i = 0; i < 10; i++)
        {
            router.Enqueue(p);
            router.Enqueue(p.ToUpperInvariant());   // Windows paths are case-insensitive
            _clock.Advance(TimeSpan.FromMilliseconds(30));
        }
        Assert.Equal(1, _clock.PendingCount);   // one flush timer, not one per event
        _clock.Advance(Debounce);
        Assert.Single(_routed);
    }

    [Fact]
    public void Different_paths_in_one_window_are_each_routed_once()
    {
        using var router = NewRouter();
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        router.Enqueue(Path.Combine(_desktop, "b.pdf"));
        _clock.Advance(Debounce);
        Assert.Equal(2, _routed.Count);
    }

    [Fact]
    public void A_later_event_after_the_flush_starts_a_new_window()
    {
        using var router = NewRouter();
        string p = Path.Combine(_desktop, "a.pdf");
        router.Enqueue(p);
        _clock.Advance(Debounce);
        router.Enqueue(p);
        _clock.Advance(Debounce);
        Assert.Equal(2, _routed.Count);
    }

    [Fact]
    public void A_failing_batch_does_not_stop_the_next_window()
    {
        int calls = 0;
        using var router = new DesktopRouter(batch => { calls++; if (calls == 1) throw new IOException("boom"); _routed.AddRange(batch); },
            _desktop, _clock, Post, Factory);
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        _clock.Advance(Debounce);
        router.Enqueue(Path.Combine(_desktop, "b.pdf"));
        _clock.Advance(Debounce);
        Assert.Equal([Path.Combine(_desktop, "b.pdf")], _routed);
    }

    [Fact]
    public void One_window_is_one_batch()
    {
        var batches = new List<int>();
        using var router = new DesktopRouter(batch => batches.Add(batch.Count), _desktop, _clock, Post, Factory);
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        router.Enqueue(Path.Combine(_desktop, "b.pdf"));
        router.Enqueue(Path.Combine(_desktop, "c.pdf"));
        _clock.Advance(Debounce);
        Assert.Equal([3], batches);
    }

    [Fact]
    public void The_watcher_listens_for_names_only()
    {
        // Created/Renamed are handled; Changed never is, so it must not be asked for (it would only fill the buffer).
        var filter = DesktopRouterNotifyFilterForTest();
        Assert.Equal(NotifyFilters.FileName, filter);
    }

    private static NotifyFilters DesktopRouterNotifyFilterForTest()
    {
        var m = typeof(DesktopRouter).GetMethod("NewWatcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        using var w = (FileSystemWatcher)m.Invoke(null, [Path.GetTempPath()])!;
        return w.NotifyFilter;
    }

    // ---- watcher errors --------------------------------------------------------------------

    // Files the user keeps on the Desktop on purpose are not "new" because the watcher was rebuilt: before P4 only
    // Created/Renamed events routed anything, and a sweep after an error would move all of them.
    [Fact]
    public void After_an_error_files_that_were_already_on_the_desktop_are_left_alone()
    {
        File.WriteAllText(Path.Combine(_desktop, "one.txt"), "1");
        File.WriteAllText(Path.Combine(_desktop, "two.txt"), "2");
        using var router = NewRouter();   // snapshot: one.txt, two.txt

        router.HandleWatcherError();
        router.HandleWatcherError();   // a burst of errors joins the restart that is already waiting
        router.HandleWatcherError();
        Assert.Equal(1, _factoryCalls);

        _clock.Advance(Restart);
        Assert.Equal(2, _factoryCalls);   // rebuilt, once
        _clock.Advance(Debounce);

        Assert.Empty(_routed);
        Assert.Equal(0, _clock.PendingCount);   // nothing left running: no loop
    }

    [Fact]
    public void A_file_created_during_the_outage_is_routed_once_after_the_restart()
    {
        File.WriteAllText(Path.Combine(_desktop, "old.txt"), "old");
        Directory.CreateDirectory(Path.Combine(_desktop, "folder"));   // the watcher reports files only, so does the rescan
        using var router = NewRouter();

        router.HandleWatcherError();
        File.WriteAllText(Path.Combine(_desktop, "missed.pdf"), "arrived while the watcher was down");
        _clock.Advance(Restart);
        _clock.Advance(Debounce);

        Assert.Equal([Path.Combine(_desktop, "missed.pdf")], _routed);

        // A later error must not route it again: it is known now.
        _routed.Clear();
        router.HandleWatcherError();
        _clock.Advance(Restart * 2);
        _clock.Advance(Debounce);
        Assert.Empty(_routed);
    }

    [Fact]
    public void A_file_that_arrived_by_event_is_known_and_not_swept_up_by_a_later_rescan()
    {
        using var router = NewRouter();
        string p = Path.Combine(_desktop, "kept.pdf");
        File.WriteAllText(p, "skipped earlier, stays on the Desktop");
        router.Enqueue(p);
        _clock.Advance(Debounce);
        _routed.Clear();

        router.HandleWatcherError();
        _clock.Advance(Restart);
        _clock.Advance(Debounce);

        Assert.Empty(_routed);
    }

    [Fact]
    public void A_start_failure_routes_nothing_on_recovery_and_only_takes_the_snapshot()
    {
        File.WriteAllText(Path.Combine(_desktop, "already-there.pdf"), "x");
        int attempts = 0;
        _factoryOverride = folder =>
        {
            if (++attempts < 2) throw new IOException("the Desktop is not reachable yet");
            var w = new FileSystemWatcher(folder);
            _watchers.Add(w);
            return w;
        };

        using var router = NewRouter();   // first start fails inside the constructor
        _clock.Advance(Restart);          // second start works
        _clock.Advance(Debounce);
        Assert.Equal(2, attempts);
        Assert.Empty(_routed);            // no snapshot existed, so nothing counts as new

        // From here on it behaves like any healthy router.
        File.WriteAllText(Path.Combine(_desktop, "after.pdf"), "x");
        router.Enqueue(Path.Combine(_desktop, "after.pdf"));
        _clock.Advance(Debounce);
        Assert.Equal([Path.Combine(_desktop, "after.pdf")], _routed);
    }

    [Fact]
    public void The_old_watcher_is_disposed_when_it_is_replaced()
    {
        using var router = NewRouter();
        var first = _watchers[0];
        router.HandleWatcherError();
        _clock.Advance(Restart);
        Assert.Equal(2, _watchers.Count);
        Assert.False(first.EnableRaisingEvents);
        Assert.True(_watchers[1].EnableRaisingEvents);
    }

    [Fact]
    public void Repeated_errors_back_off_instead_of_looping_tightly()
    {
        using var router = NewRouter();

        router.HandleWatcherError();
        _clock.Advance(Restart);
        Assert.Equal(2, _factoryCalls);

        router.HandleWatcherError();   // no real event in between: wait twice as long
        _clock.Advance(Restart);
        Assert.Equal(2, _factoryCalls);
        _clock.Advance(Restart);
        Assert.Equal(3, _factoryCalls);

        router.HandleWatcherError();   // and twice as long again
        _clock.Advance(TimeSpan.FromTicks(Restart.Ticks * 3));
        Assert.Equal(3, _factoryCalls);
        _clock.Advance(Restart);
        Assert.Equal(4, _factoryCalls);
    }

    [Fact]
    public void The_wait_never_grows_beyond_its_cap()
    {
        using var router = NewRouter();
        for (int i = 0; i < 40; i++)
        {
            router.HandleWatcherError();
            _clock.Advance(DesktopRouter.MaxRestartDelay);
        }
        int calls = _factoryCalls;
        router.HandleWatcherError();
        _clock.Advance(DesktopRouter.MaxRestartDelay);
        Assert.Equal(calls + 1, _factoryCalls);
    }

    [Fact]
    public void A_real_event_after_an_error_resets_the_wait()
    {
        using var router = NewRouter();
        router.HandleWatcherError();
        _clock.Advance(Restart);
        router.HandleWatcherError();
        _clock.Advance(Restart * 2);
        Assert.Equal(3, _factoryCalls);

        router.Enqueue(Path.Combine(_desktop, "alive.txt"));   // the watcher works again
        _clock.Advance(Debounce);

        router.HandleWatcherError();
        _clock.Advance(Restart);
        Assert.Equal(4, _factoryCalls);
    }

    [Fact]
    public void A_watcher_that_cannot_be_built_is_retried_not_thrown()
    {
        int attempts = 0;
        _factoryOverride = folder =>
        {
            if (++attempts < 3) throw new IOException("the Desktop is not reachable");
            var w = new FileSystemWatcher(folder);
            _watchers.Add(w);
            return w;
        };

        using var router = NewRouter();   // the first attempt fails inside the constructor
        Assert.Equal(1, attempts);

        _clock.Advance(Restart);          // second attempt fails
        Assert.Equal(2, attempts);
        _clock.Advance(Restart * 2);      // third works
        Assert.Equal(3, attempts);
        Assert.True(_watchers[0].EnableRaisingEvents);
    }

    [Fact]
    public void Disposing_stops_the_watcher_and_cancels_what_is_waiting()
    {
        var router = NewRouter();
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        router.HandleWatcherError();

        router.Dispose();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(_routed);
        Assert.Equal(1, _factoryCalls);
        Assert.False(_watchers[0].EnableRaisingEvents);
    }

    // ---- the pieces together ---------------------------------------------------------------

    [Fact]
    public void A_download_is_routed_when_it_is_renamed_to_its_final_name()
    {
        string rackDir = Path.Combine(_root, "Pdfs");
        Directory.CreateDirectory(rackDir);
        var rack = new Instance("Pdfs", false);
        using (rack.SuspendPersistence()) { rack.Folder = rackDir; rack.AutoRouteRegex = @"report"; }

        var audit = new RecordingAuditSink();
        using var service = new AutoRouteService(
            new AutoRouteHost(() => [rack], _ => { }, () => Path.Combine(_root, "ws")),
            _clock, broker: new FileOperationBroker(audit), shell: new ShellOperationGateway(new ShellBackend()), audit: audit);
        using var router = new DesktopRouter(service.ProcessBatch, _desktop, _clock, Post, Factory);

        string partial = Path.Combine(_desktop, "report.pdf.crdownload");
        File.WriteAllText(partial, "half");
        router.Enqueue(partial);
        _clock.Advance(Debounce);
        Assert.True(File.Exists(partial));                         // not moved half-written
        Assert.Equal("Skipped", Assert.Single(audit.Events).Status);

        string final = Path.Combine(_desktop, "report.pdf");
        File.Move(partial, final);
        router.Enqueue(final);                                      // what the Renamed event delivers
        _clock.Advance(Debounce);

        Assert.True(File.Exists(Path.Combine(rackDir, "report.pdf")));
        Assert.False(File.Exists(final));
    }

    [Fact]
    public void DestinationFor_still_answers_through_the_router()
    {
        var rack = new Instance("D", false);
        using (rack.SuspendPersistence()) { rack.IsDesktopFilterRack = true; rack.Folder = @"C:\Users\x\Desktop"; }
        Assert.Equal(@"C:\ws", DesktopRouter.DestinationFor(rack, @"C:\ws"));
    }
}
