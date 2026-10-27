using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Racks.Core.Abstractions;
using Racks.Infrastructure.Registry;
using Racks.Infrastructure.Shell;
using Racks.UseCases.AutoRoute;
using Racks.UseCases.Display;
using Racks.UseCases.DoubleClickHide;
using Racks.UseCases.HotCorner;
using Racks.UseCases.Hotkeys;
using Racks.UseCases.MagicOrganize;
using Racks.UseCases.Tray;
using Racks.UseCases.Updates;
using Racks.Util;
using Racks.Views;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using static Racks.Util.Interop;

namespace Racks
{
    /// <summary>
    /// The hidden host window: tray icon and its menu, hotkeys, the <c>WM_RACKS_*</c> IPC messages, and the owner of the
    /// services that do the work (see <c>Racks/UseCases</c>). It builds them by hand in the constructor, forwards events to
    /// them and shows the windows and dialogs; it holds no business rules of its own (P7).
    /// </summary>
    public partial class MainWindow : Window
    {
        public static InstanceController _controller = null!;
        public const int WM_RACKS_OPEN_SETTINGS = 0x0400 + 100;
        public const int WM_RACKS_OPEN_HELP = 0x0400 + 101;
        public const int WM_RACKS_TOGGLE_LOCK_ALL = 0x0400 + 102;
        public const int WM_RACKS_TOGGLE_ICE_PHYSICS = 0x0400 + 103;
        public const int WM_RACKS_TOGGLE_DESKTOP_ICONS = 0x0400 + 104;
        public const int WM_RACKS_TOGGLE_HOT_CORNER = 0x0400 + 105;
        public const int WM_RACKS_OPEN_ABOUT = 0x0400 + 106;
        public const int WM_RACKS_TOGGLE_DEV_DIAGNOSTICS = 0x0400 + 107;

        private uint _taskbarRestartMessage;
        private IntPtr _mainHwnd = IntPtr.Zero;
        // The switch in the tray menu is moved by code (startup, "sync") without applying the change to the racks.
        private bool _syncingLockAllToggle;
        private QuickFinderWindow? _quickFinder;
        private SingleInstanceWindow<Views.DevDiagnosticsOverlay> _devDiagnostics = null!;
        private SingleInstanceWindow<SettingsWindow> _settings = null!;
        private SingleInstanceWindow<HelpWindow> _help = null!;
        private Dictionary<int, Action> _ipcCommands = null!;

        private DesktopRouter _desktopRouter = null!;
        private AutoRouteService _autoRouteService = null!;
        private MagicOrganizeService _magicOrganize = null!;
        private HotCornerService _hotCorner = null!;
        private DoubleClickHideService _doubleClickHide = null!;
        private HotkeyController _hotkeys = null!;
        private TrayToggleService _toggles = null!;
        private UpdateCheckService _updates = null!;
        private DisplayChangeDebouncer _display = null!;

        /// <summary>"Double-click the empty desktop hides the racks" (set by the settings window).</summary>
        public bool DoubleClickToHide
        {
            get => _doubleClickHide.Enabled;
            set => _doubleClickHide.Enabled = value;
        }

        public MainWindow()
        {
            Application.Current.MainWindow = this;
            InitializeComponent();
            // EnsureHandle raises SourceInitialized (OnSourceInitialized) right here, inside the constructor: the hotkeys
            // and the message table must exist by then. They only read _controller when they run, never when built.
            ComposeHostServices();
            new WindowInteropHelper(this).EnsureHandle();

            // App.OnStartup shows StartupAnimationWindow before WPF's own StartupUri
            // logic constructs and shows this window, so WPF's "first window shown
            // becomes Application.MainWindow" default would otherwise leave MainWindow
            // pointing at the animation window - which closes itself a couple seconds
            // later. Wpf.Ui.Tray's TrayManager resolves the tray icon's owner HWND via
            // Application.Current.MainWindow, and RackWindow's drag/snap logic does the
            // same via an "as MainWindow" cast, so both silently broke once that window
            // closed / never matched. Claim the slot explicitly so it always points here.
            Application.Current.MainWindow = this;

            // Tray-menu header shows just "Racks" in a clean font. The version is still
            // available in the About window and as the ToolTip on this header for nerds.
            try { versionHeader.ToolTip = $"Racks {Process.GetCurrentProcess().MainModule?.FileVersionInfo.FileVersion}"; }
            catch { versionHeader.ToolTip = "Racks"; }
            _controller = new InstanceController();
            _controller.InitInstances(); // must not pump messages: rack services are null until ComposeRackServices

            ComposeRackServices();
            StartAutoRouting();

            var settings = AppSettings.Default;
            AutorunToggle.IsChecked = settings.StartOnLogin;
            DoubleClickToHide = settings.DoubleClickToHide;
            // Ice-rink physics default on; only off if the user turned it off before.
            RackPhysics.Enabled = settings.IcePhysics;
            // These IsChecked assignments fire the switches' handlers, so each one runs once at startup and re-writes
            // the value it just read (today's behavior, characterized in TrayToggleServiceTests; do not "fix").
            IcePhysicsToggle.IsChecked = RackPhysics.Enabled;
            // Only touch the toggle when a value was saved (a fresh install has none).
            if (settings.HideDesktopIcons is bool hide)
            {
                HideDesktopIconsToggle.IsChecked = hide;
                if (hide) ShellOperationGateway.Default.SetDesktopIconsHidden(true);
            }
            // A previous version (or one that was killed) may have left the whole desktop hidden.
            ShellOperationGateway.Default.EnsureDesktopVisible();
            if (settings.HotCornerHide)
            {
                HotCornerHideToggle.IsChecked = true;
                _hotCorner.Start();
            }
            SyncLockAllToggle();
            // Opt-in: check GitHub for a newer release a little after startup (Settings > Auto update).
            if (settings.AutoUpdate)
            {
                _ = _updates.CheckInBackgroundAsync();
            }
            // Download folders of earlier updates (the installer ran from there, so they could not be deleted at the time).
            _ = _updates.ScheduleCleanup();
        }

        // Composition by hand, no container. Two steps because OnSourceInitialized runs during EnsureHandle, before the
        // controller exists: this one needs nothing but the controller's static name, read when a service runs.
        private void ComposeHostServices()
        {
            _help = new SingleInstanceWindow<HelpWindow>("HelpWindow", () => new HelpWindow());
            _settings = new SingleInstanceWindow<SettingsWindow>("SettingsWindow", () => new SettingsWindow(_controller, this));
            _devDiagnostics = new SingleInstanceWindow<Views.DevDiagnosticsOverlay>("DevDiagnostics", () => new Views.DevDiagnosticsOverlay(_controller));
            _hotkeys = new HotkeyController(new Win32HotkeyApi(), new HotkeyActions(OpenQuickFinder, () => _controller.AddVirtualInstance(), _devDiagnostics.Toggle));
            _updates = new UpdateCheckService();
            _display = new DisplayChangeDebouncer(
                () => _controller.isInitializingInstances,
                action => Application.Current.Dispatcher.InvokeAsync(action),
                () => _controller.CheckFrameWindowsLive());
            // Commands other processes send to the hidden host (tools/*E2ERunner.ps1); the numbers are part of the contract.
            _ipcCommands = new Dictionary<int, Action>
            {
                [WM_RACKS_OPEN_SETTINGS] = _settings.ShowOrActivate,
                [WM_RACKS_OPEN_HELP] = _help.ShowOrActivate,
                [WM_RACKS_TOGGLE_LOCK_ALL] = () => FlipAndRun(LockAllToggle, LockAllToggle_CheckChanged),
                [WM_RACKS_TOGGLE_ICE_PHYSICS] = () => FlipAndRun(IcePhysicsToggle, IcePhysicsToggle_CheckChanged),
                [WM_RACKS_TOGGLE_DESKTOP_ICONS] = () => FlipAndRun(HideDesktopIconsToggle, HideDesktopIconsToggle_CheckChanged),
                [WM_RACKS_TOGGLE_HOT_CORNER] = () => FlipAndRun(HotCornerHideToggle, HotCornerHideToggle_CheckChanged),
                [WM_RACKS_OPEN_ABOUT] = () => { try { new AboutWindow(150, 150, 320, 280).Show(); } catch { } },
                [WM_RACKS_TOGGLE_DEV_DIAGNOSTICS] = _devDiagnostics.Toggle,
            };
        }

        // The services that hold the controller itself.
        private void ComposeRackServices()
        {
            var racks = new ControllerRackWindows(_controller);
            _hotCorner = new HotCornerService(new DispatcherRepeatingScheduler(), new ScreenHotCornerProbe(), racks.HideAll, racks.ShowAll);
            _doubleClickHide = new DoubleClickHideService(
                () => new GlobalMouseHook(),
                new DesktopHitTest(),
                () => Application.Current.Dispatcher.Invoke(() => _controller.ChangeVisibility()));
            _toggles = new TrayToggleService(
                AppSettings.Default,
                ShellOperationGateway.Default,
                _hotCorner,
                racks,
                on => RackPhysics.Enabled = on,
                () => Process.GetCurrentProcess().MainModule!.FileName);
            _magicOrganize = new MagicOrganizeService(new MagicOrganizeHost(_controller));
        }

        // Auto-routing: any file landing on the user's Desktop that matches a rack's AutoRouteRegex is
        // moved into that rack. The decisions, the move and the claims live in AutoRouteService; this
        // only hands it the racks, a way to save one, and the workspace folder.
        private void StartAutoRouting()
        {
            try
            {
                _autoRouteService = new AutoRouteService(
                    new AutoRouteHost(
                        () => _controller.Instances.ToArray(),
                        rack => _controller.WriteInstanceToKey(rack),
                        () => ShellOperationGateway.Default.WorkspacePath),
                    new DispatcherDelayScheduler());
                _desktopRouter = new DesktopRouter(_autoRouteService);
            }
            catch (System.Exception ex) { Debug.WriteLine($"DesktopRouter init failed: {ex.Message}"); }
        }

        // ---- tray menu --------------------------------------------------------------------------

        private void addDesktopFrame_Click(object sender, RoutedEventArgs e)
        {
            // New default: virtual rack. User can drop a shortcut/file/folder in and
            // a .lnk is created in the sandbox - the source is never touched.
            _controller.AddVirtualInstance();
        }

        private void addFolderFrame_Click(object sender, RoutedEventArgs e) => _controller.AddInstance();

        private async void magicOrganizeDesktop_Click(object sender, RoutedEventArgs e) => await _magicOrganize.RunAsync();

        // Layout export/import: dialogs, messages and audit are LayoutDialogs / LayoutService (P9); every failure is shown.
        private void ExportLayout_Click(object sender, RoutedEventArgs e) => LayoutDialogs.Export();

        private void ImportLayout_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutDialogs.Import(replaceExisting: true) > 0) _controller.CheckFrameWindowsLive(true);
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await _updates.CheckNowAsync();

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e) => _help.ShowOrActivate();

        private void Settings_Button_Click(object sender, RoutedEventArgs e) => _settings.ShowOrActivate();

        private void DevDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e) => _devDiagnostics.Toggle();

        private void OpenQuickFinder()
        {
            _quickFinder?.Close();
            _quickFinder = new QuickFinderWindow(_controller);
            _quickFinder.Show();
            _quickFinder.Activate();
        }

        private void visitGithub_Buton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo sInfo = new ProcessStartInfo($"https://github.com/duartelcunha/Racks") { UseShellExecute = true };
                _ = Process.Start(sInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"visitGithub failed: {ex.Message}");
            }
        }

        private void ExitApp(object sender, RoutedEventArgs e)
        {
            TrayIcon.Visibility = Visibility.Hidden;
            LifecycleAnimations.RunQuitAnimation(() => Application.Current.Shutdown());
        }

        // ---- tray switches (the effect lives in TrayToggleService) -------------------------------

        // The switch reflects reality: on only when there is at least one rack and every rack is
        // locked. Called at startup and each time the tray menu opens (racks can be locked one by
        // one from their own menu), without re-applying the change to the racks.
        private void SyncLockAllToggle()
        {
            _syncingLockAllToggle = true;
            try { LockAllToggle.IsChecked = _toggles.AllRacksLocked(); }
            finally { _syncingLockAllToggle = false; }
        }

        private void TrayMenu_Opened(object sender, RoutedEventArgs e) => SyncLockAllToggle();

        private void LockAllToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingLockAllToggle) return;
            _toggles.SetLockAll(LockAllToggle.IsChecked == true);
        }

        private void IcePhysicsToggle_CheckChanged(object sender, RoutedEventArgs e) => _toggles.SetIcePhysics(IcePhysicsToggle.IsChecked == true);

        private void HotCornerHideToggle_CheckChanged(object sender, RoutedEventArgs e) => _toggles.SetHotCorner(HotCornerHideToggle.IsChecked == true);

        private void HideDesktopIconsToggle_CheckChanged(object sender, RoutedEventArgs e) => _toggles.SetHideDesktopIcons(HideDesktopIconsToggle.IsChecked == true);

        private void AutorunToggle_CheckChanged(object sender, RoutedEventArgs e) => _toggles.SetStartOnLogin((bool)AutorunToggle.IsChecked!);

        // ---- window lifetime ---------------------------------------------------------------------

        private void Window_Initialized(object sender, EventArgs e)
        {
            this.ShowInTaskbar = false;
            this.Width = 0;
            this.Height = 0;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStyle = WindowStyle.None;
            this.Visibility = Visibility.Collapsed;
            this.Left = -500;
            this.Top = -500;

            CloseHide();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            _mainHwnd = hwnd;
            this.Title = "RacksMainHost";
            AppSettings.Default.TryPublishMainHwnd((long)hwnd);
            int exStyle = (int)GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, (IntPtr)exStyle);

            // Tray/hotkey setup lives here rather than in Window_Loaded: Window_Initialized
            // schedules a background CloseHide() that Dispatcher.Invoke()s a Close() at Send
            // priority ~100ms after construction. Loaded fires at the much lower Loaded
            // priority, so on any startup that takes >=100ms to reach the dispatcher's message
            // loop (routine, given OnStartup does hook/registry/IO work first), that Close/Hide
            // wins the race and Loaded never gets a turn - the tray icon silently never
            // registers. OnSourceInitialized runs synchronously inside Show(), before that race
            // can start, so it always gets a turn.
            _taskbarRestartMessage = RegisterWindowMessage("TaskbarCreated");
            _mainHwnd = hwnd;
            var hwndSource = HwndSource.FromHwnd(_mainHwnd);
            hwndSource?.AddHook(WndProc);
            RegisterTrayIcon();
            // Re-register once more after the startup animation / hide sequence settles,
            // in case the first attempt lost a race with Explorer or the splash window.
            var trayRetry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            trayRetry.Tick += (_, _) => { trayRetry.Stop(); RegisterTrayIcon(); };
            trayRetry.Start();

            // Ctrl+Shift+Space opens the cross-rack quick finder; Ctrl+Shift+N spawns a new virtual rack;
            // Ctrl+Shift+D toggles the dev diagnostics. A hotkey another app already owns is reported with a toast.
            _hotkeys.RegisterAll(_mainHwnd, HotkeyConflictToast.Show);
            // First-launch welcome animation: a Racks icon drops from the
            // center of the screen into the system tray and a toast follows
            // pointing the user at where the app now lives. Gated by a
            // registry marker so it runs at most once per machine.
            try { FirstRunWelcome.ShowIfFirstRun(MigrationMarkers.Default); }
            catch (Exception ex) { Debug.WriteLine($"FirstRunWelcome failed: {ex.Message}"); }
        }

        private void RegisterTrayIcon() => TrayIconRegistrar.Register(TrayIcon);

        private void CloseHide()
        {
            Task.Run(() =>
            {
                Thread.Sleep(100);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.Close();
                });
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _doubleClickHide?.Dispose(); // null-safe: the ctor may have thrown before ComposeRackServices
            _desktopRouter?.Dispose();
            _autoRouteService?.Dispose();
            _hotCorner?.Dispose();
            _hotkeys.UnregisterAll();
            base.OnClosed(e);
        }

        // ---- window messages ---------------------------------------------------------------------

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarRestartMessage)
            {
                // always recreate on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RegisterTrayIcon();
                    if (!_controller.isInitializingInstances)
                        _controller.CheckFrameWindowsLive();
                });
            }
            // WM_DISPLAYCHANGE and the follow-up message: one debounced reposition pass (DisplayChangeDebouncer).
            _display.OnMessage(msg);
            if (_hotkeys.TryDispatch(msg, wParam))
            {
                handled = true;
            }
            if (_ipcCommands.TryGetValue(msg, out var command))
            {
                command();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // The WM_RACKS_TOGGLE_* messages set IsChecked (which fires Checked/Unchecked, so the handler runs) AND then call
        // the handler by hand, so the handler runs twice for one message. Every handler is idempotent, and the tools/*E2ERunner
        // scripts were written against this: kept exactly (characterized in TrayToggleServiceTests).
        private void FlipAndRun(ToggleSwitch toggle, RoutedEventHandler handler)
        {
            toggle.IsChecked = !(toggle.IsChecked == true);
            handler(this, new RoutedEventArgs());
        }
    }
}
