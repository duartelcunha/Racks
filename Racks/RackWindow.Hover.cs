#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8622, CS8625
using Racks.ViewModels;
using Racks.Core;
using Racks.Properties;
using Racks.Shaders;
using Racks.Util;
using static Racks.Util.ThemePresets;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using WindowsDesktop;
using Wpf.Ui.Controls;
using static Racks.Util.Interop;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using File = System.IO.File;
using ListView = Wpf.Ui.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Path = System.IO.Path;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
namespace Racks
{
    public partial class RackWindow
    {
        private void MouseLeaveWindow(bool animateActiveColor = true)
        {
            _mouseLeaveAnimateActiveColor = animateActiveColor;
            if (_mouseLeaveTimer == null)
            {
                _mouseLeaveTimer = new System.Windows.Forms.Timer { Interval = 1 };
                _mouseLeaveTimer.Tick += MouseLeaveTimer_Tick;
            }
            _mouseLeaveTimer.Start();
        }

        private void MouseLeaveTimer_Tick(object? sender, EventArgs e)
        {
            var timer = _mouseLeaveTimer!;
            bool animateActiveColor = _mouseLeaveAnimateActiveColor;
            {
                if (animateActiveColor && !IsCursorWithinWindowBounds() && (GetAsyncKeyState(0x01) & 0x8000) == 0)
                {
                    _mouseIsOver = false;
                    AnimateActiveColor(Instance.AnimationSpeed);
                    if (Instance.HideTitleBarIconsWhenInactive)
                    {
                        TitleBarIconsFadeAnimation(false);
                    }
                    if (!_contextMenuIsOpen)
                    {
                        _selectedItems.Clear();
                        foreach (var fileItem in FileItems)
                        {
                            fileItem.IsSelected = false;
                            fileItem.Background = Brushes.Transparent;
                        }
                    }
                    if (!_isRenamingFromContextMenu)
                    {
                        _itemCurrentlyRenaming = null;
                    }
                }
                if (!IsCursorWithinWindowBounds() && (GetAsyncKeyState(0x01) & 0x8000) == 0) // Left mouse button is not down
                {

                    if (_canAutoClose)
                    {
                        FilterTextBox.Text = null;
                        //   FilterTextBox.Visibility = Visibility.Collapsed;
                    }
                    if (!_isTopmost)
                    {
                        this.SetNoActivate();
                    }
                    if (_didFixIsOnBottom) _fixIsOnBottomInit = false;

                    if (_deselectTimer == null)
                    {
                        _deselectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
                        _deselectTimer.Tick += (s, args) =>
                        {
                            if (!_dragdropIntoFolder)
                            {
                                Dispatcher.InvokeAsync(() =>
                                {
                                    FileListView.SelectedIndex = -1;
                                    foreach (var item in FileListView.Items)
                                    {
                                        var container = FileListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                                        if (container != null) container.IsSelected = false;
                                    }
                                });
                                _deselectTimer!.Stop();
                            }
                        };
                    }
                    _deselectTimer.Start();

                    if ((Instance.AutoExpandonCursor) && !_isMinimized && _canAutoClose)
                    {
                        AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
                        Minimize_MouseLeftButtonDown(null, null);
                        Task.Run(() =>
                        {
                            try
                            {
                                if (!_contextMenuIsOpen)
                                {
                                    _selectedItems.Clear();
                                    foreach (var fileItem in FileItems)
                                    {
                                        fileItem.IsSelected = false;
                                        fileItem.Background = Brushes.Transparent;
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
                    }
                }
                if (!_mouseIsOver)
                {
                    timer.Stop();
                }
            }
        }

        private void AnimateSymbolIcon(UIElement target, double widthTo, double opacityTo, double marginTo)
        {
            var marginAnimation = new ThicknessAnimation
            {
                To = new Thickness(0, 0, marginTo, 0),
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            var widthAnimation = new DoubleAnimation
            {
                To = widthTo,
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var opacityAnimation = new DoubleAnimation
            {
                To = opacityTo,
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            target.BeginAnimation(FrameworkElement.MarginProperty, marginAnimation);
            target.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
            target.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        private void AnimateChevron(bool flip, bool onLoad, double animationSpeed)
        {


            var rotateTransform = ChevronRotate;

            int angleToAnimateTo;
            int duration;
            if (onLoad)
            {
                angleToAnimateTo = flip ? 0 : 180;
                duration = 10;
            }
            else
            {
                angleToAnimateTo = (rotateTransform.Angle == 180) ? 0 : 180;
                duration = (int)(200 / animationSpeed);
            }
            if (_isLocked) duration = (int)(200 / animationSpeed);

            var rotateAnimation = new DoubleAnimation
            {
                From = rotateTransform.Angle,
                To = angleToAnimateTo,
                Duration = (animationSpeed == 0) ?
                    TimeSpan.FromMilliseconds(40) :
                    TimeSpan.FromMilliseconds(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            _canAnimate = false;
            rotateAnimation.Completed += (s, e) => _canAnimate = true;

            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
        }

        public void AnimateWindowOpacity(double value, double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            var animation = new DoubleAnimation
            {
                To = value,
                Duration = animationSpeed == 0 ?
                    TimeSpan.FromSeconds(0.1) :
                    TimeSpan.FromSeconds(0.2 / animationSpeed),
            };
            this.BeginAnimation(OpacityProperty, animation);
        }

        public void AnimateGrayScale(double oldValue, double newValue)
        {
            var animation = new DoubleAnimation
            {
                From = oldValue,
                To = newValue,
                Duration = Instance.DisableAnimations ? TimeSpan.Zero : TimeSpan.FromSeconds(0.1),
                FillBehavior = FillBehavior.HoldEnd
            };
            _grayscaleEffect.BeginAnimation(GrayscaleEffect.StrengthProperty, animation);
        }

        public void AnimateActiveColor(double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            if (Instance.ActiveBackgroundEnabled
                || Instance.ActiveBorderEnabled
                || Instance.ActiveTitleTextEnabled
                || Instance.GrayScaleEnabled && Instance.GrayScaleEnabled_InactiveOnly)
            {
                _mouseIsOver = IsCursorWithinWindowBounds();
            }
            if (Instance.GrayScaleEnabled && Instance.GrayScaleEnabled_InactiveOnly)
            {
                var animation = new DoubleAnimation
                {
                    From = _mouseIsOver ? Instance.MaxGrayScaleStrength : 0.0,
                    To = _mouseIsOver ? 0.0 : Instance.MaxGrayScaleStrength,
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed),
                    FillBehavior = FillBehavior.HoldEnd
                };

                _grayscaleEffect.BeginAnimation(GrayscaleEffect.StrengthProperty, animation);
            }
            if (Instance.ActiveBorderEnabled)
            {
                if (!Instance.BorderEnabled)
                {
                    WindowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00000000"));
                    WindowBorder.BorderThickness = new Thickness(1.3);
                }

                // rebind to unfreeze the brush so that the animation can be applied
                WindowBorder.SetBinding(Border.BorderBrushProperty, new Binding("Instance.BorderColor")
                {
                    Source = this,
                });

                var backgroundColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? !Instance.BorderEnabled
                                            ? (Color)ColorConverter.ConvertFromString("#00000000")
                                            : (Color)ColorConverter.ConvertFromString(Instance.BorderColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ActiveBorderColor),

                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveBorderColor)
                                        : !Instance.BorderEnabled
                                            ? (Color)ColorConverter.ConvertFromString("#00000000")
                                            : (Color)ColorConverter.ConvertFromString(Instance.BorderColor),

                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                WindowBorder.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, backgroundColorAnimation);
                backgroundColorAnimation.Completed += (sender, e) =>
                {

                    WindowBorder.SetBinding(Border.BorderThicknessProperty, new Binding("Instance.BorderEnabled")
                    {
                        Source = this,
                        Converter = (IValueConverter)Resources["BooleanToBorderThicknessConverter"]
                    });
                };
            }
            else
            {

                WindowBorder.SetBinding(Border.BorderThicknessProperty, new Binding("Instance.BorderEnabled")
                {
                    Source = this,
                    Converter = (IValueConverter)Resources["BooleanToBorderThicknessConverter"]
                });
            }

            if (Instance.ActiveBackgroundEnabled)
            {
                var borderColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ActiveBackgroundColor),
                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveBackgroundColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor),
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                WindowBackground.Background.BeginAnimation(SolidColorBrush.ColorProperty, borderColorAnimation);
            }
            if (Instance.ActiveTitleTextEnabled)
            {
                var titleBarItemsColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.TitleTextColor)
                                       : (Color)ColorConverter.ConvertFromString(Instance.ActiveTitleTextColor),
                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveTitleTextColor)
                                       : (Color)ColorConverter.ConvertFromString(Instance.TitleTextColor),
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                  : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                title.Foreground.BeginAnimation(SolidColorBrush.ColorProperty, titleBarItemsColorAnimation);
            }
        }

        // A collapsed rack shows its title bar. With the drop shadow on, the background sits inside a margin, so the window needs that too or the bar (and its name) is clipped.
        private double CollapsedHeight => titleBar.Height + WindowBackground.Margin.Top + WindowBackground.Margin.Bottom;

        private void AnimateWindowHeight(double targetHeight, double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            double currentHeight = this.ActualHeight;

            var freezeAnimation = new DoubleAnimation
            {
                To = currentHeight,
                Duration = TimeSpan.Zero,
                FillBehavior = FillBehavior.HoldEnd
            };
            this.BeginAnimation(HeightProperty, freezeAnimation);

            var animation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = animationSpeed == 0 ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(0.2 / animationSpeed),
                EasingFunction = new QuadraticEase()
            };
            animation.Completed += (s, e) =>
            {
                _canAnimate = true;
                if (targetHeight == CollapsedHeight)
                {
                    scrollViewer.ScrollToTop();
                }
            };
            _canAnimate = false;
            this.BeginAnimation(HeightProperty, animation);
        }

        private void LoadingProgressRingFade(bool showLoading)
        {
            Storyboard fadeOut = (Storyboard)this.Resources["FadeOutLoadingProgressRingStoryboard"];
            Storyboard fadeIn = (Storyboard)this.Resources["FadeInLoadingProgressRingStoryboard"];

            if (showLoading)
            {
                LoadingProgressRing.IsIndeterminate = true;
                fadeIn.Begin();
            }
            else
            {
                // Turn the spin off DIRECTLY, not via fadeOut.Completed. An indeterminate
                // ProgressRing animates continuously - pinning a CPU core through nonstop
                // composition even while collapsed or at zero opacity. The old Completed
                // handler both leaked (its "-=" removed a different lambda, never the real
                // one) and could fail to fire, leaving the ring spinning forever. The fade
                // is only cosmetic opacity, so stopping the spin up front is fine.
                LoadingProgressRing.IsIndeterminate = false;
                fadeOut.Begin();
            }
        }

        public void TitleBarIconsFadeAnimation(bool show)
        {
            Storyboard fadeIn = (Storyboard)this.Resources["FadeIn_titleBarIcons_Storyboard"];
            Storyboard fadeOut = (Storyboard)this.Resources["FadeOut_titleBarIcons_Storyboard"];

            if (show)
            {
                fadeIn.Begin();
            }
            else
            {
                fadeOut.Completed += (s, e) =>
                {
                    fadeOut.Completed -= (s, e) => { }; // cleanup
                };
                fadeOut.Begin();
            }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_mouseIsOver && IsCursorWithinWindowBounds())
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive)
                {
                    TitleBarIconsFadeAnimation(true);
                }
            }
            _mouseIsOver = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            BringFrameToFront(hwnd, false);
            SetForegroundWindow(hwnd);
            this.Activate();
            SetFocus(hwnd);
            this.Focus();
            _canAutoClose = true;
            AnimateWindowOpacity(1, Instance.AnimationSpeed);
            if ((Instance.AutoExpandonCursor) && _isMinimized)
            {
                Minimize_MouseLeftButtonDown(null, null);
            }
        }

        public bool IsCursorWithinWindowBounds()
        {
            Point cursor = System.Windows.Forms.Cursor.Position;
            bool cursorIsOverTheWindow = WindowFromPoint(new POINT { X = cursor.X, Y = cursor.Y }) == new WindowInteropHelper(this).Handle;

            Interop.GetWindowRect(new WindowInteropHelper(this).Handle, out RECT rect);
            Point point = System.Windows.Forms.Cursor.Position;
            var curPoint = new Point((int)point.X, (int)point.Y);
            bool cursorIsWithinWindowBounds = point.X + 1 > rect.Left && point.X - 1 < rect.Right && point.Y + 1 > rect.Top && point.Y - 1 < rect.Bottom;

            if (_isDragging && (GetAsyncKeyState(0x01) & 0x8000) == 0) // Left not down
            {
                _isDragging = false;
            }
            if (_isDragging)
            {
                if (cursorIsWithinWindowBounds) return true;
                if (!cursorIsWithinWindowBounds) return false;
            }

            if (_contextMenuIsOpen
                || contextMenu.IsOpen
                || (_isDragging && (GetAsyncKeyState(0x01) & 0x8000) != 0)) return true;
            if (!_contextMenuIsOpen)
            {
                if (cursorIsOverTheWindow)
                {
                    return true;
                }

                return false;
            }
            return false;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            MouseLeaveWindow();
        }

        private void SymbolIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            InfoFlyout.IsOpen = true;
        }
    }
}
