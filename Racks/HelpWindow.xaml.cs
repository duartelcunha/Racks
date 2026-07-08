using System.Windows.Input;
using Wpf.Ui.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Racks
{
    public partial class HelpWindow : FluentWindow
    {
        public HelpWindow()
        {
            InitializeComponent();
            Racks.Util.WindowFade.Attach(this);
            this.KeyDown += (s, e) => { if (e.Key == Key.Escape) this.Close(); };
        }
    }
}
