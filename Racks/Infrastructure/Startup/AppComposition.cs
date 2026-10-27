using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using Racks.Infrastructure.Registry;
using Racks.UseCases.Crash;
using Racks.UseCases.Startup;
using Racks.Util;
using Application = System.Windows.Application;

namespace Racks.Infrastructure.Startup;

/// <summary>The services <c>App</c> keeps for its lifetime.</summary>
internal sealed record AppServices(CrashReportingService Crash, AppBootstrapService Bootstrap);

/// <summary>Builds the startup services by hand (no DI container). Nothing here runs until the app calls them.</summary>
internal static class AppComposition
{
    public static AppServices Create(Application app, Action raiseBaseStartup)
    {
        var uninstall = new UninstallMode(
            new ThreadWatchdog(),
            Environment.Exit,
            titles => UninstallCleanup.Run(CleanupPaths.ForCurrentUser(), titles, touchShell: true),
            () => Instance.Store);

        var crash = new CrashReportingService(
            () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                InstanceController.appName),
            (dir, entry) => CrashLog.Append(dir, entry),
            ShowCrashToast,
            () => uninstall.IsRunningForUninstaller);

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
        return new AppServices(crash, bootstrap);
    }

    private static void ShowCrashToast() =>
        new ToastContentBuilder()
            .AddText(Properties.Lang.Crash_Toast_Title)
            .AddText(Properties.Lang.Crash_Toast_Body)
            .Show();
}
