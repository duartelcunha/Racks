using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Racks.Views
{
    // The single message/confirm dialog for the whole app, so every prompt shares one
    // premium look (gradient card, pill buttons) and motion (scale + fade in via
    // WindowFade). Use Show(...) for a simple notice, or await Confirm(...) for a
    // yes/no decision.
    public partial class RacksMessageBox : Window
    {
        public bool Confirmed { get; private set; }

        public RacksMessageBox(string message, string title = "Racks",
                               string okText = "OK", string? cancelText = null)
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            MessageText.Text = message;
            TitleText.Text = title.ToUpper();
            Title = title;
            OkButton.Content = okText;
            if (cancelText != null)
            {
                SecondaryButton.Content = cancelText;
                SecondaryButton.Visibility = Visibility.Visible;
            }
            Topmost = true; // Ensure it shows above everything

            // Gentle scale-in to match the fade WindowFade drives on Opacity.
            Loaded += (_, _) =>
            {
                var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
                var anim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromSeconds(0.22)) { EasingFunction = ease };
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
                RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
            };
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        // Simple notice with a single OK button.
        public static void Show(string message, string title = "Racks")
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var msgBox = new RacksMessageBox(message, title);
                msgBox.ShowDialog();
            });
        }

        // Yes/no confirmation. Returns true if the user chose the confirm button.
        public static bool Confirm(string message, string title = "Racks",
                                   string confirmText = "OK", string cancelText = "Cancel")
        {
            bool result = false;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var box = new RacksMessageBox(message, title, confirmText, cancelText);
                box.ShowDialog();
                result = box.Confirmed;
            });
            return result;
        }
    }
}
