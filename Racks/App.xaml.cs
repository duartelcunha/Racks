using Racks.Properties;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading;
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
        private DispatcherTimer updateTimer;
        // Hold a named Mutex for the lifetime of the process. Second-launch detects
        // this in <1ms and exits silently — the existing tray icon is already there.
        // Beats the previous Process.GetProcessesByName check, which raced on startup
        // and popped a modal dialog when you double-clicked the exe.
        private static Mutex _singleInstanceMutex;
        public RegistryHelper reg = new RegistryHelper(InstanceController.appName);
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

            // Play the signature startup animation
            var startupAnim = new Racks.Views.StartupAnimationWindow();
            startupAnim.Show();
            
            // Fences-style Desktop Integration
            Racks.Core.DesktopIconManager.Initialize();
            Racks.Core.DesktopIconManager.StartHook();

            ToastNotificationManagerCompat.OnActivated += ToastActivatedHandler;
            StartUpdateCheckTimer();
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
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Remove the C++ desktop hook
            Racks.Core.DesktopIconManager.StopHook();
            base.OnExit(e);
        }
        private void ToastActivatedHandler(ToastNotificationActivatedEventArgsCompat toastArgs)
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            Current.Dispatcher.Invoke(async () =>
            {
                if (args.Contains("action") && args["action"] == "install_update")
                {
                   await Updater.InstallUpdate();
                }

            });
        }
        private void StartUpdateCheckTimer()
        {
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(6)
            };
            updateTimer.Tick += async (_, _) =>
            {
                // Auto-update disabled — no release pipeline. Intentional no-op so the
                // timer doesn't hammer a placeholder URL every six hours.
                await Task.CompletedTask;
            };
            updateTimer.Start();
        }
    }

}
