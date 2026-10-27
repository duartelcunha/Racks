using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using System.Windows;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Util;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using static Racks.Util.Interop;
using System.Windows.Interop;
using H.Hooks;
using MouseEventArgs = H.Hooks.MouseEventArgs;
using System.Windows.Automation;
namespace Racks
{
    public partial class MainWindow : Window
    {
        bool startOnLogin;
        bool reseted = false;
        private bool _dummyWindowPending = false;
        private uint _taskbarRestartMessage;
        public static InstanceController _controller = null!;
        public static MainWindow _mainWindow = null!;
        private LowLevelMouseHook? _lowLevelMouseHook;
        private QuickFinderWindow? _quickFinder;
        private DesktopRouter _desktopRouter = null!;
        private Racks.UseCases.AutoRoute.AutoRouteService _autoRouteService = null!;
        private System.Windows.Threading.DispatcherTimer? _hotCornerTimer;
        private bool _hotCornerHidden; // tracks whether racks are currently hidden by hot-corner
        private const int QUICK_FINDER_HOTKEY_ID = 0xB0CC;
        private const int NEW_RACK_HOTKEY_ID = 0xB0CD;
        private const int DEV_DIAGNOSTICS_HOTKEY_ID = 0xB0CE;
        public const int WM_RACKS_OPEN_SETTINGS = 0x0400 + 100;
        public const int WM_RACKS_OPEN_HELP = 0x0400 + 101;
        public const int WM_RACKS_TOGGLE_LOCK_ALL = 0x0400 + 102;
        public const int WM_RACKS_TOGGLE_ICE_PHYSICS = 0x0400 + 103;
        public const int WM_RACKS_TOGGLE_DESKTOP_ICONS = 0x0400 + 104;
        public const int WM_RACKS_TOGGLE_HOT_CORNER = 0x0400 + 105;
        public const int WM_RACKS_OPEN_ABOUT = 0x0400 + 106;
        public const int WM_RACKS_TOGGLE_DEV_DIAGNOSTICS = 0x0400 + 107;
        private Views.DevDiagnosticsOverlay? _devDiagnostics;
        private SettingsWindow? _settingsWindow;
        private HelpWindow? _helpWindow;
        private IntPtr _mainHwnd = IntPtr.Zero;
        private static bool _doubleClickToHide;
        private DateTime _lastDoubleClickTime = DateTime.MinValue;

        public bool DoubleClickToHide
        {
            get => _doubleClickToHide;
            set
            {
                if (_doubleClickToHide != value)
                {
                    _doubleClickToHide = value;
                    OnDoubleToClickHideChanged();
                }
            }
        }
        private void OnDoubleToClickHideChanged()
        {
            if (_doubleClickToHide)
            {
                _lowLevelMouseHook?.Dispose();
                _lowLevelMouseHook = new LowLevelMouseHook { AddKeyboardKeys = true };
                _lowLevelMouseHook.DoubleClick += HandleGlobalDoubleClick;
                _lowLevelMouseHook.Start();
            }
            else
            {
                _lowLevelMouseHook?.Stop();
            }
        }

        public MainWindow()
        {
            Application.Current.MainWindow = this;
            _mainWindow = this;
            InitializeComponent();
            new WindowInteropHelper(this).EnsureHandle();
            _mainWindow = this;

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
            _controller.InitInstances();

            // Auto-routing: any file landing on the user's Desktop that matches a rack's AutoRouteRegex is
            // moved into that rack. The decisions, the move and the claims live in AutoRouteService; this
            // only hands it the racks, a way to save one, and the workspace folder.
            try
            {
                _autoRouteService = new Racks.UseCases.AutoRoute.AutoRouteService(
                    new Racks.UseCases.AutoRoute.AutoRouteHost(
                        () => _controller.Instances.ToArray(),
                        rack => _controller.WriteInstanceToKey(rack),
                        () => Racks.Infrastructure.Shell.ShellOperationGateway.Default.WorkspacePath),
                    new Racks.Core.Abstractions.DispatcherDelayScheduler());
                _desktopRouter = new DesktopRouter(_autoRouteService);
            }
            catch (System.Exception ex) { Debug.WriteLine($"DesktopRouter init failed: {ex.Message}"); }
            var settings = AppSettings.Default;
            startOnLogin = settings.StartOnLogin;
            AutorunToggle.IsChecked = startOnLogin;
            DoubleClickToHide = settings.DoubleClickToHide;
            // Ice-rink physics default on; only off if the user turned it off before.
            Util.RackPhysics.Enabled = settings.IcePhysics;
            IcePhysicsToggle.IsChecked = Util.RackPhysics.Enabled;
            // Only touch the toggle when a value was saved (a fresh install has none).
            if (settings.HideDesktopIcons is bool hide)
            {
                HideDesktopIconsToggle.IsChecked = hide;
                if (hide) Racks.Infrastructure.Shell.ShellOperationGateway.Default.SetDesktopIconsHidden(true);
            }
            // A previous version (or one that was killed) may have left the whole desktop hidden.
            Racks.Infrastructure.Shell.ShellOperationGateway.Default.EnsureDesktopVisible();
            if (settings.HotCornerHide)
            {
                HotCornerHideToggle.IsChecked = true;
                StartHotCornerWatch();
            }
            SyncLockAllToggle();
            // Opt-in: check GitHub for a newer release a little after startup (Settings > Auto update).
            if (settings.AutoUpdate)
            {
                _ = CheckForUpdatesInBackgroundAsync();
            }
            // Download folders of earlier updates (the installer ran from there, so they could not
            // be deleted at the time). A minute in, so a just-finished installer has exited.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(1));
                Updater.CleanupOldUpdateDirs();
            });
        }
        private void HandleGlobalDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Keys.ToString() != "MouseLeft") return;
            if ((DateTime.Now - _lastDoubleClickTime).TotalSeconds < 0.3) return;
            _lastDoubleClickTime = DateTime.Now;

            POINT pt = new POINT { X = e.Position.X, Y = e.Position.Y };
            IntPtr hwndUnderCursor = WindowFromPoint(pt);
            IntPtr desktopListView = Racks.Infrastructure.Shell.ShellOperationGateway.Default.FindDesktopIconList();

            if (hwndUnderCursor == desktopListView && !IsDesktopIconHit(pt))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _controller.ChangeVisibility();
                });
            }
        }

        private static bool IsDesktopIconHit(POINT screenPt)
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(screenPt.X, screenPt.Y));
            if (element.Current.ControlType == ControlType.ListItem)
            {
                return true;
            }
            return false;
        }
        private void addDesktopFrame_Click(object sender, RoutedEventArgs e)
        {
            // New default: virtual rack. User can drop a shortcut/file/folder in and
            // a .lnk is created in the sandbox — the source is never touched.
            _controller.AddVirtualInstance();
        }

        private void addFolderFrame_Click(object sender, RoutedEventArgs e)
        {
            _controller.AddInstance();
        }

        private async void magicOrganizeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var clusters = Racks.Core.AutoOrganizer.AnalyzeDesktop(desktopPath);

                if (clusters.Count == 0)
                {
                    Racks.Views.RacksMessageBox.Show("Your desktop is already empty or too clean to organize!", "Magic Organizer");
                    return;
                }

                var dialog = new Racks.Views.AutoOrganizePreviewDialog(clusters);
                if (dialog.ShowDialog() == true && dialog.Result != Racks.Views.OrganizeChoice.Cancel)
                {
                    if (dialog.Result == Racks.Views.OrganizeChoice.Racks)
                    {
                        var undo = Racks.Core.MagicOrganizeUndo.Begin(Racks.Core.MagicOrganizeUndo.Mode.Racks);
                        // User approved, create racks and move files!
                        var workingArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                        // Screen sizes are physical pixels; PosX/PosY/Width/Height are WPF units.
                        double dpiScale = System.Drawing.Graphics.FromHwnd(IntPtr.Zero).DpiX / 96f;
                        var slots = Racks.Core.MagicOrganizeLayout.Grid(clusters.Count,
                            workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height, dpiScale);
                        double rackWidth = Racks.Core.MagicOrganizeLayout.RackWidth;
                        double rackHeight = Racks.Core.MagicOrganizeLayout.RackHeight;

                        int movedFilesCount = 0;

                        for (int i = 0; i < clusters.Count; i++)
                        {
                            var cluster = clusters[i];
                            if (cluster.FilePaths.Count == 0) continue;

                            string safeName = Racks.Infrastructure.FileSystem.PathPolicy.SanitizeLeafName(cluster.Name, "Cluster_" + Guid.NewGuid().ToString().Substring(0, 4));

                            // The name is the registry key: a second run must not reuse (and overwrite) the first run's racks.
                            safeName = Racks.Rack.RackNames.Unique(safeName, _controller.Instances.Select(r => r.Name));
                            var inst = new Instance(safeName, false);
                            inst.TitleText = safeName;
                            inst.Folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            inst.IsDesktopFilterRack = true;
                            inst.PosX = slots[i].X;
                            inst.PosY = slots[i].Y;
                            inst.Width = rackWidth;
                            inst.Height = rackHeight;

                            inst.AssignedFiles = new List<string>();

                            _controller.Instances.Add(inst);

                            // Ensure workspace exists before moving
                            Racks.Infrastructure.Shell.ShellOperationGateway.Default.InitializeWorkspace();

                            // 2. Move files to RacksWorkspace and assign them
                            foreach (var fp in cluster.FilePaths)
                            {
                                string fileName = System.IO.Path.GetFileName(fp);
                                string destPath = System.IO.Path.Combine(Racks.Infrastructure.Shell.ShellOperationGateway.Default.WorkspacePath, fileName);

                                var moveResult = Racks.Infrastructure.FileSystem.FileOperationBroker.Default.Move(fp, destPath, "magic organize: into rack");
                                if (moveResult.Completed)
                                {
                                    Racks.Infrastructure.Shell.ShellOperationGateway.Default.NotifyItemMoved(fp, destPath, System.IO.Directory.Exists(fp));
                                    // One rack owns the name: take it from any other rack that still claims it.
                                    foreach (var lost in Racks.Rack.DesktopRackClaims.Claim(_controller.Instances, inst, fileName))
                                        _controller.WriteInstanceToKey(lost);
                                    undo.RecordMove(fp, destPath); // fp = original desktop path
                                    movedFilesCount++;
                                }
                            }
                            undo.CreatedRackNames.Add(inst.Name);

                            try
                            {
                                _controller.WriteInstanceToKey(inst);
                            }
                            catch (Exception ex)
                            {
                                Racks.Views.RacksMessageBox.Show($"Failed to save Rack '{safeName}' to registry: {ex.Message}", "Error");
                            }

                            var subWindow = new RackWindow(inst);
                            subWindow.ChangeBackgroundOpacity(inst.Opacity);
                            _controller._subWindows.Add(subWindow);

                            await System.Threading.Tasks.Task.Delay(100); // Staggered entry animation

                            subWindow.Show();
                            subWindow.Topmost = true; // Ensure it is visible to the user immediately
                            _controller._subWindowsPtr.Add(new System.Windows.Interop.WindowInteropHelper(subWindow).Handle);
                        }

                        bool undoRacks = Racks.Views.RacksMessageBox.Confirm(
                            $"Moved {movedFilesCount} item(s) into {clusters.Count} rack(s).\n\nKeep this, or undo and send everything back to your desktop?",
                            "Magic Organize complete", "Keep", "Undo");
                        if (!undoRacks) UndoMagicOrganize();
                    }
                    else if (dialog.Result == Racks.Views.OrganizeChoice.Folders)
                    {
                        var undo = Racks.Core.MagicOrganizeUndo.Begin(Racks.Core.MagicOrganizeUndo.Mode.Folders);
                        // Just move them to physical folders on the Desktop
                        int movedFilesCount = 0;
                        foreach (var cluster in clusters)
                        {
                            if (cluster.FilePaths.Count == 0) continue;

                            // The cluster name comes from file names: make it a plain folder name before it
                            // reaches Path.Combine (a name with a slash or ".." would create folders elsewhere).
                            string folderName = Racks.Infrastructure.FileSystem.PathPolicy.SanitizeLeafName(cluster.Name, "Cluster_" + Guid.NewGuid().ToString().Substring(0, 4));
                            string folderPath = System.IO.Path.Combine(desktopPath, folderName);
                            if (!System.IO.Directory.Exists(folderPath))
                            {
                                var created = Racks.Infrastructure.FileSystem.FileOperationBroker.Default.CreateDirectory(folderPath, "magic organize: new folder");
                                if (!created.Succeeded)
                                {
                                    Debug.WriteLine($"Magic Organize folder {created.Status}: {created.Reason}");
                                    continue;
                                }
                                undo.CreatedFolders.Add(folderPath);
                            }

                            foreach (var fp in cluster.FilePaths)
                            {
                                string fileName = System.IO.Path.GetFileName(fp);
                                string destPath = System.IO.Path.Combine(folderPath, fileName);

                                var moveResult = Racks.Infrastructure.FileSystem.FileOperationBroker.Default.Move(fp, destPath, "magic organize: into folder");
                                if (moveResult.Completed)
                                {
                                    Racks.Infrastructure.Shell.ShellOperationGateway.Default.NotifyItemMoved(fp, destPath, System.IO.Directory.Exists(fp));
                                    undo.RecordMove(fp, destPath);
                                    movedFilesCount++;
                                }
                            }
                        }
                        bool undoFolders = Racks.Views.RacksMessageBox.Confirm(
                            $"Moved {movedFilesCount} item(s) into {clusters.Count} folder(s) on your desktop.\n\nKeep this, or undo and send everything back?",
                            "Magic Organize complete", "Keep", "Undo");
                        if (!undoFolders) UndoMagicOrganize();
                    }
                }
            }
            catch (Exception ex)
            {
                Racks.Views.RacksMessageBox.Show($"An error occurred during Magic Organize:\n{ex.Message}\n{ex.StackTrace}", "Fatal Error");
            }
        }

        // Undo the last Magic Organize: tear down the racks/folders it created and move every
        // file back to its original desktop spot, then tidy the desktop into a grid.
        private void UndoMagicOrganize()
        {
            var undo = Racks.Core.MagicOrganizeUndo.Last;
            if (undo == null || !undo.HasAnything) return;
            try
            {
                if (undo.OrganizeMode == Racks.Core.MagicOrganizeUndo.Mode.Racks)
                {
                    // Close and delete the racks this run created (before moving files back, so
                    // their file-watchers/claims are gone).
                    foreach (var name in undo.CreatedRackNames)
                    {
                        var inst = _controller.Instances.FirstOrDefault(i => i.Name == name);
                        if (inst == null) continue;
                        inst.isWindowClosing = true;
                        var win = _controller._subWindows.FirstOrDefault(w => w.Instance == inst);
                        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(inst.GetKeyLocation(), false); } catch { }
                        if (win != null) { try { win.Close(); } catch { } _controller.RemoveInstance(inst, win); }
                        else _controller.Instances.Remove(inst);
                    }
                }

                int restored = undo.RestoreFiles();
                undo.RemoveCreatedFolders();

                // Lay the returned files out in a clean grid on the desktop.
                var back = new List<string>();
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                foreach (var m in undo.Moved)
                    if (System.IO.File.Exists(m.OriginalPath) || System.IO.Directory.Exists(m.OriginalPath))
                        back.Add(m.OriginalPath);
                if (back.Count > 0) Racks.Infrastructure.Shell.ShellOperationGateway.Default.ArrangeDesktopIcons(back);

                Racks.Core.MagicOrganizeUndo.Clear();
                Racks.Views.RacksMessageBox.Show($"Undone. {restored} item(s) are back on your desktop.", "Magic Organize");
            }
            catch (Exception ex)
            {
                Racks.Views.RacksMessageBox.Show($"Couldn't fully undo:\n{ex.Message}", "Magic Organize");
            }
        }

        private void ExportLayout_Click(object sender, RoutedEventArgs e)
        {
            try { Racks.Util.RackLayoutIO.PromptExport(); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}"); }
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_helpWindow == null || !_helpWindow.IsLoaded)
                {
                    _helpWindow = new HelpWindow();
                    _helpWindow.Closed += (_, _) => _helpWindow = null;
                    _helpWindow.Show();
                }
                else
                {
                    _helpWindow.Activate();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"HelpWindow open failed: {ex.Message}"); }
        }

        private void DevDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ToggleDevDiagnostics();
        }

        private void ToggleDevDiagnostics()
        {
            try
            {
                if (_devDiagnostics == null || !_devDiagnostics.IsLoaded)
                {
                    _devDiagnostics = new Views.DevDiagnosticsOverlay(_controller);
                    _devDiagnostics.Closed += (_, _) => _devDiagnostics = null;
                    _devDiagnostics.Show();
                }
                else
                {
                    _devDiagnostics.Close();
                    _devDiagnostics = null;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"DevDiagnostics toggle failed: {ex.Message}"); }
        }

        private async void ImportLayout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Racks.Views.RacksMessageBox.Confirm(
                        "Importing will REPLACE all your current racks with the layout in the file. Continue?",
                        "Import racks", "Replace", "Cancel")) return;
                int count = Racks.Util.RackLayoutIO.PromptImport(replaceExisting: true);
                if (count > 0) _controller.CheckFrameWindowsLive(true);
            }
            catch (System.IO.InvalidDataException ex)
            {
                // Bad/hostile layout file: existing racks were left intact (or rolled back).
                Racks.Views.RacksMessageBox.Show($"Import cancelled: {ex.Message}\n\nYour existing racks are unchanged.", "Import racks");
            }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}"); }
        }
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

            // Ctrl+Shift+Space opens the cross-rack quick finder; Ctrl+Shift+N spawns a new virtual
            // rack. RegisterHotKey returns false (it does not throw) when another app already owns
            // the combination, so check the result and tell the user instead of failing silently.
            bool quickFinderOk = false, newRackOk = false;
            try { quickFinderOk = Interop.RegisterHotKey(_mainHwnd, QUICK_FINDER_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x20); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey QuickFinder failed: {ex.Message}"); }
            try { newRackOk = Interop.RegisterHotKey(_mainHwnd, NEW_RACK_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x4E /* VK_N */); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey NewRack failed: {ex.Message}"); }
            try { Interop.RegisterHotKey(_mainHwnd, DEV_DIAGNOSTICS_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x44 /* VK_D */); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey DevDiagnostics failed: {ex.Message}"); }
            if (!quickFinderOk || !newRackOk)
            {
                try
                {
                    var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                        .AddText(Racks.Properties.Lang.Hotkey_Conflict_Title);
                    if (!quickFinderOk) toast.AddText(Racks.Properties.Lang.Hotkey_Conflict_QuickFinder);
                    if (!newRackOk) toast.AddText(Racks.Properties.Lang.Hotkey_Conflict_NewRack);
                    toast.Show();
                }
                catch (Exception ex) { Debug.WriteLine($"Hotkey conflict toast failed: {ex.Message}"); }
            }
            // First-launch welcome animation: a Racks icon drops from the
            // center of the screen into the system tray and a toast follows
            // pointing the user at where the app now lives. Gated by a
            // registry marker so it runs at most once per machine.
            try { Racks.Util.FirstRunWelcome.ShowIfFirstRun(MigrationMarkers.Default); }
            catch (Exception ex) { Debug.WriteLine($"FirstRunWelcome failed: {ex.Message}"); }
        }

        // The tray icon is the only way back into a hidden app, so registration is
        // guarded (a throw here used to skip the rest of OnSourceInitialized too) and
        // always forces the icon visible. Register() is idempotent, so the startup
        // retry and the TaskbarCreated handler can call this freely.
        private void RegisterTrayIcon()
        {
            try
            {
                try
                {
                    int iconSize = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
                    string resName = iconSize switch
                    {
                        <= 16 => "tray_16.png",
                        <= 20 => "tray_20.png",
                        <= 24 => "tray_24.png",
                        _ => "tray_32.png"
                    };
                    TrayIcon.Icon = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri($"/Icon/{resName}", UriKind.Relative));
                }
                catch { /* fallback to XAML default */ }

                TrayIcon.Register();
                TrayIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Debug.WriteLine($"TrayIcon register failed: {ex.Message}"); }
        }
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

        //private void BlurToggle_CheckChanged(object sender, System.Windows.RoutedEventArgs e)
        //{
        //    _controller.reg.WriteToRegistryRoot("blurBackground", BlurToggle.IsChecked!);
        //    _controller.ChangeBlur((bool)BlurToggle.IsChecked!);
        //}
        // Flip IsLocked on every rack at once. Persists per-rack AND tells each running
        // rack window to apply the new chrome — without ApplyLockedState the WindowChrome
        // wouldn't update at runtime and the lock would only "stick" after a relaunch.
        private bool _syncingLockAllToggle;

        // The switch reflects reality: on only when there is at least one rack and every rack is
        // locked. Called at startup and each time the tray menu opens (racks can be locked one by
        // one from their own menu), without re-applying the change to the racks.
        private void SyncLockAllToggle()
        {
            _syncingLockAllToggle = true;
            try { LockAllToggle.IsChecked = _controller.Instances.Count > 0 && _controller.Instances.All(i => i.IsLocked); }
            finally { _syncingLockAllToggle = false; }
        }

        private void TrayMenu_Opened(object sender, RoutedEventArgs e) => SyncLockAllToggle();

        private void LockAllToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingLockAllToggle) return;
            bool locked = LockAllToggle.IsChecked == true;
            foreach (var inst in _controller.Instances)
            {
                inst.IsLocked = locked;
            }
            foreach (var w in _controller._subWindows)
            {
                try { w.ApplyLockedState(); }
                catch (Exception ex) { Debug.WriteLine($"ApplyLockedState failed: {ex.Message}"); }
            }
        }

        // Global on/off for the ice-rink physics. Persists and applies live (no restart):
        // off means racks no longer push, glide, or bounce when dragged together.
        private void IcePhysicsToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            bool on = IcePhysicsToggle.IsChecked == true;
            AppSettings.Default.TrySetIcePhysics(on);
            Util.RackPhysics.Enabled = on;
        }

        // Manual "Check for updates": queries the GitHub releases API and shows a toast
        // (either "new release" with an Install button, or "up to date"). Same code path the
        // auto-update toast uses; Install is handled in App.ToastActivatedHandler.
        // Silent: shows a toast only when a newer release exists (Updater stays quiet on a first
        // failure such as being offline).
        private static async System.Threading.Tasks.Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(20));
                await Updater.CheckUpdateAsync(Updater.ReleasesApi, showToastIfNoUpdate: false);
            }
            catch (Exception ex) { Debug.WriteLine($"Background update check failed: {ex.Message}"); }
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try { await Updater.CheckUpdateAsync(Updater.ReleasesApi, showToastIfNoUpdate: true); }
            catch (Exception ex) { Debug.WriteLine($"Manual update check failed: {ex.Message}"); }
        }

        // Hot-corner peek: poll cursor position every 100ms. When it enters a small
        // square at the top-right of the primary monitor, hide all racks; when it
        // leaves, show them again. State machine prevents flicker / repeated Hide.
        private void HotCornerHideToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            bool on = HotCornerHideToggle.IsChecked == true;
            AppSettings.Default.TrySetHotCornerHide(on);
            if (on) StartHotCornerWatch(); else StopHotCornerWatch();
        }

        private void StartHotCornerWatch()
        {
            if (_hotCornerTimer != null) return;
            _hotCornerTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _hotCornerTimer.Tick += (_, _) =>
            {
                try
                {
                    var pt = System.Windows.Forms.Cursor.Position;
                    var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                    // 16x16 pixel hot zone at the top-right corner.
                    bool inCorner = pt.X >= primary.Right - 16 && pt.X <= primary.Right
                                  && pt.Y >= primary.Top && pt.Y <= primary.Top + 16;
                    if (inCorner && !_hotCornerHidden)
                    {
                        _hotCornerHidden = true;
                        foreach (var w in _controller._subWindows) w.Hide();
                    }
                    else if (!inCorner && _hotCornerHidden)
                    {
                        // 64-pixel "leave" margin to avoid flicker on jitter.
                        bool nearCorner = pt.X >= primary.Right - 64 && pt.Y <= primary.Top + 64;
                        if (!nearCorner)
                        {
                            _hotCornerHidden = false;
                            foreach (var w in _controller._subWindows) w.Show();
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"HotCorner tick failed: {ex.Message}"); }
            };
            _hotCornerTimer.Start();
        }

        private void StopHotCornerWatch()
        {
            _hotCornerTimer?.Stop();
            _hotCornerTimer = null;
            if (_hotCornerHidden)
            {
                _hotCornerHidden = false;
                foreach (var w in _controller._subWindows) w.Show();
            }
        }

        // Toggle the visibility of the Windows desktop icons (SHELLDLL_DefView under
        // Progman/WorkerW). Used to give the user a clean desktop with only racks.
        private void HideDesktopIconsToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            bool hide = HideDesktopIconsToggle.IsChecked == true;
            AppSettings.Default.TrySetHideDesktopIcons(hide);
            Racks.Infrastructure.Shell.ShellOperationGateway.Default.SetDesktopIconsHidden(hide);
        }
        private void AutorunToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            bool on = (bool)AutorunToggle.IsChecked!;
            // Adds or removes the Run entry (off also removes the legacy DeskFrame one), then saves the switch.
            AppSettings.Default.TrySetStartOnLogin(on, on ? Process.GetCurrentProcess().MainModule!.FileName : null);
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
            Racks.Util.LifecycleAnimations.RunQuitAnimation(() => Application.Current.Shutdown());
        }

        private void Settings_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                {
                    _settingsWindow = new SettingsWindow(_controller, this);
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                    _settingsWindow.Show();
                }
                else
                {
                    _settingsWindow.Activate();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"SettingsWindow open failed: {ex.Message}"); }
        }
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
            if (msg == 0x007E) // WM_DISPLAYCHANGE
            {
                if (!_controller.isInitializingInstances)
                {
                    HandleDisplayOrSettingsChange();
                    reseted = true;
                }
            }
            if (msg == 0x001C && reseted) // WM_WININICHANGE
            {
                if (!_controller.isInitializingInstances)
                {

                    reseted = false;
                    HandleDisplayOrSettingsChange();
                }
            }
            if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == QUICK_FINDER_HOTKEY_ID)
            {
                try
                {
                    _quickFinder?.Close();
                    _quickFinder = new QuickFinderWindow(_controller);
                    _quickFinder.Show();
                    _quickFinder.Activate();
                }
                catch (Exception ex) { Debug.WriteLine($"QuickFinder open failed: {ex.Message}"); }
                handled = true;
            }
            if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == NEW_RACK_HOTKEY_ID)
            {
                try { _controller.AddVirtualInstance(); }
                catch (Exception ex) { Debug.WriteLine($"New rack hotkey failed: {ex.Message}"); }
                handled = true;
            }
            if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == DEV_DIAGNOSTICS_HOTKEY_ID)
            {
                ToggleDevDiagnostics();
                handled = true;
            }
            if (msg == WM_RACKS_OPEN_SETTINGS)
            {
                Settings_Button_Click(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_OPEN_HELP)
            {
                HelpMenuItem_Click(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_TOGGLE_LOCK_ALL)
            {
                LockAllToggle.IsChecked = !(LockAllToggle.IsChecked == true);
                LockAllToggle_CheckChanged(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_TOGGLE_ICE_PHYSICS)
            {
                IcePhysicsToggle.IsChecked = !(IcePhysicsToggle.IsChecked == true);
                IcePhysicsToggle_CheckChanged(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_TOGGLE_DESKTOP_ICONS)
            {
                HideDesktopIconsToggle.IsChecked = !(HideDesktopIconsToggle.IsChecked == true);
                HideDesktopIconsToggle_CheckChanged(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_TOGGLE_HOT_CORNER)
            {
                HotCornerHideToggle.IsChecked = !(HotCornerHideToggle.IsChecked == true);
                HotCornerHideToggle_CheckChanged(this, new RoutedEventArgs());
                handled = true;
            }
            else if (msg == WM_RACKS_OPEN_ABOUT)
            {
                try { new AboutWindow(150, 150, 320, 280).Show(); } catch { }
                handled = true;
            }
            else if (msg == WM_RACKS_TOGGLE_DEV_DIAGNOSTICS)
            {
                ToggleDevDiagnostics();
                handled = true;
            }
            return IntPtr.Zero;
        }
        // On a real display or settings change (resolution, monitor plugged/unplugged,
        // DPI, theme) reposition every rack against the new working area.
        //
        // This used to also spawn a throwaway "DummyWindow" (a real desktop-child
        // RackWindow shown and immediately closed) 200ms after each message. That was the
        // root cause of a runaway freeze: creating a desktop-child window itself emits
        // WM_DISPLAYCHANGE/WM_WININICHANGE, so each dummy triggered another change, which
        // spawned another dummy - a self-sustaining message storm (~4/sec) that pegged the
        // UI thread and made the app unresponsive (couldn't even quit) right after a rack
        // was created. CheckFrameWindowsLive already repositions and repaints the racks, so
        // the dummy did nothing useful. Removed. The _dummyWindowPending flag still
        // debounces bursts of genuine display messages into a single reposition pass.
        private void HandleDisplayOrSettingsChange()
        {
            if (_dummyWindowPending) return;
            _dummyWindowPending = true;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { _controller.CheckFrameWindowsLive(); }
                finally { _dummyWindowPending = false; }
            });
        }
        protected override void OnClosed(EventArgs e)
        {
            if (_lowLevelMouseHook != null)
            {
                _lowLevelMouseHook.Stop();
                _lowLevelMouseHook.Dispose();
            }
            _desktopRouter?.Dispose();
            _autoRouteService?.Dispose();
            StopHotCornerWatch();
            if (_mainHwnd != IntPtr.Zero)
            {
                try { Interop.UnregisterHotKey(_mainHwnd, QUICK_FINDER_HOTKEY_ID); } catch { }
                try { Interop.UnregisterHotKey(_mainHwnd, NEW_RACK_HOTKEY_ID); } catch { }
                try { Interop.UnregisterHotKey(_mainHwnd, DEV_DIAGNOSTICS_HOTKEY_ID); } catch { }
            }
            base.OnClosed(e);
        }

    }
}
