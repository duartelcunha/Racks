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
        private void SetShellMenuHandlers(Action? closed, Action? rename)
            => (_menuHandlers ??= new ShellMenuHandlers(scm)).Set(closed, rename);
        public Instance Instance { get; set; }
        public string _currentFolderPath;
        private readonly Racks.Services.FileWatcherService _fileWatcherService = new Racks.Services.FileWatcherService();

        public RackViewModel ViewModel { get; }
        public System.Collections.ObjectModel.ObservableCollection<FileItem> FileItems => ViewModel.FileItems;


        public bool VirtualDesktopSupported;
        IntPtr hwnd;
        IntPtr shellView = IntPtr.Zero;

        private bool _dragdropIntoFolder;
        public int _itemPerRow;
        public int ItemPerRow
        {
            get => _itemPerRow;
            set
            {
                if (_itemPerRow != value)
                {
                    _itemPerRow = value;
                }
            }
        }

        private void ItemContainerGenerator_StatusChanged(object sender, EventArgs e)
        {
            if (FileListView.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                foreach (var item in FileListView.Items)
                {
                    var container = FileListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                    // StatusChanged fires repeatedly; wire each container only once.
                    if (container != null && _wiredContainers.TryGetValue(container, out _)) continue;
                    if (container != null)
                    {
                        _wiredContainers.Add(container, true);
                        container.MouseEnter += ListViewItem_MouseEnter;
                        container.MouseLeave += ListViewItem_MouseLeave;
                        container.Selected += ListViewItem_Selected;
                        container.Unselected += ListViewItem_Unselected;
                        container.PreviewMouseUp += FileListView_PreviewMouseUp;
                        container.MouseDoubleClick += FileListView_DoubleClick;
                        container.PreviewMouseDown += FileListView_MouseLeftButtonDown;
                        container.MouseRightButtonUp += FileListView_MouseRightButtonUp;
                    }
                }
            }
        }

        private void FileListView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _canAutoClose = false;
            if (sender is not ListView listView)
                return;
            var point = e.GetPosition(listView);
            var element = listView.InputHitTest(point) as DependencyObject;
            while (element != null && element is not ListViewItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            if (element is ListViewItem item && item.DataContext is FileItem clickedItem)
            {
                if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount != 2)
                {
                    DataObject data = new DataObject(DataFormats.FileDrop, new string[] { clickedItem.FullPath! });
                    string dragPath = clickedItem.FullPath!;
                    string dragName = clickedItem.Name;
                    bool desktopHadFileBefore = DesktopHasFile(dragName);
                    Task.Run(() =>
                    {
                        Thread.Sleep(5);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var effect = DragDrop.DoDragDrop(listView, data, DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);
                            // Same desktop-rack duplicate reconciliation as the tile drag path.
                            if (effect != DragDropEffects.None)
                            {
                                Racks.Util.Interop.GetCursorPos(out Racks.Util.Interop.POINT dropPt);
                                HandleDesktopRackDragOut(dragPath, dragName, desktopHadFileBefore, dropPt);
                            }
                        });
                    });
                }
            }
        }

        private void FileListView_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView listView)
                return;
            var point = e.GetPosition(listView);
            var element = listView.InputHitTest(point) as DependencyObject;
            while (element != null && element is not ListViewItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            if (element is ListViewItem item && item.DataContext is FileItem clickedItem)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(clickedItem.FullPath!) { UseShellExecute = true });
                }
                catch
                {
                }
            }
        }

        private void FileListView_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _canAutoClose = false;
            if (sender is not ListView listView)
                return;
            var point = e.GetPosition(listView);
            var element = listView.InputHitTest(point) as DependencyObject;
            while (element != null && element is not ListViewItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            if (element is ListViewItem item && item.DataContext is FileItem clickedFileItem)
            {
                _lastRightClickedPath = clickedFileItem.FullPath;
                var windowHelper = new WindowInteropHelper(this);
                FileInfo[] files = new FileInfo[1];
                files[0] = new FileInfo(clickedFileItem.FullPath!);
                Point cursorPosition = System.Windows.Forms.Cursor.Position;
                System.Windows.Point wpfPoint = new System.Windows.Point(cursorPosition.X, cursorPosition.Y);
                Point drawingPoint = new Point((int)wpfPoint.X, (int)wpfPoint.Y);
                _contextMenuIsOpen = true;
                SetShellMenuHandlers(() =>
                {
                    _contextMenuIsOpen = false;
                }, null);
                scm.ShowContextMenu(windowHelper.Handle, files, drawingPoint, (clickedFileItem.FullPath! == _currentFolderPath), RackProtectsFromDelete);
            }
        }

        private void OnRackDeleteBlocked()
        {
            if (_deleteBlockedNoticeOpen) return;
            _deleteBlockedNoticeOpen = true;
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Racks.Views.RacksMessageBox.Show(
                        "Files inside a rack are protected and can't be deleted here.\n\nDrag the item out to your desktop first if you want to delete it, or remove the whole rack to send everything back.",
                        "Protected");
                }
                finally { _deleteBlockedNoticeOpen = false; }
            });
        }

        private void OnOpenInExplorerRequested()
        {
            var path = _lastRightClickedPath;
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                    {
                        // Open the folder with the item selected.
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    }
                    else if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                    {
                        Process.Start(new ProcessStartInfo(_currentFolderPath) { UseShellExecute = true });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Open in Explorer failed: {ex.Message}"); }
            });
        }

        private void FileItem_LeftMouseButtonDown(object sender, MouseButtonEventArgs e)
        {
            var clickedFileItem = (sender as Border)?.DataContext as FileItem;

            if (clickedFileItem != null)
            {
                if (!(Keyboard.IsKeyDown(Key.LeftCtrl)
                || Keyboard.IsKeyDown(Key.RightCtrl)
                || Keyboard.IsKeyDown(Key.LeftShift)
                || Keyboard.IsKeyDown(Key.RightShift)))
                {
                    clickedFileItem.IsSelected = true;
                    if (!_contextMenuIsOpen)
                    {
                        _selectedItems.Clear();

                        foreach (var fileItem in FileItems)
                        {
                            if (fileItem != clickedFileItem)
                            {
                                fileItem.IsSelected = false;
                                fileItem.Background = Brushes.Transparent;
                            }
                        }
                    }
                }
                else
                {
                    clickedFileItem.IsSelected = !clickedFileItem.IsSelected;
                }
                if (clickedFileItem.IsSelected && !_selectedItems.Contains(clickedFileItem))
                {
                    _selectedItems.Add(clickedFileItem);
                }
            }
            if (e.ClickCount == 2 && sender is Border border && border.DataContext is FileItem clickedItem)
            {
                try
                {
                    if (Instance.FolderOpenInsideFrame && clickedItem.IsFolder)
                    {
                        _currentFolderPath = clickedItem.FullPath;
                        PathToBackButton.Visibility = _currentFolderPath == Instance.Folder
                            ? Visibility.Collapsed : Visibility.Visible;
                        Search.Margin = PathToBackButton.Visibility == Visibility.Visible ?
                                        new Thickness(PathToBackButton.Width + 4, 0, 0, 0) : new Thickness(0, 0, 0, 0);
                        InitializeFileWatchers();
                        FileItems.Clear();
                        LoadFiles(clickedItem.FullPath);
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(clickedItem.FullPath!) { UseShellExecute = true });
                    }
                    if (Instance.LastAccesedToFirstRow)
                    {
                        var fileId = GetFileId(clickedFileItem.FullPath!).ToString();
                        var newList = new List<string>(Instance.LastAccessedFiles);
                        newList.Remove(fileId);
                        newList.Insert(0, fileId);
                        Instance.LastAccessedFiles = newList;
                        var wrapPanel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
                        if (wrapPanel != null)
                        {
                            double itemWidth = wrapPanel.ItemWidth;
                            ItemPerRow = (int)((this.Width) / itemWidth);
                        }
                        FirstRowByLastAccessed(FileItems, Instance.LastAccessedFiles, ItemPerRow);
                    }
                }
                catch //(Exception ex)
                {
                    //  MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            // Outgoing OLE drag for the grid (WrapPanel/AnimatedTilePanel) layout
            // moved out of here. The panel decides between long-press (in-rack
            // reorder) and immediate drag (outgoing). When it picks outgoing it
            // fires OutgoingDragRequested → OnTilePanelOutgoingDragRequested,
            // which starts DragDrop.DoDragDrop. Keeping DoDragDrop here would
            // capture the mouse on every mousedown and starve both the long-press
            // timer and the in-panel reorder gesture.
            if (clickedFileItem != null && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                if (!_selectedItems.Contains(clickedFileItem))
                {

                    if (clickedFileItem.IsSelected)
                    {
                        _selectedItems.Add(clickedFileItem);
                    }
                    else
                    {
                        _selectedItems.Remove(clickedFileItem);
                    }
                }
            }
            if (clickedFileItem != null && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                && !((Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))))
            {
                int clickedIndex = FileItems.IndexOf(clickedFileItem);
                int minSelectedIndex = int.MaxValue;
                int maxSelectedIndex = -1;
                for (int i = 0; i < FileItems.Count; i++)
                {
                    if (!FileItems[i].IsSelected) continue;
                    if (i == clickedIndex) continue;
                    maxSelectedIndex = i;
                    if (minSelectedIndex > i) minSelectedIndex = i;
                }
                int selectToIndex = Math.Abs(clickedIndex - minSelectedIndex) <= Math.Abs(clickedIndex - maxSelectedIndex)
                                    ? minSelectedIndex
                                    : maxSelectedIndex;

                int start = Math.Min(clickedIndex, selectToIndex);
                int end = Math.Max(clickedIndex, selectToIndex);
                _selectedItems.Clear();

                for (int i = 0; i < FileItems.Count; i++)
                {
                    if (start <= i && i <= end)
                    {
                        FileItems[i].IsSelected = true;
                        _selectedItems.Add(FileItems[i]);
                    }
                    else
                    {
                        FileItems[i].IsSelected = false;
                    }
                }
            }
        }

        private void FileItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            _canAutoClose = false;
            var clickedFileItem = (sender as Border)?.DataContext as FileItem;

            if (clickedFileItem != null)
            {
                clickedFileItem.IsSelected = true;
                if (_selectedItems.Count <= 1 && !_selectedItems.Contains(clickedFileItem))
                {
                    _selectedItems.Clear();
                    foreach (var fileItem in FileItems)
                    {
                        if (fileItem != clickedFileItem)
                        {
                            fileItem.IsSelected = false;
                        }
                    }
                    _selectedItems.Add(clickedFileItem);
                }
            }

            if (sender is Border border && border.DataContext is FileItem clickedItem)
            {
                _lastRightClickedPath = clickedItem.FullPath;
                var windowHelper = new WindowInteropHelper(this);


                FileInfo[] files = new FileInfo[1];
                files[0] = new FileInfo(clickedItem.FullPath!);

                Point cursorPosition = System.Windows.Forms.Cursor.Position;
                System.Windows.Point wpfPoint = new System.Windows.Point(cursorPosition.X, cursorPosition.Y);
                Point drawingPoint = new Point((int)wpfPoint.X, (int)wpfPoint.Y);
                _contextMenuIsOpen = true;
                Action renameHandler = null;
                renameHandler = () =>
                {
                    try
                    {
                        if (clickedFileItem != null)
                        {
                            if (_itemCurrentlyRenaming != null)
                            {
                                _itemCurrentlyRenaming.IsRenaming = false;
                            }

                            _itemCurrentlyRenaming = clickedFileItem;
                            _itemCurrentlyRenaming.IsRenaming = true;
                            _isRenamingFromContextMenu = true;
                            DependencyObject container = FileWrapPanel.ItemContainerGenerator.ContainerFromItem(_itemCurrentlyRenaming);

                            var renameTextBox = FindParentOrChild<TextBox>(container);

                            renameTextBox!.Text = _itemCurrentlyRenaming.Name;
                            _isRenaming = true;
                            renameTextBox.Focus();

                            var text = renameTextBox.Text;
                            var dotIndex = text.LastIndexOf('.');
                            if (dotIndex <= 0) renameTextBox.SelectAll();
                            else renameTextBox.Select(0, dotIndex);
                            scm.ContextMenuRenameSelected -= renameHandler;
                        }
                    }
                    catch { }
                };
                SetShellMenuHandlers(() =>
                {
                    _selectedItems.Clear();
                    foreach (var item in FileItems)
                    {
                        item.IsSelected = false;
                    }
                    _contextMenuIsOpen = false;
                }, renameHandler);
                if (clickedFileItem != null)
                {
                    if (_selectedItems.Count > 0 && _selectedItems.Contains(clickedItem))
                    {
                        files = _selectedItems.Where(item => item.IsSelected).Select(item => new FileInfo(item.FullPath!)).ToArray();
                    }
                    else
                    {
                        _selectedItems.Clear();
                    }
                    if (_itemCurrentlyRenaming != null)
                    {
                        _itemCurrentlyRenaming.IsRenaming = false;
                    }
                    if (_selectedItems.Count > 1)
                    {
                        scm.ShowContextMenu(windowHelper.Handle, files, drawingPoint, true, RackProtectsFromDelete);
                    }
                    else
                    {
                        scm.ShowContextMenu(windowHelper.Handle, files, drawingPoint, (clickedFileItem!.FullPath == _currentFolderPath), RackProtectsFromDelete);
                    }
                }
            }
        }

        private T? FindParentOrChild<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T targetElement) return targetElement;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is T childElement) return childElement;

                var nestedChild = FindParentOrChild<T>(child);
                if (nestedChild != null) return nestedChild;
            }
            return FindParent<T>(element);
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private void ListViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is FileItem fileItem)
            {
                fileItem.IsSelected = true;
            }
        }

        private void ListViewItem_Unselected(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is FileItem fileItem)
            {
                fileItem.IsSelected = false;
                var sourceElement = e.OriginalSource as DependencyObject;
                var currentBorder = sourceElement as Border ?? FindParentOrChild<Border>(sourceElement);
                if (currentBorder != null) currentBorder.Background = Brushes.Transparent;
            }
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e)
        {
            _dropIntoFolderPath = "";

            if (sender is ListViewItem item && item.DataContext is FileItem fileItem)
            {
                _itemUnderCursor = fileItem;
                var sourceElement = e.OriginalSource as DependencyObject;
                var currentBorder = sourceElement as Border ?? FindParentOrChild<Border>(sourceElement);

                if (currentBorder != null)
                {
                    if (!fileItem.IsSelected)
                    {
                        currentBorder.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
                    }
                }
            }
        }

        private void ListViewItem_MouseLeave(object sender, MouseEventArgs e)
        {
            _dropIntoFolderPath = "";
            if (sender is ListViewItem item && item.DataContext is FileItem fileItem)
            {
                _itemUnderCursor = null;
                if (!_isRenamingFromContextMenu)
                {
                    fileItem.IsRenaming = false;
                    _isRenaming = false;
                }
                if (Instance.ShowInGrid)
                {
                    Keyboard.ClearFocus(); // Remove focus border
                }
                var sourceElement = e.OriginalSource as DependencyObject;
                var currentBorder = sourceElement as Border ?? FindParentOrChild<Border>(sourceElement);

                if (currentBorder != null)
                {
                    if (!fileItem.IsSelected)
                    {
                        currentBorder.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private void FileItem_MouseEnter(object sender, MouseEventArgs? e)
        {
            if (sender is Border border && border.DataContext is FileItem fileItem)
            {
                _itemUnderCursor = fileItem;
                if (Instance.EnableCustomItemsOrder && ((GetAsyncKeyState(0xA4) & 0x8000) != 0 ||
                    (GetAsyncKeyState(0xA5) & 0x8000) != 0)) // Left or right ALT is down
                {
                    //  fileItem.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                    _canChangeItemPosition = true;
                }
                else
                {
                    _canChangeItemPosition = false;
                }

                if (_canChangeItemPosition && _isDragging && !fileItem.IsSelected)
                {
                    fileItem.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

                    fileItem.IsMoveBarVisible = true;
                }
                else
                {
                    fileItem.IsMoveBarVisible = false;
                }
                if (_dragdropIntoFolder && fileItem.IsFolder && !_canChangeItemPosition)
                {
                    _dropIntoFolderPath = fileItem.FullPath + "\\";
                    if (showFolderInGrid.Visibility == Visibility.Visible)
                    {
                        border.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
                    }
                    else
                    {
                        fileItem.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                    }
                }
                else if (!_dragdropIntoFolder)
                {
                    if (showFolderInGrid.Visibility == Visibility.Visible)
                    {
                        border.Background = fileItem.IsSelected ? new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)) : Brushes.Transparent;
                    }
                    else
                    {
                        fileItem.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                    }
                }
                if (showFolderInGrid.Visibility == Visibility.Visible && !fileItem.IsSelected && fileItem.IsFolder)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                }
            }
        }

        private void FileItem_MouseLeave(object sender, MouseEventArgs? e)
        {
            if (sender is Border border && border.DataContext is FileItem fileItem)
            {
                _itemUnderCursor = null;
                if (!_isRenamingFromContextMenu)
                {
                    fileItem.IsRenaming = false;
                    _isRenaming = false;
                }
                fileItem.IsMoveBarVisible = false;
                _dropIntoFolderPath = "";
                if (!fileItem.IsSelected)
                {
                    fileItem.Background = fileItem.IsSelected ? new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) : Brushes.Transparent;
                }
                else
                {
                    fileItem.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                }
                if (showFolderInGrid.Visibility == Visibility.Visible && !fileItem.IsSelected /*&& !fileItem.IsFolder*/)
                {
                    border.Background = fileItem.IsSelected ? new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)) : Brushes.Transparent;
                }
            }
        }

        private void FileListView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(FileListView);
            var hit = VisualTreeHelper.HitTest(FileListView, point)?.VisualHit;

            while (hit != null && hit is not GridViewColumnHeader)
                hit = VisualTreeHelper.GetParent(hit);

            if (hit is not GridViewColumnHeader header || header.Column == null)
                return;

            int newSort = Instance.SortBy;

            if (header.Column == NameGridColumn)
                newSort = Instance.SortBy != 1 ? 1 : 2;
            else if (header.Column == DateModifiedGridColumn)
                newSort = Instance.SortBy != 3 ? 3 : 4;
            else if (header.Column == SizeGridColumn)
                newSort = Instance.SortBy != 9 ? 9 : 10;

            if (newSort != Instance.SortBy)
            {
                Instance.SortBy = newSort;
                SortItems();
            }
        }

        private void scrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;
            }
        }

        private void RenameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _itemCurrentlyRenaming != null && (_mouseIsOver || _isRenamingFromContextMenu))
            {
                string newName = ((TextBox)sender).Text;
                if (!Instance.ShowFileExtension && newName.Contains('.'))
                {
                    return;
                }

                string oldPath = _itemCurrentlyRenaming.FullPath!;
                string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
                bool renamed = true;

                // A colliding name, a trailing dot/space, a reserved character, or a
                // file locked by another process all throw here - renaming is routine
                // enough that this was one of the easiest ways to crash the app.
                try
                {
                    if (!_itemCurrentlyRenaming.IsFolder)
                    {
                        var ext = Path.GetExtension(oldPath);
                        if (!string.IsNullOrEmpty(ext) && string.IsNullOrEmpty(Path.GetExtension(newName)))
                        {
                            newPath += ext;

                        }
                        File.Move(oldPath, newPath);
                    }
                    else
                    {
                        Directory.Move(oldPath, newPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    renamed = false;
                    Racks.Views.RacksMessageBox.Show($"Couldn't rename to \"{newName}\":\n{ex.Message}", "Rename failed");
                }

                _isRenaming = false;
                _isRenamingFromContextMenu = false;
                if (renamed) _itemCurrentlyRenaming.Name = newName;
                _itemCurrentlyRenaming.IsRenaming = false;
                _itemCurrentlyRenaming.IsSelected = false;
                _itemCurrentlyRenaming.Background = Brushes.Transparent;
            }
            else if (e.Key == Key.Escape && _itemCurrentlyRenaming != null)
            {
                _itemCurrentlyRenaming.IsRenaming = false;
                _isRenamingFromContextMenu = false;
                _isRenaming = false;

            }
        }
    }
}
