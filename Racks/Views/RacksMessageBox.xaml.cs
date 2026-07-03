using System;
using System.Windows;

namespace Racks.Views
{
    public partial class RacksMessageBox : Window
    {
        public RacksMessageBox(string message, string title = "Racks")
        {
            InitializeComponent();
            MessageText.Text = message;
            TitleText.Text = title.ToUpper();
            Title = title;
            Topmost = true; // Ensure it shows above everything
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
            DialogResult = true;
            Close();
        }

        public static void Show(string message, string title = "Racks")
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var msgBox = new RacksMessageBox(message, title);
                msgBox.ShowDialog();
            });
        }
    }
}
