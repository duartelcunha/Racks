using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using Racks.Infrastructure.Registry;
using Racks.UseCases.Crash;
using Racks.UseCases.Startup;
using Racks.Util;
using Application = System.Windows.Application;

namespace Racks.Infrastructure.Startup;

/// <summary>The services <c>App</c> keeps for its lifetime.</summary>
internal sealed record AppServices(AppBootstrapService Bootstrap, UninstallMode Uninstall);

/// <summary>
/// Builds the startup services by hand (no DI container). This is the composition root: the one place in Infrastructure that
/// is allowed to know the use cases and wire them to their Windows implementations. Nothing here runs until the app calls it.
/// </summary>
internal static class AppComposition
{
    /// <summary>The crash reporter. It is built and attached first, so it only needs the uninstaller flag as a function.</summary>
    public static CrashReportingService CreateCrashService(Func<bool> runningForUninstaller) => new(
        () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            InstanceController.appName),
        (dir, entry) => CrashLog.Append(dir, entry),
        ShowCrashToast,
        runningForUninstaller);

    public static AppServices Create(Application app, Action raiseBaseStartup)
    {
        var uninstall = new UninstallMode(
            new ThreadWatchdog(),
            Environment.Exit,
            titles => UninstallCleanup.Run(CleanupPaths.ForCurrentUser(), titles, touchShell: true),
            () => Instance.Store);

        var migrations = new StartupMigrationService(
            MigrationMarkers.Default,
            InstanceController.MigrateLegacyRegistry,
            RackMirror.PinToQuickAccess,
            new TaskDispatcherDelayScheduler(),
            () => new Views.StartupAnimationWindow().Show(),
            LifecycleAnimations.RunLaunchAnimation);

#if DEBUG
        const bool singleInstance = false;
#else
        const bool singleInstance = true;
#endif
        var bootstrap = new AppBootstrapService(
            new AppStartupHost(app, raiseBaseStartup),
            uninstall,
            new SingleInstanceGuard(singleInstance, name => new MutexHandle(name)),
            migrations);
        return new AppServices(bootstrap, uninstall);
    }

    private static void ShowCrashToast() =>
        new ToastContentBuilder()
            .AddText(Properties.Lang.Crash_Toast_Title)
            .AddText(Properties.Lang.Crash_Toast_Body)
            .Show();
}
