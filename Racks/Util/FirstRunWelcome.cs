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
            const double startSize = 128;
            const double endSize = 20;
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
            // Two stacked transforms: scale for the pop-in overshoot, rotate
            // for the half-turn spin during the drop. TransformGroup keeps
            // each animatable independently.
            var scale  = new ScaleTransform(0.35, 0.35);
            var rotate = new RotateTransform(0);
            image.RenderTransform = new TransformGroup { Children = { scale, rotate } };

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

            // Phase 1 (0–450ms): pop in.
            //   Opacity 0→1 with easing,
            //   Scale 0.35→1.05 then settle to 1.0 (overshoot for that "pop").
            image.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new EasingDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                        new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }),
                        new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))),
                    },
                });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new EasingDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                        new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }),
                        new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))),
                    },
                });

            // Phase 2 (450–950ms): hold in the center so the user actually sees
            // it (and reads any taskbar text that flashes by). 500ms is the
            // sweet spot — long enough to register, short enough not to feel
            // sluggish.

            // Phase 3 (950–2300ms): drop to tray. 1350ms of motion gives the
            // arc + bounce + spin time to read clearly.
            var begin = TimeSpan.FromMilliseconds(950);
            var dropDur = TimeSpan.FromMilliseconds(1350);

            win.BeginAnimation(Window.LeftProperty,
                new DoubleAnimation(startX, endX, dropDur)
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            win.BeginAnimation(Window.TopProperty,
                new DoubleAnimation(startY, endY, dropDur)
                { BeginTime = begin, EasingFunction = new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut } });
            image.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(startSize, endSize, dropDur)
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            image.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation(startSize, endSize, dropDur)
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, dropDur)
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

            // Phase 4 (2300–2600ms): fade out at the tray corner.
            image.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(2300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                });

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
