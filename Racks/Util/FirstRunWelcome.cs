using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Toolkit.Uwp.Notifications;
// The Racks project pulls in both WPF and WinForms namespaces. Alias to the
// WPF types so the unqualified names below resolve unambiguously.
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Application = System.Windows.Application;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace Racks.Util
{
    // First-launch welcome. A 96px Racks icon pops in at the center of the
    // primary screen, rotates a half-turn, drops along a slight arc to the
    // bottom-right corner (system-tray area), shrinks to ~16px and fades out.
    // A toast follows ~200ms later with a short "Racks is in your tray" note.
    //
    // This runs at most once per machine — gated by HKCU\SOFTWARE\Racks
    // \FirstRunWelcomeShownV1. Failures are swallowed; the app must not crash
    // because the welcome flow misbehaved.
    public static class FirstRunWelcome
    {
        private const string MarkerKey = "FirstRunWelcomeShownV1";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Force a window to the very top of the Z-order, above everything
        // including topmost windows of other apps. Bypasses WPF's polite
        // "Topmost = true" which Windows 11 sometimes ignores when another
        // foreground app is active (anti-focus-steal heuristic).
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

        public static void ShowIfFirstRun(RegistryHelper reg)
        {
            try
            {
                if (reg.KeyExistsRoot(MarkerKey)) { Log("marker present — skipping"); return; }
                reg.WriteToRegistryRoot(MarkerKey, true);
                Log("marker set, will play welcome");
            }
            catch (Exception ex) { Log($"marker check failed: {ex.Message}"); return; }

            // Wait until everything (MainWindow, tray icon, rack windows) is
            // painted before showing the overlay. DispatcherPriority.Loaded
            // alone fires before some HWND paints complete, leaving the
            // transparent welcome window invisible against the desktop.
            Task.Delay(600).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { RunDropAnimation(); Log("animation kicked off"); }
                    catch (Exception ex) { Log($"animation failed: {ex}"); }
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
        }

        // Lightweight debug log so we can confirm whether the welcome flow
        // actually ran on a given launch. Lives in %TEMP%\Racks-welcome.log.
        private static readonly object _logLock = new object();
        private static string LogPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Racks-welcome.log");
        private static void Log(string msg)
        {
            try
            {
                lock (_logLock)
                {
                    System.IO.File.AppendAllText(LogPath,
                        $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }

        private static void RunDropAnimation()
        {

            // Animate inside a single fullscreen click-through overlay window
            // rather than animating Window.Left/Top — WPF's window animations
            // are unreliable on transparent borderless windows (the size/pos
            // dependency properties don't always animate, and toolwindows
            // sometimes don't repaint between frames). A Canvas + transforms
            // is rock-solid.
            var screen = SystemParameters.WorkArea;
            const double startSize = 128;
            const double endSize = 22;
            double centerX = screen.Width  / 2;
            double centerY = screen.Height / 2;
            double trayX   = screen.Width  - endSize - 14;
            double trayY   = screen.Height - endSize - 8;

            BitmapImage src;
            try
            {
                src = new BitmapImage();
                src.BeginInit();
                src.UriSource = new Uri("pack://application:,,,/ico.png", UriKind.Absolute);
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.EndInit();
                src.Freeze();
                Log($"bitmap loaded {src.PixelWidth}x{src.PixelHeight}");
            }
            catch (Exception ex)
            {
                Log($"bitmap load failed: {ex.Message}");
                ShowToast();
                return;
            }

            var image = new Image
            {
                Source = src,
                Width  = startSize,
                Height = startSize,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0,
            };
            // Place the icon at center via Canvas.SetLeft/Top so animating a
            // TranslateTransform moves it relative to that.
            Canvas.SetLeft(image, centerX - startSize / 2);
            Canvas.SetTop (image, centerY - startSize / 2);

            var translate = new TranslateTransform(0, 0);
            var scale     = new ScaleTransform(0.35, 0.35);
            var rotate    = new RotateTransform(0);
            image.RenderTransform = new TransformGroup
            {
                Children = { scale, rotate, translate }
            };

            var canvas = new Canvas { Background = Brushes.Transparent };
            canvas.Children.Add(image);

            var win = new Window
            {
                // Skip the app's global Wpf.Ui Window style — it replaces the
                // window's ControlTemplate with one that has its own Border/
                // titlebar/content presenter, which clobbers a raw Canvas
                // content. Without this, the window IS shown but the icon
                // never makes it to the screen.
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
                Top  = screen.Top,
                Width  = screen.Width,
                Height = screen.Height,
                Content = canvas,
            };
            win.Show();
            ForceTopmost(win);
            Log($"window shown {win.Left},{win.Top} {win.Width}x{win.Height}");

            // Compute drop deltas. End position is the tray corner with the
            // icon shrunk to endSize, so translation = trayCorner - origin.
            double dx = (trayX - endSize / 2) - centerX;
            double dy = (trayY - endSize / 2) - centerY;
            double endScale = endSize / startSize;

            // ONE keyframe animation per property, covering the whole timeline.
            // BeginAnimation replaces any prior animation on the same property,
            // so separate calls for pop-in + drop would cancel each other.
            //
            //   t=0–450ms    : pop in
            //   t=450–950ms  : hold at center
            //   t=950–2300ms : drop to tray (translate + spin + shrink)
            //   t=2300–2600ms: fade out (handled separately on Opacity)

            var scaleX = new DoubleAnimationUsingKeyFrames();
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(endScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            scaleX.Duration = TimeSpan.FromMilliseconds(2300);
            scaleX.FillBehavior = FillBehavior.HoldEnd;

            var scaleY = new DoubleAnimationUsingKeyFrames();
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(endScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            scaleY.Duration = TimeSpan.FromMilliseconds(2300);
            scaleY.FillBehavior = FillBehavior.HoldEnd;

            var transX = new DoubleAnimationUsingKeyFrames();
            transX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            transX.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            transX.KeyFrames.Add(new EasingDoubleKeyFrame(dx, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            transX.Duration = TimeSpan.FromMilliseconds(2300);
            transX.FillBehavior = FillBehavior.HoldEnd;

            var transY = new DoubleAnimationUsingKeyFrames();
            transY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            transY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            transY.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300)),
                new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut }));
            transY.Duration = TimeSpan.FromMilliseconds(2300);
            transY.FillBehavior = FillBehavior.HoldEnd;

            var spin = new DoubleAnimationUsingKeyFrames();
            spin.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            spin.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            spin.KeyFrames.Add(new EasingDoubleKeyFrame(360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            spin.Duration = TimeSpan.FromMilliseconds(2300);
            spin.FillBehavior = FillBehavior.HoldEnd;

            var imgOpacity = new DoubleAnimationUsingKeyFrames();
            imgOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            imgOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            imgOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
            imgOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            imgOpacity.Duration = TimeSpan.FromMilliseconds(2600);
            imgOpacity.FillBehavior = FillBehavior.HoldEnd;

            var winOpacity = new DoubleAnimationUsingKeyFrames();
            winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
            winOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            winOpacity.Duration = TimeSpan.FromMilliseconds(2600);
            winOpacity.FillBehavior = FillBehavior.HoldEnd;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            translate.BeginAnimation(TranslateTransform.XProperty, transX);
            translate.BeginAnimation(TranslateTransform.YProperty, transY);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            image.BeginAnimation(UIElement.OpacityProperty, imgOpacity);
            win.BeginAnimation(UIElement.OpacityProperty, winOpacity);

            // Close and fire toast just after the animation settles.
            Task.Delay(2700).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try { win.Close(); } catch { }
                    try { ShowToast(); } catch { }
                });
            });
        }

        private static void ShowToast()
        {
            new ToastContentBuilder()
                .AddText("Racks is in your system tray")
                .AddText("Right-click the tray icon to create a rack, open the quick finder (Ctrl+Shift+Space), or change settings.")
                .Show();
        }
    }
}
