using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;
using Racks.Tests.Settings;
using Racks.UseCases.Startup;

namespace Racks.Tests.Startup;

[Collection("Registry")]
public sealed class StartupMigrationServiceTests : IDisposable
{
    private readonly ThrowawayRegistry _reg = new();
    private readonly ManualScheduler _scheduler = new();
    private readonly List<string> _log = new();
    private bool _pinThrows;

    public void Dispose() => _reg.Dispose();

    private MigrationMarkers Markers() => new(_reg.Root, new RecordingAuditSink());

    private StartupMigrationService Create(MigrationMarkers? markers = null) => new(
        markers ?? Markers(),
        () => { _log.Add("pin"); if (_pinThrows) throw new InvalidOperationException("no shell"); },
        _scheduler,
        () => _log.Add("install-animation"),
        () => _log.Add("launch-animation"));

    [Fact]
    public void The_first_launch_is_a_first_run_and_marks_itself_so_the_next_is_not()
    {
        Assert.True(Create().ConsumeFirstRunMarker());
        Assert.True(Markers().IsSet(MigrationMarkers.InstallAnimationShownV2));

        Assert.False(Create().ConsumeFirstRunMarker());
        Assert.False(Create().ConsumeFirstRunMarker());
    }

    [Fact]
    public void The_marker_is_written_the_way_older_builds_wrote_it()
    {
        Create().ConsumeFirstRunMarker();
        var (data, kind) = _reg.RawRoot(MigrationMarkers.InstallAnimationShownV2);
        Assert.Equal("True", data);
        Assert.Equal(Microsoft.Win32.RegistryValueKind.String, kind);
    }

    [Fact]
    public void An_unreadable_registry_reads_as_a_first_run_like_before()
    {
        var markers = new MigrationMarkers(new FailingRegistryRoot(), new RecordingAuditSink());
        Assert.True(Create(markers).ConsumeFirstRunMarker());
    }

    [Fact]
    public void Quick_access_is_pinned_once_and_only_marked_after_the_pin_worked()
    {
        var svc = Create();

        svc.PinQuickAccessOnce();
        svc.PinQuickAccessOnce();
        Create().PinQuickAccessOnce();

        Assert.Equal(["pin"], _log);
        Assert.True(Markers().IsSet(MigrationMarkers.QuickAccessMirrorPinned));
    }

    [Fact]
    public void A_failed_pin_is_swallowed_and_tried_again_next_launch()
    {
        _pinThrows = true;
        Create().PinQuickAccessOnce();
        Assert.False(Markers().IsSet(MigrationMarkers.QuickAccessMirrorPinned));

        _pinThrows = false;
        Create().PinQuickAccessOnce();

        Assert.Equal(["pin", "pin"], _log);
        Assert.True(Markers().IsSet(MigrationMarkers.QuickAccessMirrorPinned));
    }

    [Fact]
    public void A_user_who_unpinned_by_hand_is_not_pinned_again()
    {
        Markers().Set(MigrationMarkers.QuickAccessMirrorPinned);
        Create().PinQuickAccessOnce();
        Assert.Empty(_log);
    }

    [Fact]
    public void The_animation_waits_500_ms_and_the_first_run_plays_the_install_animation()
    {
        var svc = Create();

        svc.ScheduleStartupAnimation(firstRun: true);
        Assert.Equal(TimeSpan.FromMilliseconds(500), StartupMigrationService.AnimationDelay);
        _scheduler.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Empty(_log);
        _scheduler.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(["install-animation"], _log);
    }

    [Fact]
    public void Every_other_launch_plays_the_launch_animation_only()
    {
        Create().ScheduleStartupAnimation(firstRun: false);
        _scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["launch-animation"], _log);
    }

    [Fact]
    public void A_failing_animation_is_swallowed()
    {
        var svc = new StartupMigrationService(Markers(), () => { }, _scheduler,
            () => throw new InvalidOperationException("no window"), () => { });
        svc.ScheduleStartupAnimation(firstRun: true);
        _scheduler.Advance(TimeSpan.FromSeconds(1));   // must not throw
    }

    [Fact]
    public void Across_two_launches_the_install_animation_plays_only_the_first_time()
    {
        for (int launch = 0; launch < 2; launch++)
        {
            var svc = Create();
            svc.ScheduleStartupAnimation(svc.ConsumeFirstRunMarker());
            _scheduler.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(["install-animation", "launch-animation"], _log);
    }
}
