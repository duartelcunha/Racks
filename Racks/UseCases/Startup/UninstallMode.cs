using Racks.Infrastructure.Registry;
using Racks.Util;

namespace Racks.UseCases.Startup;

/// <summary>Arms a timeout that runs an action if the process is still alive when it expires.</summary>
public interface IWatchdog
{
    void Arm(TimeSpan timeout, Action onTimeout);
}

/// <summary>
/// The two headless runs started by the uninstaller (installer/Racks.iss) before it removes the app. Moved unchanged out of
/// <c>App</c>: no windows, no single-instance name, no workspace.
///
/// <list type="bullet">
/// <item><c>--uninstall-cleanup [report-path]</c>: gives the user's rack files back to the Desktop and removes what Racks
/// added to the shell; the report goes to the path when one is given. Guarded by a watchdog: the uninstaller waits for this
/// process, so it must never hang (a shell COM call that never returns), and exits with code 2 after
/// <see cref="WatchdogTimeout"/> whatever happens.</item>
/// <item><c>--uninstall-anim</c>: only the goodbye animation.</item>
/// </list>
/// While either runs, <see cref="IsRunningForUninstaller"/> is true so crash reporting does not recreate %AppData%\Racks after
/// the uninstaller removed it.
/// </summary>
public sealed class UninstallMode
{
    public const string CleanupArg = "--uninstall-cleanup";
    public const string AnimationArg = "--uninstall-anim";
    public const int WatchdogExitCode = 2;
    public static readonly TimeSpan WatchdogTimeout = TimeSpan.FromMinutes(2);

    private readonly IWatchdog _watchdog;
    private readonly Action<int> _exit;
    private readonly Func<IReadOnlyDictionary<string, string>, CleanupReport> _cleanup;
    private readonly Func<IRackSettingsStore> _store;

    public UninstallMode(
        IWatchdog watchdog,
        Action<int> exit,
        Func<IReadOnlyDictionary<string, string>, CleanupReport> cleanup,
        Func<IRackSettingsStore> store)
    {
        _watchdog = watchdog;
        _exit = exit;
        _cleanup = cleanup;
        _store = store;
    }

    /// <summary>True from the moment an uninstall run starts until the process ends.</summary>
    public bool IsRunningForUninstaller { get; private set; }

    /// <summary>Only the FIRST argument selects a mode, as before.</summary>
    public static StartupMode ParseMode(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return StartupMode.Normal;
        return args[0] switch
        {
            CleanupArg => StartupMode.UninstallCleanup,
            AnimationArg => StartupMode.UninstallAnimation,
            _ => StartupMode.Normal,
        };
    }

    /// <summary>Marks the process as an uninstaller run (the animation mode does nothing else).</summary>
    public void MarkRunningForUninstaller() => IsRunningForUninstaller = true;

    /// <summary>
    /// Arms the watchdog, runs the cleanup and writes the report when a path is the second argument. A cleanup that throws
    /// becomes a report with one error line; a report that cannot be written is ignored.
    /// </summary>
    public CleanupReport RunCleanup(IReadOnlyList<string> args)
    {
        MarkRunningForUninstaller();
        _watchdog.Arm(WatchdogTimeout, () => _exit(WatchdogExitCode));

        string? reportPath = args.Count > 1 ? args[1] : null;
        CleanupReport report;
        try
        {
            report = _cleanup(Titles());
        }
        catch (Exception ex)
        {
            report = new CleanupReport();
            report.Errors.Add("cleanup failed: " + ex.Message);
        }
        if (string.IsNullOrEmpty(reportPath)) return report;
        try { report.WriteTo(reportPath); } catch { }
        return report;
    }

    // Rack titles only make leftover sandboxes readable; never let them stop the cleanup.
    private Dictionary<string, string> Titles()
    {
        try { return RackTitles.ByFolder(_store()); }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }
}

/// <summary>Folder to rack title for every saved rack, so leftover sandboxes get readable names in the uninstall report.</summary>
public static class RackTitles
{
    public static Dictionary<string, string> ByFolder(IRackSettingsStore store)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Validated values from the settings store; racks it cannot load are simply not named.
            foreach (var rack in store.LoadAll().Racks)
                if (rack.Settings.Folder is { } folder && rack.Settings.TitleText is { } title)
                    titles[folder] = title;
        }
        catch { }
        return titles;
    }
}
