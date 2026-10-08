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

        // True while running as part of the uninstaller (--uninstall-cleanup / --uninstall-anim).
        // Those runs must not recreate %AppData%\Racks after the uninstaller has removed it.
        private static bool s_runningForUninstaller;

        private static void LogUnhandledException(Exception ex, string source)
        {
            if (s_runningForUninstaller)
            {
                Debug.WriteLine($"{source}: {ex}");
                return;
            }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                InstanceController.appName);
            Racks.Util.CrashLog.Append(dir,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            Debug.WriteLine($"{source}: {ex}");
            TellUserOnce();
        }

        private static int s_toldUser;

        // The exception is swallowed so Racks keeps running, which also meant the user never knew
        // anything went wrong. Say so once per session, with where the details are.
        private static void TellUserOnce()
        {
            if (System.Threading.Interlocked.Exchange(ref s_toldUser, 1) != 0) return;
            try
            {
                new ToastContentBuilder()
                    .AddText(Racks.Properties.Lang.Crash_Toast_Title)
                    .AddText(Racks.Properties.Lang.Crash_Toast_Body)
                    .Show();
            }
            catch { /* best-effort */ }
        }

        private static void RunUninstallCleanup(string? reportPath)
        {
            Racks.Util.CleanupReport report;
            try
            {
                report = Racks.Util.UninstallCleanup.Run(
                    Racks.Util.CleanupPaths.ForCurrentUser(), ReadRackTitlesByFolder(), touchShell: true);
            }
            catch (Exception ex)
            {
                report = new Racks.Util.CleanupReport();
                report.Errors.Add("cleanup failed: " + ex.Message);
            }
            if (string.IsNullOrEmpty(reportPath)) return;
            try { report.WriteTo(reportPath); } catch { }
        }

        // Folder -> rack title for every saved rack, so leftover sandboxes get readable names.
        private static Dictionary<string, string> ReadRackTitlesByFolder()
        {
            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var instances = Registry.CurrentUser.OpenSubKey($@"Software\{InstanceController.appName}\Instances");
                if (instances == null) return titles;
                foreach (var name in instances.GetSubKeyNames())
                {
                    using var rack = instances.OpenSubKey(name);
                    if (rack?.GetValue("Folder") is string folder && rack.GetValue("TitleText") is string title)
                        titles[folder] = title;
                }
            }
            catch { }
            return titles;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Translators: set RACKS_LANG=zh-CN (any culture name) to preview a language without
            // changing the Windows display language. Unknown names are ignored.
            var previewLang = Environment.GetEnvironmentVariable("RACKS_LANG");
            if (!string.IsNullOrWhiteSpace(previewLang))
            {
                try
                {
                    var culture = System.Globalization.CultureInfo.GetCultureInfo(previewLang.Trim());
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                    System.Globalization.CultureInfo.CurrentUICulture = culture;
                    Racks.Properties.Lang.Culture = culture;
                }
                catch (System.Globalization.CultureNotFoundException) { }
            }
            // Belt for ReDoS: cap the runtime of ANY regex without an explicit timeout, so a
            // catastrophic-backtracking pattern (from the registry or an imported layout) can
            // never pin a thread. Explicit-timeout call sites (Util.SafeRegex) still win; this
            // only backstops anything that slips through. Must be set before any Regex runs.
            AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", Racks.Util.SafeRegex.MatchTimeout);
            // Run by the uninstaller (installer/Racks.iss) before it removes the app: give the
            // user's rack files back to the Desktop and remove what Racks added to the shell.
            // Headless: no windows, no mutex, no registry migration.
            if (e.Args.Length > 0 && e.Args[0] == "--uninstall-cleanup")
            {
                s_runningForUninstaller = true;
                // Watchdog: the uninstaller waits for this process, so it must never hang
                // (e.g. a shell COM call that never returns). Exit after 2 minutes regardless.
                new Thread(() => { Thread.Sleep(TimeSpan.FromMinutes(2)); Environment.Exit(2); })
                { IsBackground = true }.Start();
                RunUninstallCleanup(e.Args.Length > 1 ? e.Args[1] : null);
                Shutdown(0);
                return;
            }
            // One-time migration of HKCU\SOFTWARE\DeskFrame → HKCU\SOFTWARE\Racks so
            // users upgrading from the original DeskFrame build keep their frames.
            InstanceController.MigrateLegacyRegistry();
            // Tell Windows we'd like dark mode for native popups (shell context
            // menu). Has to run before any menu is shown.
            Racks.Util.DarkModeHelper.EnableForApp();
            bool isUninstallAnim = e.Args.Length > 0 && e.Args[0] == "--uninstall-anim";
            if (isUninstallAnim) s_runningForUninstaller = true;
#if !DEBUG
            if (!isUninstallAnim)
            {
                bool createdNew;
                // Local\ (session-scoped), not Global\: single-instance is a per-user-session
                // concept. Global\ let any process in ANY session pre-create the name and silently
                // block every Racks launch (a squatting DoS), and wrongly stopped two different
                // users from each running their own Racks. Local\ scopes it to this session.
                _singleInstanceMutex = new Mutex(true, @"Local\Racks-SingleInstance-2C9D", out createdNew);
                if (!createdNew)
                {
                    // Another Racks is already running in this session — its tray icon is live. Just exit.
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
            // Set here rather than in App.xaml so the headless modes above (--uninstall-cleanup,
            // --uninstall-anim, second instance) never load MainWindow. WPF reads StartupUri after
            // OnStartup returns, and it cannot be cleared once set.
            StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
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
            // "Hide desktop icons" only lasts while Racks runs: put the icons back on a normal exit
            // (tray Exit, update, Windows shutdown).
            try { Racks.Util.DesktopIcons.RestoreIfHiddenByRacks(); } catch { }

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
