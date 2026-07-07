using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Application = System.Windows.Application;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace Racks.Util
{
    public static class LifecycleAnimations
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static void ForceTopmost(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            }
            catch { }
        }

        private static (Window win, Image image, ScaleTransform scale, RotateTransform rotate, TranslateTransform translate, Canvas canvas) CreateAnimationWindow()
        {
            var screen = SystemParameters.WorkArea;
            
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri("pack://application:,,,/ico.png", UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();

            var image = new Image
            {
                Source = src,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var translate = new TranslateTransform(0, 0);
            var scale = new ScaleTransform(1, 1);
            var rotate = new RotateTransform(0);
            image.RenderTransform = new TransformGroup
            {
                Children = { scale, rotate, translate }
            };

            var canvas = new Canvas { Background = Brushes.Transparent };
            canvas.Children.Add(image);

            var win = new Window
            {
                Style = null,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Left,
                Top = screen.Top,
                Width = screen.Width,
                Height = screen.Height,
                Content = canvas,
            };

            return (win, image, scale, rotate, translate, canvas);
        }

        public static void RunQuitAnimation(Action onComplete)
        {
            try
            {
                var (win, image, scale, rotate, translate, canvas) = CreateAnimationWindow();
                var screen = SystemParameters.WorkArea;
                
                const double startSize = 22; // Starts from tray size
                const double endSize = 96;
                double trayX = screen.Width - startSize - 14;
                double trayY = screen.Height - startSize - 8;
                double centerX = screen.Width / 2;
                double centerY = screen.Height / 2;

                image.Width = startSize;
                image.Height = startSize;
                
                Canvas.SetLeft(image, trayX);
                Canvas.SetTop(image, trayY);
                win.Show();
                ForceTopmost(win);

                double dx = (centerX - endSize / 2) - trayX;
                double dy = (centerY - endSize / 2) - trayY;
                double targetScale = endSize / startSize;

                // 0-600ms: Arc to center while scaling up and spinning
                // 600-900ms: Shrink to 0 and fade out

                var scaleX = new DoubleAnimationUsingKeyFrames();
                scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(targetScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.8 }));
                scaleX.Duration = TimeSpan.FromMilliseconds(900);

                var scaleY = new DoubleAnimationUsingKeyFrames();
                scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(targetScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.8 }));
                scaleY.Duration = TimeSpan.FromMilliseconds(900);

                var transX = new DoubleAnimationUsingKeyFrames();
                transX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                transX.KeyFrames.Add(new EasingDoubleKeyFrame(dx, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new SineEase { EasingMode = EasingMode.EaseOut }));
                transX.Duration = TimeSpan.FromMilliseconds(900);

                var transY = new DoubleAnimationUsingKeyFrames();
                transY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                transY.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                transY.Duration = TimeSpan.FromMilliseconds(900);

                var spin = new DoubleAnimationUsingKeyFrames();
                spin.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                spin.KeyFrames.Add(new EasingDoubleKeyFrame(-360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                spin.Duration = TimeSpan.FromMilliseconds(900);

                var winOpacity = new DoubleAnimationUsingKeyFrames();
                winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
                winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
                winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
                winOpacity.Duration = TimeSpan.FromMilliseconds(1000);

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                translate.BeginAnimation(TranslateTransform.XProperty, transX);
                translate.BeginAnimation(TranslateTransform.YProperty, transY);
                rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
                win.BeginAnimation(UIElement.OpacityProperty, winOpacity);

                Task.Delay(1100).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try { win.Close(); } catch { }
                        onComplete?.Invoke();
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunQuitAnimation failed (skipping animation): {ex.Message}");
                onComplete?.Invoke();
            }
        }

        public static void RunUninstallAnimation(Action onComplete)
        {
            try
            {
                var (win, image, scale, rotate, translate, canvas) = CreateAnimationWindow();
                var screen = SystemParameters.WorkArea;
                
                const double size = 128;
                double centerX = screen.Width / 2;
                double centerY = screen.Height / 2;

                image.Width = size;
                image.Height = size;
                
                Canvas.SetLeft(image, centerX - size / 2);
                Canvas.SetTop(image, centerY - size / 2);
                
                // Add a shockwave element (initially hidden/collapsed)
                var shockwave = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 4,
                    Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                
                var shockwaveScale = new ScaleTransform(1, 1);
                shockwave.RenderTransform = shockwaveScale;

                Canvas.SetLeft(shockwave, centerX - size / 2);
                Canvas.SetTop(shockwave, centerY - size / 2);
                canvas.Children.Insert(0, shockwave); // Behind image
                
                win.Show();
                ForceTopmost(win);

                // Phase 1: Singularity (Spin and suck into center)
                var suckTime = TimeSpan.FromMilliseconds(600);
                
                var suckScaleAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = suckTime,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
                };
                
                var spinAnim = new DoubleAnimation
                {
                    From = 0,
                    To = 720, // 2 full rotations
                    Duration = suckTime,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
                };

                // Phase 2: Shockwave (Burst into nothing)
                var burstTime = TimeSpan.FromMilliseconds(400);
                
                var shockwaveScaleAnim = new DoubleAnimation
                {
                    From = 0.1,
                    To = 10.0,
                    Duration = burstTime,
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };
                
                var shockwaveFadeAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = burstTime,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                
                var shockwaveThickAnim = new DoubleAnimation
                {
                    From = 15.0,
                    To = 0.0,
                    Duration = burstTime,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };

                // Execute Singularity
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, suckScaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, suckScaleAnim);
                rotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
                
                Task.Delay(suckTime).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Hide image
                        image.Visibility = Visibility.Collapsed;
                        
                        // Execute Shockwave
                        shockwave.Opacity = 1;
                        shockwaveScale.BeginAnimation(ScaleTransform.ScaleXProperty, shockwaveScaleAnim);
                        shockwaveScale.BeginAnimation(ScaleTransform.ScaleYProperty, shockwaveScaleAnim);
                        shockwave.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, shockwaveFadeAnim);
                        shockwave.BeginAnimation(System.Windows.Shapes.Ellipse.StrokeThicknessProperty, shockwaveThickAnim);
                        
                        Task.Delay(burstTime).ContinueWith(__ =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try { win.Close(); } catch { }
                                onComplete?.Invoke();
                            });
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunUninstallAnimation failed (skipping animation): {ex.Message}");
                onComplete?.Invoke();
            }
        }
    }
}
