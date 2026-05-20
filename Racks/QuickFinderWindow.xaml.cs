using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Racks.Core;
using Wpf.Ui.Controls;

namespace Racks
{
    // Spotlight-style cross-rack finder. Opened by the global Ctrl+Shift+Space hotkey
    // registered in MainWindow. Lists every FileItem from every open rack, filtered
    // as you type. Enter opens the highlighted item; Esc / clicking-away dismisses.
    public partial class QuickFinderWindow : FluentWindow
    {
        public sealed class Row
        {
            public FileItem Item { get; init; } = null!;
            public string Subtitle { get; init; } = "";
        }

        private readonly List<Row> _all;

        public QuickFinderWindow(InstanceController controller)
        {
            InitializeComponent();
            _all = new List<Row>();
            foreach (var w in controller._subWindows)
            {
                if (w?.FileItems == null) continue;
                string rackName = w.Instance?.TitleText
                                  ?? w.Instance?.Name
                                  ?? "rack";
                foreach (var fi in w.FileItems)
                {
                    if (fi == null) continue;
                    _all.Add(new Row { Item = fi, Subtitle = $"{rackName}  —  {fi.FullPath}" });
                }
            }
            Refresh("");
            Loaded += (_, _) => { QueryBox.Focus(); };
        }

        private void Refresh(string query)
        {
            IEnumerable<Row> filtered = _all;
            if (!string.IsNullOrWhiteSpace(query))
            {
                string q = query.Trim();
                filtered = _all.Where(r =>
                    (r.Item.DisplayName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (r.Item.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (r.Subtitle ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            Results.ItemsSource = filtered.Take(200).ToList();
            if (Results.Items.Count > 0) Results.SelectedIndex = 0;
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh(QueryBox.Text);

        private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Let arrow keys move the selection without leaving the textbox.
            if (e.Key == Key.Down && Results.Items.Count > 0)
            {
                Results.SelectedIndex = Math.Min(Results.SelectedIndex + 1, Results.Items.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && Results.Items.Count > 0)
            {
                Results.SelectedIndex = Math.Max(Results.SelectedIndex - 1, 0);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { this.Close(); e.Handled = true; }
        }

        private void Window_Deactivated(object sender, EventArgs e) => this.Close();

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void OpenSelected()
        {
            if (Results.SelectedItem is not Row row) return;
            try
            {
                Process.Start(new ProcessStartInfo(row.Item.FullPath!) { UseShellExecute = true });
                this.Close();
            }
            catch (Exception ex) { Debug.WriteLine($"QuickFinder open failed: {ex.Message}"); }
        }
    }
}
