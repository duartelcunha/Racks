namespace Racks.UseCases.Startup;

/// <summary>What the bootstrap needs from WPF and Windows; the app's implementation is <c>AppStartupHost</c>, tests use a recording fake.</summary>
public interface IStartupHost
{
    /// <summary>RACKS_LANG culture preview (translators).</summary>
    void ConfigureCulturePreview();
    /// <summary>Tells WPF which language the text is in, so CJK text picks the right glyph forms. Before any element exists.</summary>
    void OverrideWpfLanguage();
    /// <summary>Caps the runtime of every regex without an explicit timeout (ReDoS backstop). Before any regex runs.</summary>
    void SetRegexDefaultTimeout();
    /// <summary>SVG thumbnails never fetch external images, elements or entities (INV-IMPORT-4).</summary>
    void ApplySafeSvg();
    /// <summary>Dark mode for native popups such as the shell context menu. Before any menu is shown.</summary>
    void EnableDarkMode();
    void SetBindingTraceLevel();
    /// <summary>Creates the workspace folder. It must exist before any rack window does (they read its path).</summary>
    void InitializeWorkspace();
    /// <summary>Sets <c>StartupUri</c> to the main window. WPF reads it after OnStartup returns and it cannot be cleared once set.</summary>
    void SetStartupUri();
    /// <summary><c>base.OnStartup</c> of the WPF application.</summary>
    void RaiseBaseStartup();
    /// <summary>Handles clicks on toast notifications (<c>install_update</c> starts the update install).</summary>
    void RegisterToastActivation();
    /// <summary>Plays the goodbye animation and shuts the app down when it ends.</summary>
    void RunUninstallAnimation();
    void Shutdown(int exitCode);
    /// <summary>Puts the desktop icons back if Racks hid them.</summary>
    void RestoreDesktopIcons();
}

/// <summary>Names of the startup steps (see <see cref="AppBootstrapService.StepNames"/> for the order per mode).</summary>
public static class StartupSteps
{
    public const string ConfigureCulturePreview = nameof(ConfigureCulturePreview);
    public const string OverrideWpfLanguage = nameof(OverrideWpfLanguage);
    public const string SetRegexDefaultTimeout = nameof(SetRegexDefaultTimeout);
    public const string ApplySafeSvg = nameof(ApplySafeSvg);
    public const string RunUninstallCleanup = nameof(RunUninstallCleanup);
    public const string ShutdownAfterCleanup = nameof(ShutdownAfterCleanup);
    public const string MigrateLegacyRegistry = nameof(MigrateLegacyRegistry);
    public const string EnableDarkMode = nameof(EnableDarkMode);
    public const string MarkUninstallAnimation = nameof(MarkUninstallAnimation);
    public const string AcquireSingleInstance = nameof(AcquireSingleInstance);
    public const string RunUninstallAnimation = nameof(RunUninstallAnimation);
    public const string SetBindingTraceLevel = nameof(SetBindingTraceLevel);
    public const string InitializeWorkspace = nameof(InitializeWorkspace);
    public const string SetStartupUri = nameof(SetStartupUri);
    public const string RaiseBaseStartup = nameof(RaiseBaseStartup);
    public const string ReadFirstRunMarker = nameof(ReadFirstRunMarker);
    public const string RegisterToastActivation = nameof(RegisterToastActivation);
    public const string PinQuickAccess = nameof(PinQuickAccess);
    public const string ScheduleStartupAnimation = nameof(ScheduleStartupAnimation);
}

/// <summary>
/// What <c>App.OnStartup</c> and <c>App.OnExit</c> do, as one ordered list of named steps per mode. This is the only place
/// the startup order lives; a test reads it (and runs it with fakes) instead of reading source text.
///
/// Order, per mode (a step returning false ends the run; the app then does not continue to the main window):
/// <code>
/// Normal             ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg,
///                    MigrateLegacyRegistry, EnableDarkMode, AcquireSingleInstance (a second Racks shuts down here),
///                    SetBindingTraceLevel, InitializeWorkspace, SetStartupUri, RaiseBaseStartup,
///                    ReadFirstRunMarker, RegisterToastActivation, PinQuickAccess, ScheduleStartupAnimation
/// UninstallCleanup   ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg,
///                    RunUninstallCleanup, ShutdownAfterCleanup          (no registry migration, no single instance, no UI)
/// UninstallAnimation ConfigureCulturePreview, OverrideWpfLanguage, SetRegexDefaultTimeout, ApplySafeSvg,
///                    MigrateLegacyRegistry, EnableDarkMode, MarkUninstallAnimation, RunUninstallAnimation (no single instance)
/// </code>
/// WPF creates <c>MainWindow</c> from <c>StartupUri</c> only after <c>OnStartup</c> returns, so after the whole Normal list.
/// The SVG hardening and the regex timeout come before anything can parse a file, and the workspace folder exists before
/// any rack window reads its path.
/// </summary>
public sealed class AppBootstrapService
{
    private readonly IStartupHost _host;
    private readonly UninstallMode _uninstall;
    private readonly SingleInstanceGuard _instance;
    private readonly StartupMigrationService _migrations;
    private bool _firstRun;
    private readonly List<string> _ran = new();

    public AppBootstrapService(IStartupHost host, UninstallMode uninstall, SingleInstanceGuard instance, StartupMigrationService migrations)
    {
        _host = host;
        _uninstall = uninstall;
        _instance = instance;
        _migrations = migrations;
    }

    /// <summary>The steps that ran in the last <see cref="Run"/>, in order.</summary>
    public IReadOnlyList<string> RanSteps => _ran;

    /// <summary>The names of the steps of a mode, in order. Pure: nothing runs.</summary>
    public IReadOnlyList<string> StepNames(StartupMode mode) => BuildSteps(mode, Array.Empty<string>()).Select(s => s.Name).ToList();

    /// <summary>Runs the startup. False when the app must not go on to the main window (uninstall run, or a second instance).</summary>
    public bool Run(IReadOnlyList<string> args)
    {
        _ran.Clear();
        var mode = UninstallMode.ParseMode(args);
        foreach (var (name, step) in BuildSteps(mode, args))
        {
            _ran.Add(name);
            if (!step()) return false;
        }
        return mode == StartupMode.Normal;
    }

    /// <summary>OnExit: icons Racks hid come back on a normal exit (tray Exit, update, Windows shutdown), then the single-instance name is released.</summary>
    public void Exit()
    {
        try { _host.RestoreDesktopIcons(); } catch { }
        _instance.Release();
    }

    private List<(string Name, Func<bool> Run)> BuildSteps(StartupMode mode, IReadOnlyList<string> args)
    {
        var steps = new List<(string Name, Func<bool> Run)>
        {
            (StartupSteps.ConfigureCulturePreview, Do(_host.ConfigureCulturePreview)),
            (StartupSteps.OverrideWpfLanguage, Do(_host.OverrideWpfLanguage)),
            (StartupSteps.SetRegexDefaultTimeout, Do(_host.SetRegexDefaultTimeout)),
            (StartupSteps.ApplySafeSvg, Do(_host.ApplySafeSvg)),
        };
        switch (mode)
        {
            case StartupMode.UninstallCleanup:
                steps.Add((StartupSteps.RunUninstallCleanup, () => { _uninstall.RunCleanup(args); return true; }));
                steps.Add((StartupSteps.ShutdownAfterCleanup, () => { _host.Shutdown(0); return false; }));
                break;
            case StartupMode.UninstallAnimation:
                steps.Add((StartupSteps.MigrateLegacyRegistry, Do(_migrations.MigrateLegacyRegistry)));
                steps.Add((StartupSteps.EnableDarkMode, Do(_host.EnableDarkMode)));
                steps.Add((StartupSteps.MarkUninstallAnimation, Do(_uninstall.MarkRunningForUninstaller)));
                steps.Add((StartupSteps.RunUninstallAnimation, () => { _host.RunUninstallAnimation(); return false; }));
                break;
            default:
                steps.Add((StartupSteps.MigrateLegacyRegistry, Do(_migrations.MigrateLegacyRegistry)));
                steps.Add((StartupSteps.EnableDarkMode, Do(_host.EnableDarkMode)));
                steps.Add((StartupSteps.AcquireSingleInstance, () =>
                {
                    if (_instance.TryAcquire()) return true;
                    // Another Racks is already running in this session; its tray icon is live. Just exit.
                    _host.Shutdown(0);
                    return false;
                }
                ));
                steps.Add((StartupSteps.SetBindingTraceLevel, Do(_host.SetBindingTraceLevel)));
                steps.Add((StartupSteps.InitializeWorkspace, Do(_host.InitializeWorkspace)));
                steps.Add((StartupSteps.SetStartupUri, Do(_host.SetStartupUri)));
                steps.Add((StartupSteps.RaiseBaseStartup, Do(_host.RaiseBaseStartup)));
                steps.Add((StartupSteps.ReadFirstRunMarker, () => { _firstRun = _migrations.ConsumeFirstRunMarker(); return true; }));
                steps.Add((StartupSteps.RegisterToastActivation, Do(_host.RegisterToastActivation)));
                steps.Add((StartupSteps.PinQuickAccess, Do(_migrations.PinQuickAccessOnce)));
                steps.Add((StartupSteps.ScheduleStartupAnimation, () => { _migrations.ScheduleStartupAnimation(_firstRun); return true; }));
                break;
        }
        return steps;
    }

    private static Func<bool> Do(Action action) => () => { action(); return true; };
}
