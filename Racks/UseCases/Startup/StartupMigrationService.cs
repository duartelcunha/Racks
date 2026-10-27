using System.Diagnostics;
using Racks.Core.Abstractions;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Startup;

/// <summary>
/// The one-time work of a launch: the DeskFrame registry migration, the first-run markers (welcome animation, Quick Access
/// pin) and the startup animation. Moved unchanged out of <c>App.OnStartup</c>.
///
/// <list type="bullet">
/// <item>The install animation is shown once per machine (<see cref="MigrationMarkers.InstallAnimationShownV2"/>); every
/// other launch plays the shorter launch animation.</item>
/// <item>The Quick Access pin is gated by <see cref="MigrationMarkers.QuickAccessMirrorPinned"/>, set only after the pin
/// worked, so a user who unpins it by hand is not pinned again, and a failed pin is tried again next launch.</item>
/// <item>The animation waits <see cref="AnimationDelay"/>: after the heavy startup work and after the main window painted. A
/// real delay (not a dispatcher priority, which the always-busy timers and hooks can starve), then the action runs on the UI thread.</item>
/// </list>
/// </summary>
public sealed class StartupMigrationService
{
    public static readonly TimeSpan AnimationDelay = TimeSpan.FromMilliseconds(500);

    private readonly MigrationMarkers _markers;
    private readonly Action _migrateLegacyRegistry;
    private readonly Action _pinQuickAccess;
    private readonly IDelayScheduler _scheduler;
    private readonly Action _playInstallAnimation;
    private readonly Action _playLaunchAnimation;

    public StartupMigrationService(
        MigrationMarkers markers,
        Action migrateLegacyRegistry,
        Action pinQuickAccess,
        IDelayScheduler scheduler,
        Action playInstallAnimation,
        Action playLaunchAnimation)
    {
        _markers = markers;
        _migrateLegacyRegistry = migrateLegacyRegistry;
        _pinQuickAccess = pinQuickAccess;
        _scheduler = scheduler;
        _playInstallAnimation = playInstallAnimation;
        _playLaunchAnimation = playLaunchAnimation;
    }

    /// <summary>Copies HKCU\SOFTWARE\DeskFrame to HKCU\SOFTWARE\Racks once, so users coming from the original build keep their racks.</summary>
    public void MigrateLegacyRegistry() => _migrateLegacyRegistry();

    /// <summary>True when this is the first launch after install (and marks it, so the next launch is not).</summary>
    public bool ConsumeFirstRunMarker()
    {
        try
        {
            if (_markers.IsSet(MigrationMarkers.InstallAnimationShownV2)) return false;
            _markers.Set(MigrationMarkers.InstallAnimationShownV2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pins %USERPROFILE%\Racks to Explorer's Quick Access once, so any file picker can reach rack contents.</summary>
    public void PinQuickAccessOnce()
    {
        try
        {
            if (_markers.IsSet(MigrationMarkers.QuickAccessMirrorPinned)) return;
            _pinQuickAccess();
            _markers.Set(MigrationMarkers.QuickAccessMirrorPinned);
        }
        catch (Exception ex) { Debug.WriteLine($"Quick Access pin failed: {ex.Message}"); }
    }

    /// <summary>Plays the install animation (first run) or the launch animation after <see cref="AnimationDelay"/>.</summary>
    public void ScheduleStartupAnimation(bool firstRun)
    {
        _scheduler.Schedule(AnimationDelay, () =>
        {
            try
            {
                if (firstRun) _playInstallAnimation();
                else _playLaunchAnimation();
            }
            catch (Exception ex) { Debug.WriteLine($"Startup animation failed: {ex.Message}"); }
        });
    }
}
