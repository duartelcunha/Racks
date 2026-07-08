using Racks.Properties;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Racks
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        // Hold a named Mutex for the lifetime of the process. Second-launch detects
        // this in <1ms and exits silently — the existing tray icon is already there.
        // Beats the previous Process.GetProcessesByName check, which raced on startup
        // and popped a modal dialog when you double-clicked the exe.
        #pragma warning disable CS0649
        private static Mutex? _singleInstanceMutex;
#pragma warning restore CS0649
        public RegistryHelper reg = new RegistryHelper(InstanceController.appName);

        public App()
        {
            // Racks is a background tray app that's meant to keep running for the
            // whole session - a single bad rename, a bad saved regex, a stray null
            // ref in a click handler, shouldn't take down every open rack with it.
            // These are the last line of defense: log what happened and keep going
            // instead of vanishing with no explanation. Wired in the constructor so
            // they're active before OnStartup does anything else.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Non-UI-thread exceptions are always fatal on .NET - this can't stop the
            // crash, only make sure there's a record of what actually happened.
            if (e.ExceptionObject is Exception ex) LogUnhandledException(ex, "AppDomainUnhandledException");
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        }

        private static void LogUnhandledException(Exception ex, string source)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    InstanceController.appName);
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* logging is best-effort; never let it mask the original exception */ }
            Debug.WriteLine($"{source}: {ex}");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // One-time migration of HKCU\SOFTWARE\DeskFrame → HKCU\SOFTWARE\Racks so
            // users upgrading from the original DeskFrame build keep their frames.
            InstanceController.MigrateLegacyRegistry();
            // Tell Windows we'd like dark mode for native popups (shell context
            // menu). Has to run before any menu is shown.
            Racks.Util.DarkModeHelper.EnableForApp();
            bool isUninstallAnim = e.Args.Length > 0 && e.Args[0] == "--uninstall-anim";
#if !DEBUG
            if (!isUninstallAnim)
            {
                bool createdNew;
                _singleInstanceMutex = new Mutex(true, @"Global\Racks-SingleInstance-2C9D", out createdNew);
                if (!createdNew)
                {
                    // Another Racks is already running — its tray icon is live. Just exit.
                    Application.Current.Shutdown();
                    return;
                }
            }
#endif
            if (isUninstallAnim)
            {
                Racks.Util.LifecycleAnimations.RunUninstallAnimation(() => Application.Current.Shutdown());
                return;
            }

            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
            base.OnStartup(e);

            // Two distinct animations:
            //  - FIRST run after install: the signature "logo rolls in and drops into the
            //    tray" welcome (StartupAnimationWindow). Shown once per machine.
            //  - Every normal launch: a shorter "wake up" animation (the logo fades/pulses
            //    in at center and glides to the tray) - different from both the install
            //    animation and the quit animation.
            bool firstRun = false;
            try
            {
                const string AnimMarker = "InstallAnimationShownV2";
                if (!reg.KeyExistsRoot(AnimMarker)) { firstRun = true; reg.WriteToRegistryRoot(AnimMarker, true); }
            }
            catch { }

            // Fences-style Desktop Integration
            Racks.Core.DesktopIconManager.Initialize();
            Racks.Core.DesktopIconManager.StartHook();

            ToastNotificationManagerCompat.OnActivated += ToastActivatedHandler;
            // Once-only: pin %USERPROFILE%\Racks to the Explorer / file-picker
            // Quick Access list so that the user can reach rack contents from
            // any picker without manually navigating. Gated by a registry
            // marker so we don't re-pin if the user manually unpins.
            try
            {
                const string PinnedMarker = "QuickAccessMirrorPinned";
                if (!reg.KeyExistsRoot(PinnedMarker))
                {
                    Racks.Util.RackMirror.PinToQuickAccess();
                    reg.WriteToRegistryRoot(PinnedMarker, true);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Quick Access pin failed: {ex.Message}"); }

            // Play the startup animation AFTER the heavy startup work above and after the
            // MainWindow has painted. A real delay (not just a dispatcher priority, which the
            // app's always-busy timers/hooks can starve) then a UI-thread invoke, so the
            // animation window is created when the thread is free to actually render it.
            bool playInstall = firstRun;
            Task.Delay(500).ContinueWith(_ =>
            {
                Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (playInstall) new Racks.Views.StartupAnimationWindow().Show();
                        else Racks.Util.LifecycleAnimations.RunLaunchAnimation();
                    }
                    catch (Exception ex) { Debug.WriteLine($"Startup animation failed: {ex.Message}"); }
                });
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Remove the C++ desktop hook
            try { Racks.Core.DesktopIconManager.StopHook(); } catch { }
            
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            
            base.OnExit(e);
        }
        private void ToastActivatedHandler(ToastNotificationActivatedEventArgsCompat toastArgs)
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            Current.Dispatcher.InvokeAsync(async () =>
            {
                if (args.Contains("action") && args["action"] == "install_update")
                {
                   await Updater.InstallUpdate();
                }

            });
        }

    }

}
