using Racks.UseCases.Startup;
using static Racks.UseCases.Startup.StartupSteps;

namespace Racks.Tests.Startup;

[Collection("Registry")]
public sealed class AppBootstrapServiceTests
{
    private static readonly string[] NormalOrder =
    [
        ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg,
        MigrateLegacyRegistry, EnableDarkMode, AcquireSingleInstance, SetBindingTraceLevel, InitializeWorkspace,
        SetStartupUri, RaiseBaseStartup, ReadFirstRunMarker, RegisterToastActivation, PinQuickAccess, ScheduleStartupAnimation,
    ];

    [Fact]
    public void The_normal_startup_order_is_the_one_the_app_always_had()
    {
        using var h = StartupHarness.Create();
        Assert.Equal(NormalOrder, h.Bootstrap.StepNames(StartupMode.Normal));
    }

    [Fact]
    public void Running_the_normal_startup_executes_exactly_those_steps_in_that_order()
    {
        using var h = StartupHarness.Create();

        bool goOn = h.Bootstrap.Run(Array.Empty<string>());

        Assert.True(goOn);
        Assert.Equal(NormalOrder, h.Bootstrap.RanSteps);
        // The effects happened in the same order (the single-instance line carries the name it asked for).
        Assert.Equal(
            NormalOrder.Where(n => n is not (ReadFirstRunMarker or ScheduleStartupAnimation)).Select(n => n == AcquireSingleInstance ? AcquireSingleInstance + ":" + SingleInstanceGuard.MutexName : n),
            h.Log);
    }

    [Fact]
    public void The_hardening_comes_before_anything_that_can_open_a_file_or_a_window()
    {
        using var h = StartupHarness.Create();
        var order = h.Bootstrap.StepNames(StartupMode.Normal).ToList();

        int svg = order.IndexOf(ApplySafeSvg);
        int regex = order.IndexOf(SetRegexDefaultTimeout);
        int lang = order.IndexOf(OverrideWpfLanguage);
        int workspace = order.IndexOf(InitializeWorkspace);
        int uri = order.IndexOf(SetStartupUri);
        int baseStartup = order.IndexOf(RaiseBaseStartup);

        Assert.True(svg < uri, "SafeSvg.Apply before StartupUri");
        Assert.True(regex < uri && regex < order.IndexOf(MigrateLegacyRegistry), "regex timeout before anything can run a regex");
        Assert.True(lang < order.IndexOf(SetStartupUri), "WPF language override before any element exists");
        Assert.True(workspace < uri, "the workspace folder exists before MainWindow (and any rack window) is created");
        Assert.True(uri < baseStartup, "StartupUri is set before the base OnStartup");
        Assert.Equal(order.Count - 1, order.IndexOf(ScheduleStartupAnimation));
        Assert.True(order.IndexOf(EnableDarkMode) < uri, "dark mode before any menu can be shown");
    }

    [Fact]
    public void A_second_instance_shuts_down_before_any_ui_work()
    {
        using var h = StartupHarness.CreateWithRunningInstance();

        bool goOn = h.Bootstrap.Run(Array.Empty<string>());

        Assert.False(goOn);
        Assert.Equal(AcquireSingleInstance, h.Bootstrap.RanSteps[^1]);
        Assert.Contains("Shutdown(0)", h.Log);
        Assert.DoesNotContain(SetStartupUri, h.Log);
        Assert.DoesNotContain(InitializeWorkspace, h.Log);
        Assert.DoesNotContain(RaiseBaseStartup, h.Log);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void The_cleanup_run_exits_before_any_ui_and_skips_the_migration_and_the_single_instance_name()
    {
        using var h = StartupHarness.Create();

        bool goOn = h.Bootstrap.Run(["--uninstall-cleanup", @"C:\x\report.txt"]);

        Assert.False(goOn);
        Assert.Equal(
            [ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg, RunUninstallCleanup, ShutdownAfterCleanup],
            h.Bootstrap.RanSteps);
        Assert.Equal(StartupMode.UninstallCleanup, UninstallMode.ParseMode(["--uninstall-cleanup"]));
        Assert.Equal(RunUninstallCleanup, h.Log[^2]);
        Assert.Equal("Shutdown(0)", h.Log[^1]);
        Assert.DoesNotContain(MigrateLegacyRegistry, h.Log);
        Assert.DoesNotContain(h.Log, l => l.StartsWith(AcquireSingleInstance));
        Assert.DoesNotContain(SetStartupUri, h.Log);
        Assert.True(h.Uninstall.IsRunningForUninstaller);
    }

    [Fact]
    public void The_animation_run_shows_only_the_goodbye_animation_and_takes_no_single_instance_name()
    {
        using var h = StartupHarness.Create();

        bool goOn = h.Bootstrap.Run(["--uninstall-anim"]);

        Assert.False(goOn);
        Assert.Equal(
            [ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg, MigrateLegacyRegistry, EnableDarkMode, MarkUninstallAnimation, RunUninstallAnimation],
            h.Bootstrap.RanSteps);
        Assert.DoesNotContain(h.Log, l => l.StartsWith(AcquireSingleInstance));
        Assert.DoesNotContain(InitializeWorkspace, h.Log);
        Assert.DoesNotContain(SetStartupUri, h.Log);
        Assert.True(h.Uninstall.IsRunningForUninstaller);
    }

    [Fact]
    public void Every_mode_applies_the_svg_and_regex_settings_first()
    {
        foreach (var mode in Enum.GetValues<StartupMode>())
        {
            using var h = StartupHarness.Create();
            Assert.Equal(
                [ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg],
                h.Bootstrap.StepNames(mode).Take(4));
        }
    }

    [Fact]
    public void Only_the_first_argument_selects_a_mode()
    {
        using var h = StartupHarness.Create();
        h.Bootstrap.Run(["something", "--uninstall-cleanup"]);
        Assert.Equal(NormalOrder, h.Bootstrap.RanSteps);
    }

    [Fact]
    public void A_debug_build_has_no_single_instance_name()
    {
        using var h = StartupHarness.Create(singleInstance: false);
        Assert.True(h.Bootstrap.Run(Array.Empty<string>()));
        Assert.DoesNotContain(h.Log, l => l.StartsWith(AcquireSingleInstance + ":"));
        Assert.Null(h.Handle);
    }

    [Fact]
    public void Running_again_repeats_the_same_steps_and_a_second_Run_does_not_stack_the_step_list()
    {
        using var h = StartupHarness.Create();
        h.Bootstrap.Run(Array.Empty<string>());
        h.Bootstrap.Run(Array.Empty<string>());
        Assert.Equal(NormalOrder, h.Bootstrap.RanSteps);
    }

    [Fact]
    public void Exit_restores_the_icons_then_releases_the_name_once()
    {
        using var h = StartupHarness.Create();
        h.Bootstrap.Run(Array.Empty<string>());
        h.Log.Clear();

        h.Bootstrap.Exit();
        h.Bootstrap.Exit();   // idempotent

        Assert.Equal(["RestoreDesktopIcons", "mutex-release", "mutex-dispose", "RestoreDesktopIcons"], h.Log);
        Assert.Equal(1, h.Handle!.Released);
        Assert.Equal(1, h.Handle.Disposed);
    }

    [Fact]
    public void Exit_still_releases_the_name_when_restoring_the_icons_fails()
    {
        using var h = StartupHarness.Create();
        h.Bootstrap.Run(Array.Empty<string>());
        h.Host.ThrowOnRestore = true;

        h.Bootstrap.Exit();

        Assert.Equal(1, h.Handle!.Released);
    }

    [Fact]
    public void A_refused_second_instance_only_disposes_the_name_it_never_owned()
    {
        using var h = StartupHarness.CreateWithRunningInstance();
        h.Bootstrap.Run(Array.Empty<string>());

        h.Bootstrap.Exit();

        Assert.Equal(0, h.Handle!.Released);   // releasing a mutex we do not own throws
        Assert.Equal(1, h.Handle.Disposed);
    }

    [Fact]
    public void The_single_instance_name_is_session_scoped_and_unchanged()
    {
        Assert.Equal(@"Local\Racks-SingleInstance-2C9D", SingleInstanceGuard.MutexName);
    }

    [Fact]
    public void Exit_without_a_startup_is_safe()
    {
        using var h = StartupHarness.Create();
        h.Bootstrap.Exit();
        Assert.Equal(["RestoreDesktopIcons"], h.Log);
    }
}
