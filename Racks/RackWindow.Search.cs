#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8622, CS8625
using Racks.ViewModels;
using Racks.Core;
using Racks.Properties;
using Racks.Shaders;
using Racks.Util;
using static Racks.Util.ThemePresets;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using WindowsDesktop;
using Wpf.Ui.Controls;
using static Racks.Util.Interop;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using File = System.IO.File;
using ListView = Wpf.Ui.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Path = System.IO.Path;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
namespace Racks
{
    public partial class RackWindow
    {
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_isRenaming)
            {
                return;
            }
            // Arrow keys + Enter navigate filtered items when the search box is active.
            // Esc clears search like before.
            if (Search.Visibility == Visibility.Visible
                && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter))
            {
                NavigateSearchResults(e.Key);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape || !_mouseIsOver)
            {
                FilterTextBox.Text = null;
            }
            else
            {
                Search.Opacity = 0;
                Search.Visibility = Visibility.Visible;
            }
            FilterTextBox.Focus();
            return;
        }

        // Walk through the currently-visible (post-filter) items and let the user
        // open one with Enter. Reuses the existing IsSelected highlight so no new
        // visuals are needed.
        private void NavigateSearchResults(Key key)
        {
            if (_collectionView == null) return;
            var visible = new List<FileItem>();
            foreach (var obj in _collectionView)
                if (obj is FileItem fi) visible.Add(fi);
            if (visible.Count == 0) return;

            int current = visible.FindIndex(fi => fi.IsSelected);
            int next;
            if (key == Key.Enter)
            {
                var target = current >= 0 ? visible[current] : visible[0];
                try
                {
                    Process.Start(new ProcessStartInfo(target.FullPath!) { UseShellExecute = true });
                    FilterTextBox.Text = null;
                }
                catch (Exception ex) { Debug.WriteLine($"Search open failed: {ex.Message}"); }
                return;
            }
            if (current < 0) next = 0;
            else if (key == Key.Down) next = Math.Min(current + 1, visible.Count - 1);
            else next = Math.Max(current - 1, 0);

            foreach (var fi in visible) fi.IsSelected = false;
            visible[next].IsSelected = true;
        }

        private async void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilterTextBox.Text))
            {
                Search.Visibility = Visibility.Collapsed;
                title.Visibility = Visibility.Visible;
            }
            else if (_mouseIsOver)
            {
                Search.Opacity = 1;
                Search.Visibility = Visibility.Visible;
                Search.Margin = PathToBackButton.Visibility == Visibility.Visible ?
                    new Thickness(PathToBackButton.Width + 4, 0, 0, 0) : new Thickness(0, 0, 0, 0);
                // On a collapsed rack the title bar is all that's visible, so keep the name
                // showing even while searching (there's no item list to filter here anyway).
                if (!_isMinimized) title.Visibility = Visibility.Collapsed;
            }


            if (_collectionView == null)
                return;

            string filter = _mouseIsOver ? FilterTextBox.Text : "";
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                await Task.Delay(50, token);
                _selectedItems.Clear();
                if (!_contextMenuIsOpen)
                {
                    foreach (var fileItem in FileItems)
                    {
                        fileItem.IsSelected = false;
                        fileItem.Background = Brushes.Transparent;
                    }
                    _selectedItems.Clear();
                }
                string regexPattern = Regex.Escape(filter).Replace("\\*", ".*"); // Escape other regex special chars and replace '*' with '.*'

                var filteredItems = await Task.Run(() =>
                {
                    return new Predicate<object>(item =>
                    {
                        if (token.IsCancellationRequested) return false;
                        var fileItem = item as FileItem;
                        return string.IsNullOrWhiteSpace(filter) ||
                               Regex.IsMatch(fileItem.Name!, regexPattern, RegexOptions.IgnoreCase);
                    });
                }, token);

                if (!token.IsCancellationRequested)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _collectionView.Filter = filteredItems;
                        _collectionView.Refresh();
                    });
                }
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
