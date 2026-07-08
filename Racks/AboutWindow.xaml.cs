using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;

namespace Racks
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(double top, double left, double height, double width)
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            this.Width = 270;

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = $"Version {v?.Major}.{v?.Minor}.{v?.Build}";

            // Center over the settings window that opened it (roughly), once we know our size.
            Loaded += (_, _) =>
            {
                this.Left = left + (width - this.ActualWidth) / 2;
                this.Top = top + (height - this.ActualHeight) / 2;

                var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
                var anim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromSeconds(0.22)) { EasingFunction = ease };
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
            };
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
