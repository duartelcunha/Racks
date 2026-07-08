using System.ComponentModel;
using System.Diagnostics;
using System.Security.Policy;
using System.Windows;
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
        private System.Windows.Threading.DispatcherTimer? _hotCornerTimer;
        private bool _hotCornerHidden; // tracks whether racks are currently hidden by hot-corner
        private const int QUICK_FINDER_HOTKEY_ID = 0xB0CC;
        private const int NEW_RACK_HOTKEY_ID = 0xB0CD;
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
            InitializeComponent();
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
            RefreshGlobalHiddenFiles();

            // Auto-routing: any file landing on the user's Desktop that matches a
            // rack's AutoRouteRegex gets a .lnk in that rack. Original file stays put.
            try
            {
                _desktopRouter = new DesktopRouter(
                    () => _controller.Instances,
                    (sourcePath, destFolder) =>
                    {
                        // Auto-route MOVES matching files into the rack (no duplicate on Desktop).
                        // Routed through SafeMove so a too-greedy regex (e.g., ".*") can't
                        // vacuum shell-special folders into the rack, name collisions are
                        // skipped cleanly, and cross-volume moves fall back to copy+delete.
                        // Failures are silent here (auto-route is background, not user-
                        // initiated) — logged to Debug only.
                        try
                        {
                            string dest = System.IO.Path.Combine(destFolder, System.IO.Path.GetFileName(sourcePath));
                            bool isDir = System.IO.Directory.Exists(sourcePath);
                            var result = Racks.Util.SafeMove.TryMove(sourcePath, dest, out string reason);
                            if (result != Racks.Util.SafeMove.Result.Moved)
                            {
                                Debug.WriteLine($"Auto-route {result}: {reason}");
                                return;
                            }
                            Util.Interop.NotifyShellMove(sourcePath, dest, isDir);
                            Util.Interop.NotifyShellUpdateDir(System.IO.Path.GetDirectoryName(sourcePath)!);
                        }
                        catch (System.Exception ex) { Debug.WriteLine($"Auto-route move failed: {ex.Message}"); }
                    });
            }
            catch (System.Exception ex) { Debug.WriteLine($"DesktopRouter init failed: {ex.Message}"); }
            if (_controller.reg.KeyExistsRoot("startOnLogin")) startOnLogin = _controller.reg.ReadKeyValueRoot("startOnLogin") as bool? ?? false;
            AutorunToggle.IsChecked = startOnLogin;
            // if (_controller.reg.KeyExistsRoot("blurBackground")) BlurToggle.IsChecked = (bool)_controller.reg.ReadKeyValueRoot("blurBackground");
            if (_controller.reg.KeyExistsRoot("DoubleClickToHide")) DoubleClickToHide = _controller.reg.ReadKeyValueRoot("DoubleClickToHide") as bool? ?? false;
            if (_controller.reg.KeyExistsRoot("HideDesktopIcons"))
            {
                bool hide = _controller.reg.ReadKeyValueRoot("HideDesktopIcons") as bool? ?? false;
                HideDesktopIconsToggle.IsChecked = hide;
                if (hide) ApplyDesktopIconsHidden(true);
            }
            if (_controller.reg.KeyExistsRoot("HotCornerHide")
                && _controller.reg.ReadKeyValueRoot("HotCornerHide") as bool? == true)
            {
                HotCornerHideToggle.IsChecked = true;
                StartHotCornerWatch();
            }
            // Auto-update is disabled in this fork (no release pipeline yet).
        }
        private void HandleGlobalDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Keys.ToString() != "MouseLeft") return;
            if ((DateTime.Now - _lastDoubleClickTime).TotalSeconds < 0.3) return;
            _lastDoubleClickTime = DateTime.Now;

            POINT pt = new POINT { X = e.Position.X, Y = e.Position.Y };
            IntPtr hwndUnderCursor = WindowFromPoint(pt);
            IntPtr desktopListView = GetDesktopListViewHandle();

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


        public void RefreshGlobalHiddenFiles()
        {
            // Now handled entirely by RackWindow reporting to DesktopIconManager.
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
                    var msgDialog = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Magic Organizer",
                        Content = "Your desktop is already empty or too clean to organize!",
                        CloseButtonText = "Ok"
                    };
                    await msgDialog.ShowDialogAsync();
                    return;
                }

                var dialog = new Racks.Views.AutoOrganizePreviewDialog(clusters);
                if (dialog.ShowDialog() == true && dialog.Result != Racks.Views.OrganizeChoice.Cancel)
                {
                    if (dialog.Result == Racks.Views.OrganizeChoice.Racks)
                    {
                        // User approved, create racks and move files!
                        var workingArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                        
                        int rackWidth = 300;
                        int rackHeight = 380;
                        int gap = 30;
                        
                        int cols = (int)Math.Ceiling(Math.Sqrt(clusters.Count));
                        if (cols == 0) cols = 1;
                        int rows = (int)Math.Ceiling((double)clusters.Count / cols);
                        
                        int totalWidth = cols * rackWidth + (cols - 1) * gap;
                        int totalHeight = rows * rackHeight + (rows - 1) * gap;
                        
                        int startX = workingArea.Left + (workingArea.Width - totalWidth) / 2;
                        int startY = workingArea.Top + (workingArea.Height - totalHeight) / 2;
                        
                        // Fallback if it exceeds screen
                        if (startX < workingArea.Left) startX = workingArea.Left + 50;
                        if (startY < workingArea.Top) startY = workingArea.Top + 50;

                        int movedFilesCount = 0;
                        
                        for (int i = 0; i < clusters.Count; i++)
                        {
                            var cluster = clusters[i];
                            if (cluster.FilePaths.Count == 0) continue;

                            string safeName = string.Join("_", cluster.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Cluster_" + Guid.NewGuid().ToString().Substring(0, 4);

                            int col = i % cols;
                            int row = i / cols;

                            var inst = new Instance(safeName, false);
                            inst.TitleText = cluster.Name;
                            inst.Folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            inst.IsDesktopFilterRack = true;
                            inst.PosX = startX + col * (rackWidth + gap);
                            inst.PosY = startY + row * (rackHeight + gap);
                            inst.Width = rackWidth;
                            inst.Height = rackHeight;
                            
                            inst.AssignedFiles = new List<string>();

                            _controller.Instances.Add(inst);

                            // Ensure workspace exists before moving
                            Racks.Core.DesktopIconManager.Initialize();

                            // 2. Move files to RacksWorkspace and assign them
                            foreach (var fp in cluster.FilePaths)
                            {
                                string fileName = System.IO.Path.GetFileName(fp);
                                string destPath = System.IO.Path.Combine(Racks.Core.DesktopIconManager.RacksWorkspacePath, fileName);
                                
                                var moveResult = Racks.Util.SafeMove.TryMove(fp, destPath, out string reason);
                                if (moveResult == Racks.Util.SafeMove.Result.Moved)
                                {
                                    Racks.Util.Interop.NotifyShellMove(fp, destPath, System.IO.Directory.Exists(fp));
                                    inst.AssignedFiles.Add(fileName);
                                    movedFilesCount++;
                                }
                            }

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

                        Racks.Views.RacksMessageBox.Show($"Magic Organize Complete!\nMoved {movedFilesCount} items into {clusters.Count} racks.\nIf the racks are hidden behind other windows, you can bring them to the front from the tray icon.", "Success");
                    }
                    else if (dialog.Result == Racks.Views.OrganizeChoice.Folders)
                    {
                        // Just move them to physical folders on the Desktop
                        int movedFilesCount = 0;
                        foreach (var cluster in clusters)
                        {
                            if (cluster.FilePaths.Count == 0) continue;
                            
                            string folderPath = System.IO.Path.Combine(desktopPath, cluster.Name);
                            if (!System.IO.Directory.Exists(folderPath))
                            {
                                System.IO.Directory.CreateDirectory(folderPath);
                            }

                            foreach (var fp in cluster.FilePaths)
                            {
                                string fileName = System.IO.Path.GetFileName(fp);
                                string destPath = System.IO.Path.Combine(folderPath, fileName);
                                
                                var moveResult = Racks.Util.SafeMove.TryMove(fp, destPath, out string reason);
                                if (moveResult == Racks.Util.SafeMove.Result.Moved)
                                {
                                    Racks.Util.Interop.NotifyShellMove(fp, destPath, System.IO.Directory.Exists(fp));
                                    movedFilesCount++;
                                }
                            }
                        }
                        Racks.Views.RacksMessageBox.Show($"Magic Organize Complete!\nMoved {movedFilesCount} items into {clusters.Count} folders on your Desktop.", "Success");
                    }
                }
            }
            catch (Exception ex)
            {
                Racks.Views.RacksMessageBox.Show($"An error occurred during Magic Organize:\n{ex.Message}\n{ex.StackTrace}", "Fatal Error");
            }
        }

        private void ExportLayout_Click(object sender, RoutedEventArgs e)
        {
            try { Racks.Util.RackLayoutIO.PromptExport(); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}"); }
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try { new HelpWindow().Show(); }
            catch (Exception ex) { Debug.WriteLine($"HelpWindow open failed: {ex.Message}"); }
        }

        private async void ImportLayout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirm = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Import racks",
                    Content = "Importing will REPLACE all your current racks with the layout in the file. Continue?",
                    PrimaryButtonText = "Replace",
                    CloseButtonText = "Cancel",
                };
                if ((await confirm.ShowDialogAsync()) != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
                int count = Racks.Util.RackLayoutIO.PromptImport(replaceExisting: true);
                if (count > 0) _controller.CheckFrameWindowsLive(true);
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

            // Ctrl+Shift+Space opens the cross-rack quick finder.
            try { Interop.RegisterHotKey(_mainHwnd, QUICK_FINDER_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x20); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey QuickFinder failed: {ex.Message}"); }
            // Ctrl+Shift+N spawns a new virtual rack.
            try { Interop.RegisterHotKey(_mainHwnd, NEW_RACK_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x4E /* VK_N */); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey NewRack failed: {ex.Message}"); }
            // First-launch welcome animation: a Racks icon drops from the
            // center of the screen into the system tray and a toast follows
            // pointing the user at where the app now lives. Gated by a
            // registry marker so it runs at most once per machine.
            try { Racks.Util.FirstRunWelcome.ShowIfFirstRun(_controller.reg); }
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
                TrayIcon.Register();
                TrayIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Debug.WriteLine($"TrayIcon register failed: {ex.Message}"); }
        }
        private static IntPtr GetDesktopListViewHandle()
        {
            IntPtr progman = FindWindow("Progman", null!);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null!);

            if (defView == IntPtr.Zero)
            {
                IntPtr workerw = IntPtr.Zero;
                do
                {
                    workerw = FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null!);
                    defView = FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null!);
                }
                while (workerw != IntPtr.Zero && defView == IntPtr.Zero);
            }
            return FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
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
        private void LockAllToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
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

        // Hot-corner peek: poll cursor position every 100ms. When it enters a small
        // square at the top-right of the primary monitor, hide all racks; when it
        // leaves, show them again. State machine prevents flicker / repeated Hide.
        private void HotCornerHideToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            bool on = HotCornerHideToggle.IsChecked == true;
            _controller.reg.WriteToRegistryRoot("HotCornerHide", on);
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
            _controller.reg.WriteToRegistryRoot("HideDesktopIcons", hide);
            ApplyDesktopIconsHidden(hide);
        }

        private static void ApplyDesktopIconsHidden(bool hide)
        {
            try
            {
                IntPtr defView = GetDesktopShellView();
                if (defView != IntPtr.Zero) ShowWindow(defView, hide ? SW_HIDE : SW_SHOW);
            }
            catch (Exception ex) { Debug.WriteLine($"ApplyDesktopIconsHidden failed: {ex.Message}"); }
        }

        // Find the SHELLDLL_DefView (the actual desktop-icon host), regardless of
        // whether wallpaper slideshow is on (Progman) or off (WorkerW).
        private static IntPtr GetDesktopShellView()
        {
            IntPtr progman = FindWindow("Progman", null!);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null!);
            if (defView != IntPtr.Zero) return defView;
            IntPtr workerw = IntPtr.Zero;
            do
            {
                workerw = FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null!);
                defView = FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null!);
            } while (workerw != IntPtr.Zero && defView == IntPtr.Zero);
            return defView;
        }

        private void AutorunToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            if ((bool)AutorunToggle.IsChecked!)
            {

                _controller.reg.AddToAutoRun(InstanceController.appName, Process.GetCurrentProcess().MainModule!.FileName);
            }
            else
            {
                _controller.reg.RemoveFromAutoRun(InstanceController.appName);
                _controller.reg.RemoveFromAutoRun(InstanceController.LegacyAppName);
            }
            _controller.reg.WriteToRegistryRoot("startOnLogin", AutorunToggle.IsChecked);
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
            new SettingsWindow(_controller, this).Show();
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
            StopHotCornerWatch();
            if (_mainHwnd != IntPtr.Zero)
            {
                try { Interop.UnregisterHotKey(_mainHwnd, QUICK_FINDER_HOTKEY_ID); } catch { }
                try { Interop.UnregisterHotKey(_mainHwnd, NEW_RACK_HOTKEY_ID); } catch { }
            }
            base.OnClosed(e);
        }

    }
}