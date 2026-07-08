using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Racks
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) => Racks.Util.WindowPlacement.CenterOnCursorScreen(this);
            this.KeyDown += (s, e) => { if (e.Key == Key.Escape) this.Close(); };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
