using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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

        // Keep an animation window pinned above everything for its whole lifetime - racks
        // reparent to the desktop and other windows can steal the top spot, hiding it. The
        // returned handler must be detached before the window closes.
        private static EventHandler KeepOnTop(Window w)
        {
            w.Activate();
            ForceTopmost(w);
            EventHandler h = (_, _) => ForceTopmost(w);
            CompositionTarget.Rendering += h;
            return h;
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

        // NORMAL launch "wake up": the logo blooms in near the tray corner (scale-up from
        // small with a soft overshoot) and settles, then quickly fades. No full-screen dim,
        // no roll, no travel across the screen - deliberately different from both the install
        // animation (rolls in and drops) and the quit animation (rises from the tray to
        // center). Short (~750ms) and unobtrusive so it's pleasant on every login.
        public static void RunLaunchAnimation()
        {
            try
            {
                var (win, image, scale, rotate, translate, canvas) = CreateAnimationWindow();
                win.Background = Brushes.Transparent; // never dim the screen on a normal launch
                var screen = SystemParameters.WorkArea;

                const double size = 92;
                image.Width = size;
                image.Height = size;
                image.Opacity = 0;
                // Start near the tray corner; a soft glow makes it feel like it lights up.
                double x = screen.Width - size - 22;
                double y = screen.Height - size - 18;
                Canvas.SetLeft(image, x);
                Canvas.SetTop(image, y);
                var glow = new DropShadowEffect { Color = Colors.White, BlurRadius = 0, ShadowDepth = 0, Opacity = 0 };
                image.Effect = glow;

                win.Show();
                var keep = KeepOnTop(win);

                // Spring up from small with a gentle overshoot, a tiny lift, a glow flash, then
                // settle and fade. Quick (~700ms) and elegant - the daily "Racks is awake".
                var s = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                s.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                s.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
                    new ElasticEase { Oscillations = 1, Springiness = 7, EasingMode = EasingMode.EaseOut }));
                s.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
                s.KeyFrames.Add(new EasingDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new CubicEase { EasingMode = EasingMode.EaseIn }));

                var lift = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new CubicEase { EasingMode = EasingMode.EaseIn }));

                var op = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                op.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                op.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                op.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
                op.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new CubicEase { EasingMode = EasingMode.EaseIn }));

                var glowBlur = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                glowBlur.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                glowBlur.KeyFrames.Add(new EasingDoubleKeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                glowBlur.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
                var glowOp = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
                glowOp.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                glowOp.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))));
                glowOp.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, s);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, s);
                translate.BeginAnimation(TranslateTransform.YProperty, lift);
                image.BeginAnimation(UIElement.OpacityProperty, op);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowBlur);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOp);

                Task.Delay(760).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CompositionTarget.Rendering -= keep;
                        try { win.Close(); } catch { }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunLaunchAnimation failed: {ex.Message}");
            }
        }

        // Quit: the logo lifts out of the system tray, floats up in a smooth arc to the
        // center growing as it rises, holds a beat with a soft glow, then dissolves - drifting
        // upward while fading and blurring out, like it evaporates. Calm, premium goodbye.
        public static void RunQuitAnimation(Action onComplete)
        {
            try
            {
                var (win, image, scale, rotate, translate, canvas) = CreateAnimationWindow();
                win.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)); // gentle dim
                var screen = SystemParameters.WorkArea;

                const double size = 150;
                double centerX = screen.Width / 2.0;
                double centerY = screen.Height / 2.0;
                // Anchor the image at center; animate the TranslateTransform from the tray to 0.
                Canvas.SetLeft(image, centerX - size / 2);
                Canvas.SetTop(image, centerY - size / 2);
                image.Width = size;
                image.Height = size;
                image.Opacity = 0;

                double trayX = (screen.Width - 30) - centerX;   // start offset: tray corner
                double trayY = (screen.Height - 30) - centerY;

                var glow = new DropShadowEffect { Color = Colors.White, BlurRadius = 0, ShadowDepth = 0, Opacity = 0 };
                image.Effect = glow;

                win.Show();
                var keepQuit = KeepOnTop(win);

                // --- 1. Rise from tray to center in a slow, clearly visible arc (0 -> 900ms) ---
                var riseX = new DoubleAnimation { From = trayX, To = 0, Duration = TimeSpan.FromMilliseconds(900), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var riseY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
                riseY.KeyFrames.Add(new EasingDoubleKeyFrame(trayY, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                riseY.KeyFrames.Add(new EasingDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new SineEase { EasingMode = EasingMode.EaseOut }));
                riseY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), new SineEase { EasingMode = EasingMode.EaseInOut }));
                var grow = new DoubleAnimation { From = 0.16, To = 1.0, Duration = TimeSpan.FromMilliseconds(900), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
                var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(340), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var glowIn = new DoubleAnimation { From = 0, To = 30, BeginTime = TimeSpan.FromMilliseconds(450), Duration = TimeSpan.FromMilliseconds(450) };
                var glowInOp = new DoubleAnimation { From = 0, To = 0.7, BeginTime = TimeSpan.FromMilliseconds(450), Duration = TimeSpan.FromMilliseconds(450) };

                translate.BeginAnimation(TranslateTransform.XProperty, riseX);
                translate.BeginAnimation(TranslateTransform.YProperty, riseY);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                image.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowIn);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowInOp);

                // --- 2. Hold a clear beat at center, then dissolve upward (1300 -> 2000ms) ---
                Task.Delay(1300).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var driftUp = new DoubleAnimation { To = -90, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                        var expand = new DoubleAnimation { To = 1.5, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                        var dissolveFade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(660), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                        var blurOut = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(700) };
                        var winFade = new DoubleAnimation { To = 0, BeginTime = TimeSpan.FromMilliseconds(360), Duration = TimeSpan.FromMilliseconds(340) };

                        // Swap the sharp glow for a blur so it visually "evaporates".
                        var blur = new BlurEffect { Radius = 0 };
                        image.Effect = blur;

                        translate.BeginAnimation(TranslateTransform.YProperty, driftUp);
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, expand);
                        image.BeginAnimation(UIElement.OpacityProperty, dissolveFade);
                        blur.BeginAnimation(BlurEffect.RadiusProperty, blurOut);
                        win.BeginAnimation(UIElement.OpacityProperty, winFade);

                        Task.Delay(740).ContinueWith(__ => Application.Current.Dispatcher.Invoke(() =>
                        {
                            CompositionTarget.Rendering -= keepQuit;
                            try { win.Close(); } catch { }
                            onComplete?.Invoke();
                        }));
                    }
                    catch { CompositionTarget.Rendering -= keepQuit; try { win.Close(); } catch { } onComplete?.Invoke(); }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunQuitAnimation failed (skipping animation): {ex.Message}");
                onComplete?.Invoke();
            }
        }


        // Uninstall farewell: the logo fades in at center with a soft glow, holds a beat,
        // then gently disintegrates - a "Goodbye" line fades in beneath it as the logo
        // dissolves upward (fade + blur + drift). Calm and classy, not a jarring burst.
        public static void RunUninstallAnimation(Action onComplete)
        {
            try
            {
                var (win, image, scale, rotate, translate, canvas) = CreateAnimationWindow();
                win.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)); // darker for a clear farewell
                var screen = SystemParameters.WorkArea;

                const double size = 150;
                double centerX = screen.Width / 2.0;
                double centerY = screen.Height / 2.0;
                image.Width = size;
                image.Height = size;
                image.Opacity = 0;
                Canvas.SetLeft(image, centerX - size / 2);
                Canvas.SetTop(image, centerY - size / 2);

                var glow = new DropShadowEffect { Color = Colors.White, BlurRadius = 0, ShadowDepth = 0, Opacity = 0 };
                image.Effect = glow;

                // "Goodbye" text below the logo, hidden at first.
                var bye = new System.Windows.Controls.TextBlock
                {
                    Text = "Goodbye — thanks for using Racks",
                    Foreground = Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display, Segoe UI"),
                    FontSize = 22,
                    Opacity = 0,
                    TextAlignment = TextAlignment.Center
                };
                bye.Measure(new System.Windows.Size(screen.Width, screen.Height));
                Canvas.SetLeft(bye, centerX - bye.DesiredSize.Width / 2);
                Canvas.SetTop(bye, centerY + size / 2 + 24);
                canvas.Children.Add(bye);

                win.Show();
                var keep = KeepOnTop(win);

                // --- 1. Fade + spring in at center with glow (0 -> 520ms) ---
                var inScale = new DoubleAnimation { From = 0.6, To = 1.0, Duration = TimeSpan.FromMilliseconds(520), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
                var inFade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(360), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var glowIn = new DoubleAnimation { From = 0, To = 26, BeginTime = TimeSpan.FromMilliseconds(250), Duration = TimeSpan.FromMilliseconds(400) };
                var glowInOp = new DoubleAnimation { From = 0, To = 0.6, BeginTime = TimeSpan.FromMilliseconds(250), Duration = TimeSpan.FromMilliseconds(400) };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, inScale);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, inScale);
                image.BeginAnimation(UIElement.OpacityProperty, inFade);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowIn);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowInOp);

                // --- 2. Reveal the goodbye text (520 -> 900ms) ---
                bye.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { From = 0, To = 0.9, BeginTime = TimeSpan.FromMilliseconds(520), Duration = TimeSpan.FromMilliseconds(380), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

                // --- 3. Hold, then dissolve upward: fade + blur + drift + text fade (1500 -> 2200ms) ---
                Task.Delay(1500).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var blur = new BlurEffect { Radius = 0 };
                        image.Effect = blur;
                        var driftUp = new DoubleAnimation { To = -80, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                        var expand = new DoubleAnimation { To = 1.4, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                        var outFade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(640), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                        var blurOut = new DoubleAnimation { To = 22, Duration = TimeSpan.FromMilliseconds(700) };
                        var byeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                        var winFade = new DoubleAnimation { To = 0, BeginTime = TimeSpan.FromMilliseconds(380), Duration = TimeSpan.FromMilliseconds(320) };

                        translate.BeginAnimation(TranslateTransform.YProperty, driftUp);
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, expand);
                        image.BeginAnimation(UIElement.OpacityProperty, outFade);
                        blur.BeginAnimation(BlurEffect.RadiusProperty, blurOut);
                        bye.BeginAnimation(UIElement.OpacityProperty, byeOut);
                        win.BeginAnimation(UIElement.OpacityProperty, winFade);

                        Task.Delay(740).ContinueWith(__ => Application.Current.Dispatcher.Invoke(() =>
                        {
                            CompositionTarget.Rendering -= keep;
                            try { win.Close(); } catch { }
                            onComplete?.Invoke();
                        }));
                    }
                    catch { CompositionTarget.Rendering -= keep; try { win.Close(); } catch { } onComplete?.Invoke(); }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunUninstallAnimation failed (skipping animation): {ex.Message}");
                onComplete?.Invoke();
            }
        }
    }
}
