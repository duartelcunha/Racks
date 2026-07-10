using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
namespace Racks
{
    public partial class SettingsWindow : Window
    {
        InstanceController _controller;
        RackWindow? _dWindows;
        Instance? _instance;
        MainWindow _window;
        ContextMenu? ManageFrameContextMenu;
        public SettingsWindow(InstanceController controller, MainWindow window)
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            this.LocationChanged += Window_LocationChanged;
            this.MinHeight = 0;
            this.MinWidth = 200;
            _window = window;
            _controller = controller;
            // if (_controller.reg.KeyExistsRoot("blurBackground")) blurToggle.IsChecked = (bool)_controller.reg.ReadKeyValueRoot("blurBackground");
            if (_controller.reg.KeyExistsRoot("AutoUpdate")) AutoUpdateToggleSwitch.IsChecked = _controller.reg.ReadKeyValueRoot("AutoUpdate") as bool? ?? false;
            if (_controller.reg.KeyExistsRoot("DoubleClickToHide")) DoubleClickToHideSwitch.IsChecked = _controller.reg.ReadKeyValueRoot("DoubleClickToHide") as bool? ?? false;
            // Ice-rink physics: default on, reflect whatever the engine currently has loaded.
            IcePhysicsSwitch.IsChecked = _controller.reg.KeyExistsRoot("IcePhysics")
                ? (_controller.reg.ReadKeyValueRoot("IcePhysics") as bool? ?? true)
                : Racks.Util.RackPhysics.Enabled;

            // Open on the monitor the user is actually on (where they clicked the tray),
            // not always the primary. Centre this window in that monitor's working area.
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) => Racks.Util.WindowPlacement.CenterOnCursorScreen(this);
        }

        private void blurToggle_CheckChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            //   _controller.reg.WriteToRegistryRoot("blurBackground", blurToggle.IsChecked!);
            //   _controller.ChangeBlur((bool)blurToggle.IsChecked!);
        }

        private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ExportRegistryKey(_controller.reg.regKeyName);
        }

        void ExportRegistryKey(string regKeyName)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Registry Files (*.reg)|*.reg",
                Title = "Export Registry Key",
                FileName = $"Racks_settings_{DateTime.Now.ToString("yyyy-MM-dd_hhmm")}"
            };
            if (saveDialog.ShowDialog() == true)
            {
                string fullKeyPath = $@"HKCU\SOFTWARE\{regKeyName}";
                string arguments = $"export \"{fullKeyPath}\" \"{saveDialog.FileName}\" /y";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            }
        }

        private void AutoUpdateToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)AutoUpdateToggleSwitch.IsChecked!)
            {

                _controller.reg.AddToAutoRun(InstanceController.appName, Process.GetCurrentProcess().MainModule!.FileName);
            }
            else
            {
                _controller.reg.RemoveFromAutoRun(InstanceController.appName);
                _controller.reg.RemoveFromAutoRun(InstanceController.LegacyAppName);
            }
            _controller.reg.WriteToRegistryRoot("AutoUpdate", AutoUpdateToggleSwitch.IsChecked);
        }

        private void ManageFrameButton_Click(object sender, RoutedEventArgs e)
        {
            ManageFrameContextMenu = new ContextMenu();
            List<MenuItem> menuItems = new List<MenuItem>();
            foreach (var frame in _controller._subWindows)
            {
                MenuItem menuItem = new MenuItem
                {
                    Header = frame.title.Text,
                    Height = 34,
                    Icon = new SymbolIcon(SymbolRegular.Window20)
                };

                string originalBorderColor = frame.Instance.BorderColor;
                bool originalBorderState = frame.Instance.BorderEnabled;

                menuItem.Click += (_, _) =>
                {
                    var dialog = new RackSettingsDialog(frame);
                    dialog.ShowDialog();
                    if (dialog.DialogResult == true)
                    {
                        MainWindow._controller.WriteInstanceToKey(frame.Instance);
                        frame.LoadFiles(frame._currentFolderPath);
                    }
                };
                menuItem.MouseEnter += (_, _) =>
                {
                    menuItem.Icon.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#7CFF00"));
                    frame.Instance.BorderEnabled = true;
                    frame.Instance.BorderColor = "#7CFF00";
                };
                menuItem.MouseLeave += (_, _) =>
                {
                    menuItem.Icon.Foreground = Brushes.White;
                    frame.Instance.BorderEnabled = originalBorderState;
                    frame.Instance.BorderColor = originalBorderColor;
                };
                menuItems.Add(menuItem);
            }
            foreach (var item in menuItems)
            {
                ManageFrameContextMenu.Items.Add(item);
            }
            ManageFrameContextMenu.IsOpen = true;
        }

        private void DefaultFrameStyleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dWindows != null) _dWindows.Close();

            _instance = new Instance("Default Style", true);
            _instance.SettingDefault = true;
            _instance.Folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            _dWindows = new RackWindow(_instance);
            _dWindows.addFolder.Visibility = Visibility.Hidden;
            _dWindows.showFolder.Visibility = Visibility.Visible;
            _dWindows.title.Visibility = Visibility.Visible;
            _dWindows.WindowBorder.Visibility = Visibility.Visible;
            _dWindows.Show();
            _dWindows.Left = this.Width + this.Left + 10;
            _dWindows.Top = this.Top;
        }
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_dWindows != null)
            {
                _dWindows.Left = this.Width + this.Left + 10;
                _dWindows.Top = this.Top;
            }
        }
        private void ResetDefaultFrameStyleButton_Click(object sender, RoutedEventArgs e)
        {
            string[] keep = { "AutoUpdate", "blurBackground", "startOnLogin" };
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($"Software\\{InstanceController.appName}", writable: true);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (Array.IndexOf(keep, name) == -1)
                {
                    try
                    {
                        key.DeleteValue(name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting registry key: {ex.Message}");
                    }
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_dWindows != null)
            {
                _dWindows.Close();
            }
        }
        private void DoubleClickToHideSwitch_Click(object sender, RoutedEventArgs e)
        {
            _controller.reg.WriteToRegistryRoot("DoubleClickToHide", DoubleClickToHideSwitch.IsChecked!);
            _window.DoubleClickToHide = (bool)DoubleClickToHideSwitch.IsChecked!;
        }

        private void IcePhysicsSwitch_Click(object sender, RoutedEventArgs e)
        {
            bool on = (bool)IcePhysicsSwitch.IsChecked!;
            _controller.reg.WriteToRegistryRoot("IcePhysics", on);
            Racks.Util.RackPhysics.Enabled = on; // takes effect live, no restart
        }

        private void KofiButtonImage_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
                    try
                    {
                        ProcessStartInfo sInfo = new ProcessStartInfo($"https://ko-fi.com/J3J61PAH6H") { UseShellExecute = true };
                        _ = Process.Start(sInfo);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Kofi open failed: {ex.Message}");
                    }
        }

        private void ReloadAllFramesButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadAllFramesButton.IsEnabled = false;
            _window.Dispatcher.Invoke(() =>
            {
                _window.TrayIcon.Register();
                if (!_controller.isInitializingInstances)
                    _controller.CheckFrameWindowsLive(true);
            });
            ReloadAllFramesButton.IsEnabled = true;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AboutWindow(this.Top, this.Left, this.Height, this.Width);
            dialog.ShowDialog();
        }

    }
}
