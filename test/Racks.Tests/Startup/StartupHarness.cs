using Racks.Core.Abstractions;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;
using Racks.UseCases.Startup;
using Racks.Util;

namespace Racks.Tests.Startup;

/// <summary>Everything the startup would do, recorded into one log in the order it happens.</summary>
internal sealed class StartupHarness : IDisposable
{
    private readonly Settings.ThrowawayRegistry _reg = new();

    public List<string> Log { get; } = new();
    public RecordingHost Host { get; }
    public FakeWatchdog Watchdog { get; } = new();
    public List<int> Exits { get; } = new();
    public FakeHandle? Handle { get; private set; }
    public bool HandleCreatedNew { get; set; } = true;
    public ManualScheduler Scheduler { get; } = new();
    public MigrationMarkers Markers { get; }
    public CleanupReport CleanupResult { get; set; } = new();
    public List<IReadOnlyDictionary<string, string>> CleanupTitles { get; } = new();
    public UninstallMode Uninstall { get; }
    public StartupMigrationService Migrations { get; }
    public SingleInstanceGuard Guard { get; }
    public AppBootstrapService Bootstrap { get; }

    private StartupHarness(bool singleInstance = true)
    {
        Host = new RecordingHost(Log);
        Markers = new MigrationMarkers(_reg.Root, new RecordingAuditSink());
        Uninstall = new UninstallMode(Watchdog, Exits.Add, titles =>
        {
            Log.Add(StartupSteps.RunUninstallCleanup);
            CleanupTitles.Add(titles);
            return CleanupResult;
        }, () => new EmptyStore());
        Migrations = new StartupMigrationService(
            Markers,
            () => Log.Add(StartupSteps.MigrateLegacyRegistry),
            () => Log.Add(StartupSteps.PinQuickAccess),
            Scheduler,
            () => Log.Add("play-install-animation"),
            () => Log.Add("play-launch-animation"));
        Guard = new SingleInstanceGuard(singleInstance, name =>
        {
            Log.Add(StartupSteps.AcquireSingleInstance + ":" + name);
            Handle = new FakeHandle(HandleCreatedNew, Log);
            return Handle;
        });
        Bootstrap = new AppBootstrapService(Host, Uninstall, Guard, Migrations);
    }

    public static StartupHarness Create(bool singleInstance = true) => new(singleInstance);

    /// <summary>Same as <see cref="Create"/> but the next acquire sees another Racks holding the name.</summary>
    public static StartupHarness CreateWithRunningInstance()
    {
        var h = new StartupHarness { HandleCreatedNew = false };
        return h;
    }

    public void Dispose() => _reg.Dispose();

    public sealed class RecordingHost(List<string> log) : IStartupHost
    {
        public bool ThrowOnRestore { get; set; }
        public void ConfigureCulturePreview() => log.Add(StartupSteps.ConfigureCulturePreview);
        public void OverrideWpfLanguage() => log.Add(StartupSteps.OverrideWpfLanguage);
        public void SetRegexDefaultTimeout() => log.Add(StartupSteps.SetRegexDefaultTimeout);
        public void ApplySafeSvg() => log.Add(StartupSteps.ApplySafeSvg);
        public void EnableDarkMode() => log.Add(StartupSteps.EnableDarkMode);
        public void SetBindingTraceLevel() => log.Add(StartupSteps.SetBindingTraceLevel);
        public void InitializeWorkspace() => log.Add(StartupSteps.InitializeWorkspace);
        public void SetStartupUri() => log.Add(StartupSteps.SetStartupUri);
        public void RaiseBaseStartup() => log.Add(StartupSteps.RaiseBaseStartup);
        public void RegisterToastActivation() => log.Add(StartupSteps.RegisterToastActivation);
        public void RunUninstallAnimation() => log.Add(StartupSteps.RunUninstallAnimation);
        public void Shutdown(int exitCode) => log.Add("Shutdown(" + exitCode + ")");
        public void RestoreDesktopIcons()
        {
            log.Add("RestoreDesktopIcons");
            if (ThrowOnRestore) throw new InvalidOperationException("explorer is gone");
        }
    }

    public sealed class FakeHandle(bool createdNew, List<string> log) : ISingleInstanceHandle
    {
        public bool CreatedNew { get; } = createdNew;
        public int Released { get; private set; }
        public int Disposed { get; private set; }
        public void Release() { Released++; log.Add("mutex-release"); }
        public void Dispose() { Disposed++; log.Add("mutex-dispose"); }
    }

    public sealed class FakeWatchdog : IWatchdog
    {
        public List<TimeSpan> Armed { get; } = new();
        private Action? _onTimeout;
        public void Arm(TimeSpan timeout, Action onTimeout) { Armed.Add(timeout); _onTimeout = onTimeout; }
        public void Fire() => _onTimeout!();
    }

    /// <summary>A settings store with no racks.</summary>
    public sealed class EmptyStore : IRackSettingsStore
    {
        public RackLoadResult LoadAll() => new(Array.Empty<LoadedRack>(), Array.Empty<SettingIssue>());
        public RackSettings ReadGlobalStyleDefaults() => throw new NotSupportedException();
        public bool Save(Instance rack) => throw new NotSupportedException();
        public bool Normalize(Instance rack) => throw new NotSupportedException();
        public bool SaveField(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool SaveGlobalDefault(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool Delete(string name) => throw new NotSupportedException();
        public int ResetGlobalStyleDefaults() => throw new NotSupportedException();
    }
}
