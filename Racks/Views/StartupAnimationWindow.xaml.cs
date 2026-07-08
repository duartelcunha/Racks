using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Racks.Views
{
    // First-run "welcome" animation: the logo makes a confident entrance - springs up from
    // small with a spin, holds at center with a soft glow pulse, then sweeps in a smooth arc
    // down to the system tray while shrinking and fading. Celebratory, plays once per machine.
    public partial class StartupAnimationWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

        public StartupAnimationWindow()
        {
            InitializeComponent();
            var wa = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = wa.Left; this.Top = wa.Top; this.Width = wa.Width; this.Height = wa.Height;
            this.Loaded += StartupAnimationWindow_Loaded;
        }

        private void ForceTop()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private async void StartupAnimationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Activate();
            ForceTop();
            EventHandler keepTop = (_, _) => ForceTop();
            CompositionTarget.Rendering += keepTop;

            // Soft glow behind the logo (grows as it settles at center).
            var glow = new DropShadowEffect { Color = Colors.White, BlurRadius = 0, ShadowDepth = 0, Opacity = 0.0 };
            AppLogo.Effect = glow;

            var ease = new Func<EasingMode, IEasingFunction>(m => new CubicEase { EasingMode = m });

            // --- 1. Entrance: spring up from small with a spin (0 -> 650ms) ---
            var scaleIn = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(650) };
            scaleIn.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleIn.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)),
                new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }));

            var spinIn = new DoubleAnimation
            {
                From = -140, To = 0, Duration = TimeSpan.FromMilliseconds(650),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease(EasingMode.EaseOut) };
            var glowUp = new DoubleAnimation { From = 0, To = 22, BeginTime = TimeSpan.FromMilliseconds(300), Duration = TimeSpan.FromMilliseconds(400), EasingFunction = ease(EasingMode.EaseOut) };
            var glowOpUp = new DoubleAnimation { From = 0, To = 0.55, BeginTime = TimeSpan.FromMilliseconds(300), Duration = TimeSpan.FromMilliseconds(400) };

            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            LogoRotate.BeginAnimation(RotateTransform.AngleProperty, spinIn);
            AppLogo.BeginAnimation(OpacityProperty, fadeIn);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowUp);
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOpUp);

            // --- 2. Hold + gentle breath pulse (650 -> 1150ms) ---
            await Task.Delay(650);
            var breathe = new DoubleAnimation { From = 1.0, To = 1.06, Duration = TimeSpan.FromMilliseconds(500), AutoReverse = true, EasingFunction = ease(EasingMode.EaseInOut) };
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            await Task.Delay(500);

            // --- 3. Sweep to the tray in a smooth arc, shrinking + fading (1150 -> 1900ms) ---
            var wa = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            double cx = wa.Width / 2.0, cy = wa.Height / 2.0;
            double targetX = (wa.Width - 34) - cx;
            double targetY = (wa.Height - 34) - cy;

            var moveX = new DoubleAnimation { To = targetX, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            // Arc: dip slightly, then drop - via a keyframed Y with a control lift.
            var moveY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new SineEase { EasingMode = EasingMode.EaseOut }));
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(targetY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new QuarticEase { EasingMode = EasingMode.EaseIn }));

            var shrink = new DoubleAnimation { To = 0.1, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var spinOut = new DoubleAnimation { To = 200, Duration = TimeSpan.FromMilliseconds(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var fadeOut = new DoubleAnimation { To = 0.0, BeginTime = TimeSpan.FromMilliseconds(400), Duration = TimeSpan.FromMilliseconds(300), EasingFunction = ease(EasingMode.EaseIn) };
            var glowDown = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300) };

            LogoTranslate.BeginAnimation(TranslateTransform.XProperty, moveX);
            LogoTranslate.BeginAnimation(TranslateTransform.YProperty, moveY);
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            LogoRotate.BeginAnimation(RotateTransform.AngleProperty, spinOut);
            AppLogo.BeginAnimation(OpacityProperty, fadeOut);
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowDown);

            await Task.Delay(720);
            CompositionTarget.Rendering -= keepTop;
            this.Close();
        }
    }
}
