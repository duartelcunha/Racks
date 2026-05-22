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
using Application = System.Windows.Application;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

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

        public static void ShowIfFirstRun(RegistryHelper reg)
        {
            try
            {
                if (reg.KeyExistsRoot(MarkerKey)) return;
                reg.WriteToRegistryRoot(MarkerKey, true);
            }
            catch { return; }

            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { RunDropAnimation(); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
        }

        private static void RunDropAnimation()
        {
            var screen = SystemParameters.WorkArea;
            const double startSize = 96;
            const double endSize = 18;
            double startX = screen.Left + (screen.Width  - startSize) / 2;
            double startY = screen.Top  + (screen.Height - startSize) / 2;
            double endX   = screen.Right  - endSize - 12;
            double endY   = screen.Bottom - endSize - 6;

            var image = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/ico.png", UriKind.Absolute)),
                Width  = startSize,
                Height = startSize,
                Opacity = 0,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var rotate = new RotateTransform(0);
            image.RenderTransform = rotate;

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                Width = startSize,
                Height = startSize,
                Left = startX,
                Top  = startY,
                Content = image,
            };
            win.Show();

            // Phase 1: pop in (0–250ms). Fade in + slight overshoot scale.
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            image.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Phase 2: drop (350–1350ms). Concurrent translate + shrink + rotate.
            //   X: linear-ish ease.
            //   Y: bounce-out so it lands with a slight overshoot near the tray.
            //   Size: ease to 18px.
            //   Rotation: 180° spin (the "hoop").
            var begin = TimeSpan.FromMilliseconds(350);
            var dropDur = TimeSpan.FromMilliseconds(950);

            var moveX = new DoubleAnimation(startX, endX, dropDur)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var moveY = new DoubleAnimation(startY, endY, dropDur)
            { BeginTime = begin, EasingFunction = new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut } };
            var shrinkW = new DoubleAnimation(startSize, endSize, dropDur)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var shrinkH = new DoubleAnimation(startSize, endSize, dropDur)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var spin = new DoubleAnimation(0, 360, dropDur)
            { BeginTime = begin };

            win.BeginAnimation(Window.LeftProperty, moveX);
            win.BeginAnimation(Window.TopProperty,  moveY);
            image.BeginAnimation(FrameworkElement.WidthProperty,  shrinkW);
            image.BeginAnimation(FrameworkElement.HeightProperty, shrinkH);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);

            // Phase 3: fade out at the tray (1350–1600ms).
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = TimeSpan.FromMilliseconds(1350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            image.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            // Close and fire toast just after the animation settles.
            Task.Delay(1700).ContinueWith(_ =>
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
