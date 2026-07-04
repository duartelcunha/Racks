using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Racks.Views
{
    public partial class StartupAnimationWindow : Window
    {
        public StartupAnimationWindow()
        {
            InitializeComponent();
            this.Loaded += StartupAnimationWindow_Loaded;
        }

        private async void StartupAnimationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Pop-in scale
            var scaleAnim = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }
            };

            // 2. Roll in place
            var rotateAnim = new DoubleAnimation
            {
                To = 720,
                Duration = TimeSpan.FromSeconds(1.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
            LogoRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotateAnim);

            await Task.Delay(1500);

            // 3. Fall into the system tray
            var workingArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            double targetX = (workingArea.Right - this.Left) - 75; // 75 is half icon width
            double targetY = (workingArea.Bottom - this.Top) - 75;

            var fallXAnim = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var fallYAnim = new DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };

            var fadeOut = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleDown = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            LogoTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, fallXAnim);
            LogoTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, fallYAnim);
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleDown);
            LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleDown);

            await Task.Delay(600);
            this.Close();
        }
    }
}
