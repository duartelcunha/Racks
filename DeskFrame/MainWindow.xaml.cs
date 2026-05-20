using System.ComponentModel;
using System.Diagnostics;
using System.Security.Policy;
using System.Windows;
using DeskFrame.Util;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using static DeskFrame.Util.Interop;
using System.Windows.Interop;
using H.Hooks;
using MouseEventArgs = H.Hooks.MouseEventArgs;
using System.Windows.Automation;
namespace DeskFrame
{
    public partial class MainWindow : Window
    {
        bool startOnLogin;
        bool reseted = false;
        private uint _taskbarRestartMessage;
        public static InstanceController _controller;
        private LowLevelMouseHook _lowLevelMouseHook;
        private DesktopRouter _desktopRouter;
        private System.Windows.Threading.DispatcherTimer _hotCornerTimer;
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
                _lowLevelMouseHook = new LowLevelMouseHook { AddKeyboardKeys = true };
                _lowLevelMouseHook.DoubleClick += HandleGlobalDoubleClick;
                _lowLevelMouseHook.Start();
            }
            else
            {
                _lowLevelMouseHook.Stop();
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Tray-menu header shows just "Racks" in a clean font. The version is still
            // available in the About window and as the ToolTip on this header for nerds.
            versionHeader.ToolTip = $"Racks {Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion}";
            _controller = new InstanceController();
            _controller.InitInstances();

            // Auto-routing: any file landing on the user's Desktop that matches a
            // rack's AutoRouteRegex gets a .lnk in that rack. Original file stays put.
            try
            {
                _desktopRouter = new DesktopRouter(
                    () => _controller.Instances,
                    (sourcePath, destFolder) =>
                    {
                        // Auto-route MOVES matching files into the rack (no duplicate on Desktop).
                        // If a name collision exists in the destination, the source is left alone.
                        // SHChangeNotify is called after each successful move so the Desktop
                        // view drops the icon without F5.
                        try
                        {
                            string dest = System.IO.Path.Combine(destFolder, System.IO.Path.GetFileName(sourcePath));
                            bool isDir = System.IO.Directory.Exists(sourcePath);
                            if (isDir)
                            {
                                if (System.IO.Directory.Exists(dest)) return;
                                System.IO.Directory.Move(sourcePath, dest);
                            }
                            else if (System.IO.File.Exists(sourcePath))
                            {
                                if (System.IO.File.Exists(dest)) return;
                                System.IO.File.Move(sourcePath, dest);
                            }
                            else return;
                            Util.Interop.NotifyShellMove(sourcePath, dest, isDir);
                            Util.Interop.NotifyShellUpdateDir(System.IO.Path.GetDirectoryName(sourcePath)!);
                        }
                        catch (System.Exception ex) { Debug.WriteLine($"Auto-route move failed: {ex.Message}"); }
                    });
            }
            catch (System.Exception ex) { Debug.WriteLine($"DesktopRouter init failed: {ex.Message}"); }
            if (_controller.reg.KeyExistsRoot("startOnLogin")) startOnLogin = (bool)_controller.reg.ReadKeyValueRoot("startOnLogin");
            AutorunToggle.IsChecked = startOnLogin;
            // if (_controller.reg.KeyExistsRoot("blurBackground")) BlurToggle.IsChecked = (bool)_controller.reg.ReadKeyValueRoot("blurBackground");
            if (_controller.reg.KeyExistsRoot("DoubleClickToHide")) DoubleClickToHide = (bool)_controller.reg.ReadKeyValueRoot("DoubleClickToHide");
            if (_controller.reg.KeyExistsRoot("HideDesktopIcons"))
            {
                bool hide = (bool)_controller.reg.ReadKeyValueRoot("HideDesktopIcons");
                HideDesktopIconsToggle.IsChecked = hide;
                if (hide) ApplyDesktopIconsHidden(true);
            }
            if (_controller.reg.KeyExistsRoot("HotCornerHide")
                && (bool)_controller.reg.ReadKeyValueRoot("HotCornerHide"))
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

        private void ExportLayout_Click(object sender, RoutedEventArgs e)
        {
            try { DeskFrame.Util.RackLayoutIO.PromptExport(); }
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
                int count = DeskFrame.Util.RackLayoutIO.PromptImport(replaceExisting: true);
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
        }
        private static IntPtr GetDesktopListViewHandle()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                IntPtr workerw = IntPtr.Zero;
                do
                {
                    workerw = FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null);
                    defView = FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null);
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
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
            IntPtr workerw = IntPtr.Zero;
            do
            {
                workerw = FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null);
                defView = FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null);
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
                ProcessStartInfo sInfo = new ProcessStartInfo($"https://github.com/duartelcunha") { UseShellExecute = true };
                _ = Process.Start(sInfo);
            }
            catch
            {
            }
        }

        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Settings_Button_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(_controller, this).Show();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _taskbarRestartMessage = RegisterWindowMessage("TaskbarCreated");
            _mainHwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(_mainHwnd);
            hwndSource.AddHook(WndProc);
            // Ctrl+Shift+Space opens the cross-rack quick finder.
            try { Interop.RegisterHotKey(_mainHwnd, QUICK_FINDER_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x20); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey QuickFinder failed: {ex.Message}"); }
            // Ctrl+Shift+N spawns a new virtual rack.
            try { Interop.RegisterHotKey(_mainHwnd, NEW_RACK_HOTKEY_ID, Interop.MOD_CONTROL | Interop.MOD_SHIFT, 0x4E /* VK_N */); }
            catch (Exception ex) { Debug.WriteLine($"RegisterHotKey NewRack failed: {ex.Message}"); }
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarRestartMessage)
            {
                // always recreate on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TrayIcon.Register();
                    if (!_controller.isInitializingInstances)
                        _controller.CheckFrameWindowsLive();
                });
            }
            if (msg == 0x007E) // WM_DISPLAYCHANGE   
            {
                if (!_controller.isInitializingInstances)
                {
                    // System.Windows.Forms.MessageBox.Show("ee");

                    _controller.CheckFrameWindowsLive();
                    Thread.Sleep(200);
                    DummyWindow();
                    reseted = true;
                }
            }
            if (msg == 0x001C && reseted) // WM_WININICHANGE
            {
                if (!_controller.isInitializingInstances)
                {

                    reseted = false;
                    _controller.CheckFrameWindowsLive();
                    Thread.Sleep(200);
                    DummyWindow();
                }
            }
            if (msg == Interop.WM_HOTKEY && wParam.ToInt32() == QUICK_FINDER_HOTKEY_ID)
            {
                try
                {
                    var finder = new QuickFinderWindow(_controller);
                    finder.Show();
                    finder.Activate();
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
        private void DummyWindow()
        {
            var window = new DeskFrameWindow(new Instance("empty", false))
            {
                MinHeight = 1,
                MinWidth = 1,
                Height = 1,
                Width = 1,
                Opacity = 0,
            };
            window.Show();
            window.Close();
        }
        protected override void OnClosed(EventArgs e)
        {
            if (_lowLevelMouseHook != null)
            {
                _lowLevelMouseHook.Stop();
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