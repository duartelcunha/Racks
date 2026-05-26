using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using Color = System.Windows.Media.Color;
using Racks.ColorPicker;
using System.IO;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TextBox = Wpf.Ui.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using WindowsDesktop;
using System.Windows.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Racks
{
    public partial class RackSettingsDialog : FluentWindow
    {
        private readonly RackWindow _frame;
        private readonly Instance _instance;
        private readonly Instance _originalInstance;

        private bool _isValidTitleBarColor;
        private bool _isValidTitleTextColor;
        private bool _isValidTitleTextAlignment = true;
        private bool _isValidBorderColor;
        private bool _isValidActiveBorderColor;
        private bool _isValidActiveBackgroundColor;
        private bool _isValidActiveTitleTextColor;
        private bool _isValidFileFilterRegex = true;
        private bool _isValidFileFilterHideRegex = true;
        private bool _isValidListViewBackgroundColor = true;
        private bool _isValidListViewFontColor = true;
        private bool _isValidListViewFontShadowColor = true;
        private bool _isValidShowOnVirtualDesktops = true;
        private bool _isReverting;
        private bool _initDone;

        private string _lastInstanceName;
        private Brush _borderBrush;
        private Brush _backgroundBrush;

        public ObservableCollection<string> FontList { get; }

        private ScrollViewer[] _pages;

        public RackSettingsDialog(RackWindow frame)
        {
            InitializeComponent();

            this.Owner = frame;

            _backgroundBrush = TitleBarColorTextBox.Background;
            _borderBrush = TitleBarColorTextBox.BorderBrush;

            _originalInstance = new Instance(frame.Instance, frame.Instance.SettingDefault);
            _lastInstanceName = _originalInstance.Name;
            _frame = frame;
            _instance = frame.Instance;
            DataContext = _instance;

            _pages = new[] { PageAppearance, PageTitleBar, PageItems, PageWindow, PageFolder };

            GrayScaleEnabled_CheckBox.IsChecked = _instance.GrayScaleEnabled;
            GrayScaleEnabled_InactiveOnly_CheckBox.IsChecked = _instance.GrayScaleEnabled_InactiveOnly;
            MaxGrayScaleStrengthSlider.Value = _instance.MaxGrayScaleStrength * 10;

            if (_instance.Folder == "empty")
            {
                ChangeFolderButton.Visibility = Visibility.Collapsed;
            }
            if (_instance.SettingDefault)
            {
                TitleTextBox.Visibility = Visibility.Hidden;
                ShowOnVirtualDesktopTextBox.Visibility = Visibility.Hidden;
                FileFilterRegexTextBox.Visibility = Visibility.Hidden;
                FileFilterHideRegexTextBox.Visibility = Visibility.Hidden;
            }

            AnimationSpeedSlider.Value = _instance.AnimationSpeed * 4;
            AnimationSpeedLabel.Text = _instance.AnimationSpeed == 0.0 ? "OFF" : "x" + _instance.AnimationSpeed;
            IdleOpacitySlider.Value = _instance.IdleOpacity * 10;
            IdleOpacityLabel.Text = _instance.IdleOpacity * 100 + "%";
            IconSizeSlider.Value = _instance.IconSize / 4;
            IconSizeLabel.Text = _instance.IconSize.ToString();

            frame.AnimateWindowOpacity(_instance.IdleOpacity, _instance.AnimationSpeed);

            if (!frame.VirtualDesktopSupported)
            {
                ShowOnVirtualDesktopTextBox.Text = "Not available";
                ShowOnVirtualDesktopTextBox.IsEnabled = false;
            }
            else
            {
                ShowOnVirtualDesktopTextBox.Text = _instance.ShowOnVirtualDesktops != null
                    ? string.Join(",", _instance.ShowOnVirtualDesktops)
                    : string.Empty;
            }

            _originalInstance.ShowOnVirtualDesktops = _instance.ShowOnVirtualDesktops;
            _originalInstance.AnimationSpeed = _instance.AnimationSpeed;
            _originalInstance.IdleOpacity = _instance.IdleOpacity;
            _originalInstance.IconSize = _instance.IconSize;
            _originalInstance.HideTitleBarIconsWhenInactive = _instance.HideTitleBarIconsWhenInactive;
            _originalInstance.SnapWidthToIconWidth = _instance.SnapWidthToIconWidth;
            _originalInstance.SnapWidthToIconWidth_PlusScrollbarWidth = _instance.SnapWidthToIconWidth_PlusScrollbarWidth;
            _originalInstance.ShowShortcutArrow = _instance.ShowShortcutArrow;
            _originalInstance.MaxGrayScaleStrength = _instance.MaxGrayScaleStrength;
            _originalInstance.AutoExpandonCursor = _instance.AutoExpandonCursor;
            _originalInstance.FolderOpenInsideFrame = _instance.FolderOpenInsideFrame;
            _originalInstance.CheckFolderSize = _instance.CheckFolderSize;
            _originalInstance.GrayScaleEnabled = _instance.GrayScaleEnabled;
            _originalInstance.GrayScaleEnabled_InactiveOnly = _instance.GrayScaleEnabled_InactiveOnly;
            _originalInstance.LastAccesedToFirstRow = _instance.LastAccesedToFirstRow;
            _originalInstance.TitleText = _instance.TitleText;

            TitleBarColorTextBox.Text = _instance.TitleBarColor;
            TitleTextColorTextBox.Text = _instance.TitleTextColor;
            ListViewBackgroundColorTextBox.Text = _instance.ListViewBackgroundColor;
            ListViewFontColorTextBox.Text = _instance.ListViewFontColor;
            ListViewFontShadowColorTextBox.Text = _instance.ListViewFontShadowColor;
            BorderColorTextBox.Text = _instance.BorderColor;
            ActiveBorderColorTextBox.Text = _instance.ActiveBorderColor;
            ActiveBackgroundColorTextBox.Text = _instance.ActiveBackgroundColor;
            ActiveTitleTextColorTextBox.Text = _instance.ActiveTitleTextColor;
            BorderEnabledCheckBox.IsChecked = _instance.BorderEnabled;
            ActiveBorderEnabledCheckBox.IsChecked = _instance.ActiveBorderEnabled;
            ActiveBackgroundEnabledCheckBox.IsChecked = _instance.ActiveBackgroundEnabled;
            ActiveTitleTextEnabledCheckBox.IsChecked = _instance.ActiveTitleTextEnabled;
            TitleTextBox.Text = _instance.TitleText ?? _instance.Name;
            TitleFontSizeNumberBox.Value = _instance.TitleFontSize > 0 ? _instance.TitleFontSize : 13;
            FileFilterRegexTextBox.Text = _instance.FileFilterRegex;
            FileFilterHideRegexTextBox.Text = _instance.FileFilterHideRegex;
            TitleTextAlignmentComboBox.SelectedIndex = (int)_instance.TitleTextAlignment;

            ShowFileExtensionIconCheckBox.IsChecked = _instance.ShowFileExtensionIcon;
            ShowHiddenFilesIconCheckBox.IsChecked = _instance.ShowHiddenFilesIcon;
            ShowDisplayNameCheckBox.IsChecked = _instance.ShowDisplayName;
            HideTitleBarIconsWhenInactive_CheckBox.IsChecked = _instance.HideTitleBarIconsWhenInactive;
            SnapWidthToIconWidth_CheckBox.IsChecked = _instance.SnapWidthToIconWidth;
            SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.IsChecked = _instance.SnapWidthToIconWidth_PlusScrollbarWidth;
            SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.Visibility = _instance.SnapWidthToIconWidth ? Visibility.Visible : Visibility.Collapsed;
            AutoExpandonCursorCheckBox.IsChecked = _instance.AutoExpandonCursor;
            ShowShortcutArrowCheckBox.IsChecked = _instance.ShowShortcutArrow;
            FolderOpenInsideFrameCheckBox.IsChecked = _instance.FolderOpenInsideFrame;
            CheckFolderSizeCheckBox.IsChecked = _instance.CheckFolderSize;
            ShowLastAccessedToFirstRowCheckBox.IsChecked = _instance.LastAccesedToFirstRow;

            TitleTextAutoSuggestionBox.Text = _instance.TitleFontFamily;
            ItemTextAutoSuggestionBox.Text = _instance.ItemFontFamily;
            _frame.title.FontSize = _instance.TitleFontSize;
            _frame.title.TextWrapping = TextWrapping.Wrap;

            double titleBarHeight = Math.Max(30, _instance.TitleFontSize * 1.5);
            _frame.titleBar.Height = titleBarHeight;
            double scrollViewerMargin = titleBarHeight + 5;
            _frame.scrollViewer.Margin = new Thickness(0, scrollViewerMargin, 0, 0);

            TitleFontSizeNumberBox.ValueChanged += (sender, args) =>
            {
                if (args.NewValue.HasValue)
                {
                    _instance.TitleFontSize = args.NewValue.Value;
                    _frame.title.FontSize = args.NewValue.Value;
                    _frame.title.TextWrapping = TextWrapping.Wrap;

                    double tbH = Math.Max(30, args.NewValue.Value * 1.5);
                    _frame.titleBar.Height = tbH;
                    _frame.scrollViewer.Margin = new Thickness(0, tbH + 5, 0, 0);
                }
            };

            FontList = new ObservableCollection<string>();
            InstalledFontCollection fonts = new InstalledFontCollection();
            foreach (System.Drawing.FontFamily font in fonts.Families)
            {
                FontList.Add(font.Name);
            }

            var fontIndex = new HashSet<string>(FontList, StringComparer.OrdinalIgnoreCase);

            TitleTextAutoSuggestionBox.ItemsSource = FontList;
            TitleTextAutoSuggestionBox.Text = string.IsNullOrEmpty(_instance.TitleFontFamily) ? "Segoe UI" : _instance.TitleFontFamily;
            TitleTextAutoSuggestionBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new System.Windows.Controls.TextChangedEventHandler((s, e) =>
                {
                    if (!_initDone) return;
                    var val = TitleTextAutoSuggestionBox.Text;
                    if (string.IsNullOrWhiteSpace(val) || !fontIndex.Contains(val)) return;
                    _frame.title.FontFamily = new System.Windows.Media.FontFamily(val);
                    _instance.TitleFontFamily = val;
                }));

            ItemTextAutoSuggestionBox.ItemsSource = FontList;
            ItemTextAutoSuggestionBox.Text = string.IsNullOrEmpty(_instance.ItemFontFamily) ? "Segoe UI" : _instance.ItemFontFamily;
            ItemTextAutoSuggestionBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new System.Windows.Controls.TextChangedEventHandler((s, e) =>
                {
                    if (!_initDone) return;
                    var val = ItemTextAutoSuggestionBox.Text;
                    if (string.IsNullOrWhiteSpace(val) || !fontIndex.Contains(val)) return;
                    _frame.Resources["ItemFont"] = new System.Windows.Media.FontFamily(val);
                    _instance.ItemFontFamily = val;
                }));

            _initDone = true;
        }

        private int _currentPage = 0;

        private void SectionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_pages == null) return;
            int idx = SectionList.SelectedIndex;
            if (idx < 0) idx = 0;
            if (idx == _currentPage && _pages[idx].Visibility == Visibility.Visible) return;

            var newPage = _pages[idx];
            ScrollViewer? oldPage = null;
            if (_currentPage >= 0 && _currentPage < _pages.Length && _currentPage != idx)
                oldPage = _pages[_currentPage];

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (oldPage != null)
            {
                var fadeOut = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(110),
                    EasingFunction = ease
                };
                var captured = oldPage;
                fadeOut.Completed += (_, __) => captured.Visibility = Visibility.Collapsed;
                oldPage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            newPage.Opacity = 0;
            newPage.Visibility = Visibility.Visible;
            var slide = new ThicknessAnimation
            {
                From = new Thickness(10, 0, -10, 0),
                To = new Thickness(0),
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = ease
            };
            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(60)
            };
            newPage.BeginAnimation(FrameworkElement.MarginProperty, slide);
            newPage.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            _currentPage = idx;
        }

        private double _rackLastLeft;
        private double _rackLastTop;
        private bool _rackTracked;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DockToRack();
            _rackLastLeft = _frame.Left;
            _rackLastTop = _frame.Top;
            _frame.LocationChanged += Frame_LocationChanged;
            _frame.SizeChanged += Frame_SizeChanged;
            _rackTracked = true;

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_rackTracked)
            {
                _frame.LocationChanged -= Frame_LocationChanged;
                _frame.SizeChanged -= Frame_SizeChanged;
                _rackTracked = false;
            }
        }

        private void Frame_LocationChanged(object? sender, EventArgs e)
        {
            double dx = _frame.Left - _rackLastLeft;
            double dy = _frame.Top - _rackLastTop;
            _rackLastLeft = _frame.Left;
            _rackLastTop = _frame.Top;
            this.Left += dx;
            this.Top += dy;
        }

        private void Frame_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DockToRack();
            _rackLastLeft = _frame.Left;
            _rackLastTop = _frame.Top;
        }

        private void DockToRack()
        {
            var workArea = SystemParameters.WorkArea;
            double gap = 8;

            double rackLeft = _frame.Left;
            double rackTop = _frame.Top;
            double rackWidth = _frame.Width;
            double rackHeight = _frame.Height;

            bool offscreen = _frame.WindowState == WindowState.Minimized
                || rackLeft < -10000 || rackTop < -10000
                || double.IsNaN(rackLeft) || double.IsNaN(rackTop);

            if (offscreen)
            {
                rackLeft = _instance.PosX > -10000 ? _instance.PosX : workArea.Left + 40;
                rackTop = _instance.PosY > -10000 ? _instance.PosY : workArea.Top + 40;
                rackWidth = _instance.Width > 0 ? _instance.Width : 280;
                rackHeight = _instance.Height > 0 ? _instance.Height : 200;
            }

            double desiredLeft = rackLeft + rackWidth + gap;
            if (desiredLeft + this.Width > workArea.Right)
            {
                desiredLeft = rackLeft - this.Width - gap;
                if (desiredLeft < workArea.Left)
                {
                    desiredLeft = Math.Max(workArea.Left, workArea.Right - this.Width);
                }
            }
            double desiredTop = rackTop;
            if (desiredTop + this.Height > workArea.Bottom)
                desiredTop = workArea.Bottom - this.Height;
            if (desiredTop < workArea.Top)
                desiredTop = workArea.Top;

            this.Left = desiredLeft;
            this.Top = desiredTop;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initDone) return;
            ValidateSettings();
        }

        private void TextChangedHandler(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_initDone) return;
            ValidateSettings();
        }

        private void TitleTextAlignmentComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_initDone) return;
            ValidateSettings();
        }

        private void BorderEnabledCheckBox_Checked(object sender, RoutedEventArgs e) { if (!_initDone) return; ValidateSettings(); }
        private void ActiveBorderEnabledCheckBox_Checked(object sender, RoutedEventArgs e) { if (!_initDone) return; ValidateSettings(); }
        private void ActiveTitleTextEnabledCheckBox_Checked(object sender, RoutedEventArgs e) { if (!_initDone) return; ValidateSettings(); }
        private void ActiveBackgroundEnabledCheckBox_Checked(object sender, RoutedEventArgs e) { if (!_initDone) return; ValidateSettings(); }

        private bool ValidateVirtualDesktop(string strValue)
        {
            return strValue
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(s => int.TryParse(s, out _));
        }

        private void ValidateSettings()
        {
            if (_isReverting) return;
            _instance.AnimationSpeed = AnimationSpeedSlider.Value * 0.25;
            AnimationSpeedLabel.Text = _instance.AnimationSpeed == 0 ? "OFF" : "x" + _instance.AnimationSpeed;
            _instance.IdleOpacity = IdleOpacitySlider.Value == 0 ? 0.002 : (IdleOpacitySlider.Value / 10);
            IdleOpacityLabel.Text = _instance.IdleOpacity * 100 + "%";
            _instance.IconSize = (int)(IconSizeSlider.Value * 4);
            IconSizeLabel.Text = _instance.IconSize.ToString();

            _frame.AnimateWindowOpacity(_instance.IdleOpacity, _instance.AnimationSpeed);
            _isValidTitleBarColor = TryParseColor(string.IsNullOrEmpty(TitleBarColorTextBox.Text) ? "#0C000000" : TitleBarColorTextBox.Text, TitleBarColorTextBox);
            _isValidTitleTextColor = TryParseColor(string.IsNullOrEmpty(TitleTextColorTextBox.Text) ? "#FFFFFF" : TitleTextColorTextBox.Text, TitleTextColorTextBox);
            _isValidBorderColor = BorderEnabledCheckBox.IsChecked == true ? TryParseColor(string.IsNullOrEmpty(BorderColorTextBox.Text) ? "#FFFFFF" : BorderColorTextBox.Text, BorderColorTextBox) : true;
            _isValidActiveBorderColor = ActiveBorderEnabledCheckBox.IsChecked == true ? TryParseColor(string.IsNullOrEmpty(ActiveBorderColorTextBox.Text) ? "#FFFFFF" : ActiveBorderColorTextBox.Text, ActiveBorderColorTextBox) : true;
            _isValidActiveBackgroundColor = ActiveBackgroundEnabledCheckBox.IsChecked == true ? TryParseColor(string.IsNullOrEmpty(ActiveBackgroundColorTextBox.Text) ? "#FFFFFF" : ActiveBackgroundColorTextBox.Text, ActiveBackgroundColorTextBox) : true;
            _isValidActiveTitleTextColor = ActiveTitleTextEnabledCheckBox.IsChecked == true ? TryParseColor(string.IsNullOrEmpty(ActiveTitleTextColorTextBox.Text) ? "#FFFFFF" : ActiveTitleTextColorTextBox.Text, ActiveTitleTextColorTextBox) : true;
            _isValidFileFilterRegex = TryParseRegex(FileFilterRegexTextBox.Text, FileFilterRegexTextBox);
            _isValidFileFilterHideRegex = TryParseRegex(FileFilterHideRegexTextBox.Text, FileFilterHideRegexTextBox);

            _isValidTitleTextAlignment = TitleTextAlignmentComboBox.SelectedIndex >= 0;
            _isValidListViewBackgroundColor = TryParseColor(string.IsNullOrEmpty(ListViewBackgroundColorTextBox.Text) ? "#0C000000" : ListViewBackgroundColorTextBox.Text, ListViewBackgroundColorTextBox);
            _isValidListViewFontColor = TryParseColor(string.IsNullOrEmpty(ListViewFontColorTextBox.Text) ? "#FFFFFF" : ListViewFontColorTextBox.Text, ListViewFontColorTextBox);
            _isValidListViewFontShadowColor = TryParseColor(string.IsNullOrEmpty(ListViewFontShadowColorTextBox.Text) ? "#000000" : ListViewFontShadowColorTextBox.Text, ListViewFontShadowColorTextBox);

            _isValidShowOnVirtualDesktops = !_frame.VirtualDesktopSupported
                || ValidateVirtualDesktop(ShowOnVirtualDesktopTextBox.Text);

            if (_isValidTitleBarColor && _isValidTitleTextColor && _isValidTitleTextAlignment &&
                _isValidBorderColor && _isValidActiveBorderColor && _isValidActiveBackgroundColor && _isValidActiveTitleTextColor && _isValidFileFilterRegex && _isValidFileFilterHideRegex &&
                _isValidListViewBackgroundColor && _isValidListViewFontColor && _isValidListViewFontShadowColor &&
                _isValidShowOnVirtualDesktops)
            {
                _instance.TitleBarColor = string.IsNullOrEmpty(TitleBarColorTextBox.Text) ? "#0C000000" : TitleBarColorTextBox.Text;
                _instance.TitleTextColor = string.IsNullOrEmpty(TitleTextColorTextBox.Text) ? "#FFFFFF" : TitleTextColorTextBox.Text;

                _instance.BorderColor = BorderColorTextBox.Text;
                _instance.ActiveBorderColor = ActiveBorderColorTextBox.Text;
                _instance.ActiveBackgroundColor = ActiveBackgroundColorTextBox.Text;
                _instance.ActiveTitleTextColor = ActiveTitleTextColorTextBox.Text;
                _instance.BorderEnabled = BorderEnabledCheckBox.IsChecked == true;
                _instance.ActiveBorderEnabled = ActiveBorderEnabledCheckBox.IsChecked == true;
                _instance.ActiveBackgroundEnabled = ActiveBackgroundEnabledCheckBox.IsChecked == true;
                _instance.ActiveTitleTextEnabled = ActiveTitleTextEnabledCheckBox.IsChecked == true;
                _instance.TitleTextAlignment = (System.Windows.HorizontalAlignment)TitleTextAlignmentComboBox.SelectedIndex;
                _instance.TitleText = TitleTextBox.Text;
                _instance.FileFilterRegex = FileFilterRegexTextBox.Text;
                _instance.FileFilterHideRegex = FileFilterHideRegexTextBox.Text;

                _instance.ListViewBackgroundColor = string.IsNullOrEmpty(ListViewBackgroundColorTextBox.Text) ? "#0C000000" : ListViewBackgroundColorTextBox.Text;
                _instance.ListViewFontColor = string.IsNullOrEmpty(ListViewFontColorTextBox.Text) ? "#FFFFFF" : ListViewFontColorTextBox.Text;
                _instance.ListViewFontShadowColor = string.IsNullOrEmpty(ListViewFontShadowColorTextBox.Text) ? "#000000" : ListViewFontShadowColorTextBox.Text;
                _instance.Opacity = ((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.ListViewBackgroundColor)).A;
                _instance.TitleFontSize = TitleFontSizeNumberBox.Value ?? 12;

                if (_frame.VirtualDesktopSupported)
                {
                    var parts = ShowOnVirtualDesktopTextBox.Text
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    _instance.ShowOnVirtualDesktops = parts.Length > 0
                        ? parts.Select(s => int.Parse(s)).ToArray()
                        : null;

                    if (_instance.ShowOnVirtualDesktops != null && !_instance.ShowOnVirtualDesktops.Contains(Array.IndexOf(VirtualDesktop.GetDesktops(), VirtualDesktop.Current) + 1))
                    {
                        _frame.Hide();
                    }
                    else
                    {
                        _frame.Show();
                        this.Activate();
                    }
                }
                _frame.titleBar.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.TitleBarColor));
                _frame.title.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.TitleTextColor));
                _frame.title.Text = TitleTextBox.Text == "" ? _instance.Name : _instance.TitleText;

                _frame.WindowBackground.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.ListViewBackgroundColor));

                TitleBarColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.TitleBarColor));
                TitleTextColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.TitleTextColor));
                ListViewBackgroundColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.ListViewBackgroundColor));
                ListViewFontColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.ListViewFontColor));
                ListViewFontShadowColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_instance.ListViewFontShadowColor));
                BorderColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(TryParseColor(BorderColorTextBox.Text, BorderColorTextBox) ? _instance.BorderColor : "#FFFFFF"));
                ActiveBorderColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(TryParseColor(ActiveBorderColorTextBox.Text, ActiveBorderColorTextBox) ? _instance.ActiveBorderColor : "#FFFFFF"));
                ActiveBackgroundColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(TryParseColor(ActiveBackgroundColorTextBox.Text, ActiveBackgroundColorTextBox) ? _instance.ActiveBackgroundColor : "#FFFFFF"));
                ActiveTitleTextColorTextBox.Icon!.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(TryParseColor(ActiveTitleTextColorTextBox.Text, ActiveTitleTextColorTextBox) ? _instance.ActiveTitleTextColor : "#FFFFFF"));
            }
        }

        private bool TryParseColor(string colorText, TextBox tb)
        {
            try
            {
                new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(colorText));
                tb.BorderBrush = _borderBrush;
                tb.Background = _backgroundBrush;
                return true;
            }
            catch
            {
                tb.BorderBrush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF96A6A"));
                tb.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#0FD82B2B"));
                return false;
            }
        }

        private bool TryParseRegex(string regexText, TextBox tb)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(regexText))
                {
                    new System.Text.RegularExpressions.Regex(regexText);
                }
                tb.BorderBrush = _borderBrush;
                tb.Background = _backgroundBrush;
                return true;
            }
            catch
            {
                tb.BorderBrush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF96A6A"));
                tb.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#0FD82B2B"));
                return false;
            }
        }

        private async void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Confirm",
                Content = "Revert all settings to the values they had when you opened this window?",
                PrimaryButtonText = "Revert",
                CloseButtonText = "Cancel"
            };

            var result = await dialog.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                _isReverting = true;
                _instance.TitleBarColor = _originalInstance.TitleBarColor;
                _instance.TitleTextColor = _originalInstance.TitleTextColor;
                _instance.BorderColor = _originalInstance.BorderColor;
                _instance.ActiveBorderColor = _originalInstance.ActiveBorderColor;
                _instance.ActiveBackgroundColor = _originalInstance.ActiveBackgroundColor;
                _instance.ActiveTitleTextColor = _originalInstance.ActiveTitleTextColor;
                _instance.BorderEnabled = _originalInstance.BorderEnabled;
                _instance.ActiveBorderEnabled = _originalInstance.ActiveBorderEnabled;
                _instance.ActiveBackgroundEnabled = _originalInstance.ActiveBackgroundEnabled;
                _instance.ActiveTitleTextEnabled = _originalInstance.ActiveTitleTextEnabled;
                _instance.TitleText = _originalInstance.TitleText == "" ? _originalInstance.Name : _originalInstance.TitleText;
                _instance.FileFilterRegex = _originalInstance.FileFilterRegex;
                _instance.FileFilterHideRegex = _originalInstance.FileFilterHideRegex;
                _instance.TitleTextAlignment = _originalInstance.TitleTextAlignment;
                _instance.ListViewBackgroundColor = _originalInstance.ListViewBackgroundColor;
                _instance.ListViewFontColor = _originalInstance.ListViewFontColor;
                _instance.ListViewFontShadowColor = _originalInstance.ListViewFontShadowColor;
                _instance.Opacity = _originalInstance.Opacity;
                _instance.TitleFontSize = _originalInstance.TitleFontSize;
                _instance.TitleFontFamily = _originalInstance.TitleFontFamily;
                _instance.ItemFontFamily = _originalInstance.ItemFontFamily;
                _instance.ShowOnVirtualDesktops = _originalInstance.ShowOnVirtualDesktops;
                _instance.IdleOpacity = _originalInstance.IdleOpacity;
                _instance.IconSize = _originalInstance.IconSize;
                _instance.AnimationSpeed = _originalInstance.AnimationSpeed;
                _instance.AutoExpandonCursor = _originalInstance.AutoExpandonCursor;
                _instance.ShowShortcutArrow = _originalInstance.ShowShortcutArrow;
                _instance.FolderOpenInsideFrame = _originalInstance.FolderOpenInsideFrame;
                _instance.CheckFolderSize = _originalInstance.CheckFolderSize;
                _instance.HideTitleBarIconsWhenInactive = _originalInstance.HideTitleBarIconsWhenInactive;
                _instance.SnapWidthToIconWidth = _originalInstance.SnapWidthToIconWidth;
                _instance.SnapWidthToIconWidth_PlusScrollbarWidth = _originalInstance.SnapWidthToIconWidth_PlusScrollbarWidth;
                SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.Visibility = _instance.SnapWidthToIconWidth ? Visibility.Visible : Visibility.Collapsed;

                _instance.MaxGrayScaleStrength = _originalInstance.MaxGrayScaleStrength;
                _instance.GrayScaleEnabled = _originalInstance.GrayScaleEnabled;
                _instance.GrayScaleEnabled_InactiveOnly = _originalInstance.GrayScaleEnabled_InactiveOnly;

                _instance.LastAccesedToFirstRow = _originalInstance.LastAccesedToFirstRow;

                if (_originalInstance.Folder != _instance.Folder)
                {
                    _instance.Folder = _originalInstance.Folder;
                    _frame._currentFolderPath = _originalInstance.Folder;
                    string name = _instance.Name;
                    _instance.Name = Path.GetFileName(_originalInstance.Name);

                    _frame.title.Text = TitleTextBox.Text == "" ? _instance.Name : _instance.TitleText;

                    MainWindow._controller.WriteOverInstanceToKey(_instance, name);
                    _frame.LoadFiles(_frame._currentFolderPath);
                    _frame.InitializeFileWatchers();
                }

                _frame.TitleBarIconsFadeAnimation(!_instance.HideTitleBarIconsWhenInactive);
                AnimationSpeedSlider.Value = _originalInstance.AnimationSpeed * 4;
                AnimationSpeedLabel.Text = _originalInstance.AnimationSpeed == 0.0 ? "OFF" : "x" + _originalInstance.AnimationSpeed;
                IdleOpacitySlider.Value = _instance.IdleOpacity * 10;
                _frame.AnimateWindowOpacity(_instance.IdleOpacity, _instance.AnimationSpeed);
                IdleOpacityLabel.Text = _instance.IdleOpacity * 100 + "%";
                IconSizeSlider.Value = _instance.IconSize / 4;
                IconSizeLabel.Text = _instance.IconSize.ToString();

                PositionX_NumberBox.Value = _originalInstance.PosX;
                PositionY_NumberBox.Value = _originalInstance.PosY;

                _instance.Folder = _originalInstance.Folder;
                _instance.Name = _originalInstance.Name;
                _instance.TitleText = _originalInstance.TitleText;

                TitleBarColorTextBox.Text = _instance.TitleBarColor;
                TitleTextColorTextBox.Text = _instance.TitleTextColor;
                BorderColorTextBox.Text = _instance.BorderColor;
                ActiveBorderColorTextBox.Text = _instance.ActiveBorderColor;
                ActiveBackgroundColorTextBox.Text = _instance.ActiveBackgroundColor;
                ActiveTitleTextColorTextBox.Text = _instance.ActiveTitleTextColor;
                BorderEnabledCheckBox.IsChecked = _instance.BorderEnabled;
                ActiveBorderEnabledCheckBox.IsChecked = _instance.ActiveBorderEnabled;
                ActiveBackgroundEnabledCheckBox.IsChecked = _instance.ActiveBackgroundEnabled;
                ActiveTitleTextEnabledCheckBox.IsChecked = _instance.ActiveTitleTextEnabled;

                GrayScaleEnabled_CheckBox.IsChecked = _instance.GrayScaleEnabled;
                GrayScaleEnabled_InactiveOnly_CheckBox.IsChecked = _instance.GrayScaleEnabled_InactiveOnly;
                MaxGrayScaleStrengthSlider.Value = _instance.MaxGrayScaleStrength * 10;

                TitleTextBox.Text = _instance.TitleText ?? _instance.Name;
                FileFilterRegexTextBox.Text = _instance.FileFilterRegex;
                FileFilterHideRegexTextBox.Text = _instance.FileFilterHideRegex;

                TitleTextAlignmentComboBox.SelectedIndex = (int)_instance.TitleTextAlignment;
                ListViewBackgroundColorTextBox.Text = _instance.ListViewBackgroundColor;
                ListViewFontColorTextBox.Text = _instance.ListViewFontColor;
                ListViewFontShadowColorTextBox.Text = _instance.ListViewFontShadowColor;
                TitleFontSizeNumberBox.Value = _instance.TitleFontSize;
                TitleTextAutoSuggestionBox.Text = _instance.TitleFontFamily;
                ItemTextAutoSuggestionBox.Text = _instance.ItemFontFamily;

                AutoExpandonCursorCheckBox.IsChecked = _instance.AutoExpandonCursor;
                ShowShortcutArrowCheckBox.IsChecked = _instance.ShowShortcutArrow;
                FolderOpenInsideFrameCheckBox.IsChecked = _instance.FolderOpenInsideFrame;
                CheckFolderSizeCheckBox.IsChecked = _instance.CheckFolderSize;

                HideTitleBarIconsWhenInactive_CheckBox.IsChecked = _instance.HideTitleBarIconsWhenInactive;

                SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.IsChecked = _instance.SnapWidthToIconWidth_PlusScrollbarWidth;
                SnapWidthToIconWidth_CheckBox.IsChecked = _instance.SnapWidthToIconWidth;
                SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.Visibility = _instance.SnapWidthToIconWidth ? Visibility.Visible : Visibility.Collapsed;

                ShowLastAccessedToFirstRowCheckBox.IsChecked = _instance.LastAccesedToFirstRow;

                ShowOnVirtualDesktopTextBox.Text = _instance.ShowOnVirtualDesktops != null
                      ? string.Join(",", _instance.ShowOnVirtualDesktops)
                      : string.Empty;
                if (_instance.ShowOnVirtualDesktops != null && !_instance.ShowOnVirtualDesktops.Contains(Array.IndexOf(VirtualDesktop.GetDesktops(), VirtualDesktop.Current) + 1))
                {
                    _frame.Hide();
                }
                else
                {
                    _frame.Show();
                    this.Activate();
                }
                _isReverting = false;
                ValidateSettings();
            }
        }

        private void OpenColorPicker(System.Windows.Controls.TextBox textbox)
        {
            ColorCard.Children.Clear();
            var colorPicker = new ColorPicker.ColorPicker(textbox);
            ColorCard.Children.Add(colorPicker);

            uiFlyout.PlacementTarget = textbox;
            uiFlyout.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            uiFlyout.IsOpen = true;
        }

        private void Titlebar_CloseClicked(TitleBar sender, RoutedEventArgs args)
        {
            this.DialogResult = true;
        }

        private void ShowFileExtensionIconCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.ShowFileExtensionIcon = ShowFileExtensionIconCheckBox.IsChecked ?? false;
            _frame.UpdateIconVisibility();
        }

        private void ShowHiddenFilesIconCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.ShowHiddenFilesIcon = ShowHiddenFilesIconCheckBox.IsChecked ?? false;
            _frame.UpdateIconVisibility();
        }

        private void ShowDisplayNameCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.ShowDisplayName = ShowDisplayNameCheckBox.IsChecked ?? true;
            _frame.UpdateIconVisibility();
        }

        private void HideTitleBarIconsWhenInactive_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.HideTitleBarIconsWhenInactive = HideTitleBarIconsWhenInactive_CheckBox.IsChecked ?? true;
            _frame.TitleBarIconsFadeAnimation(!_instance.HideTitleBarIconsWhenInactive);
        }

        private void SnapWidthToIconWidth_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.SnapWidthToIconWidth = SnapWidthToIconWidth_CheckBox.IsChecked ?? true;
            SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.Visibility = _instance.SnapWidthToIconWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.SnapWidthToIconWidth_PlusScrollbarWidth = SnapWidthToIconWidth_PlusScrollbarWidth_CheckBox.IsChecked ?? true;
        }

        private void AutoExpandonCursorCheckBox_Checked(object sender, RoutedEventArgs e) => _instance.AutoExpandonCursor = true;
        private void AutoExpandonCursorCheckBox_Unchecked(object sender, RoutedEventArgs e) => _instance.AutoExpandonCursor = false;

        private void GrayScaleEnabled_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.GrayScaleEnabled = (bool)GrayScaleEnabled_CheckBox.IsChecked!;
            GrayScaleEnabled_InactiveOnly_CheckBox.Visibility =
                _instance.GrayScaleEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (!_instance.GrayScaleEnabled)
            {
                _frame.AnimateGrayScale(_instance.MaxGrayScaleStrength, 0);
            }
            else if (_instance.GrayScaleEnabled != null)
            {
                _frame.AnimateGrayScale(0, _instance.MaxGrayScaleStrength);
            }
        }

        private void GrayScaleEnabled_InactiveOnly_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.GrayScaleEnabled_InactiveOnly = (bool)GrayScaleEnabled_InactiveOnly_CheckBox.IsChecked!;
        }

        private void ShowShortcutArrowCheckBox_Checked(object sender, RoutedEventArgs e) => _instance.ShowShortcutArrow = true;
        private void ShowShortcutArrowCheckBox_Unchecked(object sender, RoutedEventArgs e) => _instance.ShowShortcutArrow = false;

        private void FolderOpenInsideFrameCheckBox_Checked(object sender, RoutedEventArgs e) => _instance.FolderOpenInsideFrame = true;
        private void FolderOpenInsideFrameCheckBox_Unchecked(object sender, RoutedEventArgs e) => _instance.FolderOpenInsideFrame = false;

        private void CheckFolderSizeCheckBox_Checkked(object sender, RoutedEventArgs e) => _instance.CheckFolderSize = true;
        private void CheckFolderSizeCheckBox_Uncheckked(object sender, RoutedEventArgs e) => _instance.CheckFolderSize = false;

        private void ShowLastAccessedToFirstRowCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _instance.LastAccesedToFirstRow = true;
            _frame.SortItems();
        }

        private void ShowLastAccessedToFirstRowCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _instance.LastAccesedToFirstRow = false;
            _frame.SortItems();
        }

        private void ChangeFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select a folder",
                ShowNewFolderButton = true
            };
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _instance.Folder = folderDialog.SelectedPath;
                _frame._currentFolderPath = _instance.Folder;
                _instance.Name = Path.GetFileName(folderDialog.SelectedPath);
                MainWindow._controller.WriteOverInstanceToKey(_instance, _lastInstanceName);
                _lastInstanceName = _instance.Name;
                _frame.LoadFiles(_frame._currentFolderPath);
                _frame.title.Text = _instance.TitleText == "" ? _instance.Name : _instance.TitleText;
                _instance.TitleText = _instance.TitleText;
                TitleTextBox.Text = _instance.TitleText;
                _frame.InitializeFileWatchers();
                _frame.PathToBackButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ListViewFontShadowColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ListViewFontShadowColorTextBox);
        private void ListViewFontColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ListViewFontColorTextBox);
        private void TitleTextColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(TitleTextColorTextBox);
        private void TitleBarColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(TitleBarColorTextBox);
        private void ListViewBackgroundColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ListViewBackgroundColorTextBox);
        private void BorderColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(BorderColorTextBox);
        private void ActiveBorderColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ActiveBorderColorTextBox);
        private void ActiveBackgroundColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ActiveBackgroundColorTextBox);
        private void ActiveTitleTextColorTextBoxIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenColorPicker(ActiveTitleTextColorTextBox);

        private void ChangeStyleDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow._controller.Instances.Count <= 1)
                return;

            ContextMenu contextMenu = new ContextMenu();
            foreach (var instance in MainWindow._controller.Instances)
            {
                if (ReferenceEquals(instance, _instance)) continue;
                var menuItem = new MenuItem
                {
                    Header = instance.TitleText ?? instance.Name,
                };
                menuItem.Click += (s, e) =>
                {
                    AnimationSpeedSlider.Value = instance.AnimationSpeed * 4;
                    AnimationSpeedLabel.Text = instance.AnimationSpeed == 0.0 ? "OFF" : "x" + instance.AnimationSpeed;
                    IdleOpacitySlider.Value = instance.IdleOpacity * 10;
                    IdleOpacityLabel.Text = instance.IdleOpacity * 100 + "%";
                    IconSizeSlider.Value = instance.IconSize / 4;
                    IconSizeLabel.Text = instance.IconSize.ToString();
                    _backgroundBrush = TitleBarColorTextBox.Background;
                    _borderBrush = TitleBarColorTextBox.BorderBrush;
                    TitleBarColorTextBox.Text = instance.TitleBarColor;
                    TitleTextColorTextBox.Text = instance.TitleTextColor;
                    ListViewBackgroundColorTextBox.Text = instance.ListViewBackgroundColor;
                    ListViewFontColorTextBox.Text = instance.ListViewFontColor;
                    ListViewFontShadowColorTextBox.Text = instance.ListViewFontShadowColor;
                    BorderColorTextBox.Text = instance.BorderColor;
                    ActiveBorderColorTextBox.Text = instance.ActiveBorderColor;
                    ActiveBackgroundColorTextBox.Text = instance.ActiveBackgroundColor;
                    ActiveTitleTextColorTextBox.Text = instance.ActiveTitleTextColor;
                    BorderEnabledCheckBox.IsChecked = instance.BorderEnabled;
                    ActiveBorderEnabledCheckBox.IsChecked = instance.ActiveBorderEnabled;
                    ActiveBackgroundEnabledCheckBox.IsChecked = instance.ActiveBackgroundEnabled;
                    ActiveTitleTextEnabledCheckBox.IsChecked = instance.ActiveTitleTextEnabled;
                    TitleFontSizeNumberBox.Value = instance.TitleFontSize;
                    TitleTextAutoSuggestionBox.Text = instance.TitleFontFamily;
                    ItemTextAutoSuggestionBox.Text = instance.ItemFontFamily;
                    TitleTextAlignmentComboBox.SelectedIndex = (int)instance.TitleTextAlignment;
                    AutoExpandonCursorCheckBox.IsChecked = instance.AutoExpandonCursor;
                    ShowShortcutArrowCheckBox.IsChecked = instance.ShowShortcutArrow;
                    FolderOpenInsideFrameCheckBox.IsChecked = instance.FolderOpenInsideFrame;
                    CheckFolderSizeCheckBox.IsChecked = instance.CheckFolderSize;
                    ShowLastAccessedToFirstRowCheckBox.IsChecked = instance.LastAccesedToFirstRow;
                };
                contextMenu.Items.Add(menuItem);
            }
            contextMenu.PlacementTarget = ChangeStyleDropDownButton;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private async void ChangePosition(object sender, NumberBoxValueChangedEventArgs args)
        {
            await _frame.AdjustPositionAsync();
        }

        private void MaxGrayScaleStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initDone) return;
            _instance.MaxGrayScaleStrength = MaxGrayScaleStrengthSlider.Value / 10;
            MaxGrayScaleStrengthLabel.Text = (_instance.MaxGrayScaleStrength * 100).ToString("F0") + "%";
            if (_instance.GrayScaleEnabled)
            {
                _frame.AnimateGrayScale(e.OldValue / 10, e.NewValue / 10);
            }
            else if (!_instance.GrayScaleEnabled)
            {
                _frame.AnimateGrayScale(_instance.MaxGrayScaleStrength, 0);
            }
        }
    }
}
