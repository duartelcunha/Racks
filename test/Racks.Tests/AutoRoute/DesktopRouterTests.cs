using System.IO;
using Racks.Application.AutoRoute;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Shell;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.AutoRoute;

// The thin watcher: debounce, de-duplication and recovery from watcher errors. Time is a ManualScheduler, the UI
// thread is "run it now", and the FileSystemWatcher comes from a factory, so nothing here waits or watches the
// real Desktop.
public sealed class DesktopRouterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-router-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly ManualScheduler _clock = new();
    private readonly List<string> _routed = [];
    private readonly List<FileSystemWatcher> _watchers = [];
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
        => new(p => _routed.Add(p), _desktop, _clock, a => a(), Factory);

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
    public void An_exception_from_one_path_does_not_stop_the_others()
    {
        var seen = new List<string>();
        using var router = new DesktopRouter(p => { seen.Add(p); if (p.EndsWith("a.pdf")) throw new IOException("boom"); },
            _desktop, _clock, a => a(), Factory);
        router.Enqueue(Path.Combine(_desktop, "a.pdf"));
        router.Enqueue(Path.Combine(_desktop, "b.pdf"));
        _clock.Advance(Debounce);
        Assert.Equal(2, seen.Count);
    }

    // ---- watcher errors --------------------------------------------------------------------

    [Fact]
    public void An_error_rebuilds_the_watcher_once_and_looks_at_the_desktop_once()
    {
        File.WriteAllText(Path.Combine(_desktop, "one.txt"), "1");
        File.WriteAllText(Path.Combine(_desktop, "two.txt"), "2");
        Directory.CreateDirectory(Path.Combine(_desktop, "folder"));   // the watcher reports files only, so does the rescan
        using var router = NewRouter();

        router.HandleWatcherError();
        router.HandleWatcherError();   // a burst of errors joins the restart that is already waiting
        router.HandleWatcherError();
        Assert.Equal(1, _factoryCalls);

        _clock.Advance(Restart);
        Assert.Equal(2, _factoryCalls);   // rebuilt, once
        Assert.Empty(_routed);            // the rescan goes through the same debounce

        _clock.Advance(Debounce);
        Assert.Equal(
            [Path.Combine(_desktop, "one.txt"), Path.Combine(_desktop, "two.txt")],
            _routed.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(0, _clock.PendingCount);   // nothing left running: no loop
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
        using var router = new DesktopRouter(p => service.Process(p), _desktop, _clock, a => a(), Factory);

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
