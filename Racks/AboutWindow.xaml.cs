using System.Reflection;
using Wpf.Ui.Controls;

namespace Racks
{
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow(double top, double left, double height, double width)
        {
            InitializeComponent();
            this.Width = width / 2;
            this.Left = left + (width - this.Width) / 2;
            this.Top = top + (height - this.Height) / 2;

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
        }
    }
}