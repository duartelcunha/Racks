#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8622, CS8625
using Racks.ViewModels;
using Racks.Core;
using Racks.Properties;
using Racks.Shaders;
using Racks.Util;
using Racks.Rack.Sorting;
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
        // The ordering rules live in Rack/Sorting/FileSorter.cs (no UI, tested). These wrappers bind them to
        // this rack's settings, file ids and controls.

        private static string? FileIdOf(string? path) => path == null ? null : GetFileId(path).ToString();

        public static ObservableCollection<FileItem> SortFileItems(ObservableCollection<FileItem> fileItems, int sortBy, int folderOrder)
            => new ObservableCollection<FileItem>(FileSorter.Sort(fileItems, sortBy, folderOrder));

        public async Task<List<FileSystemInfo>> SortFileItemsToList(List<FileSystemInfo> fileItems, int sortBy, int folderOrder)
        {
            var withSizes = new List<(FileSystemInfo, long)>();
            foreach (var item in fileItems)
                withSizes.Add((item, await GetItemSizeAsync(item)));
            return FileSorter.Sort(withSizes, sortBy, folderOrder);
        }

        public void SortCustomOrder(List<FileSystemInfo> items, List<Tuple<string, string>> customOrderedItems)
            => FileSorter.ApplyCustomOrder(items, customOrderedItems, f => FileIdOf(f.FullName));

        public void SortCustomOrderOc(ObservableCollection<FileItem> items, List<Tuple<string, string>> customOrderedItems)
            => FileSorter.ApplyCustomOrder(items, customOrderedItems, f => FileIdOf(f.FullPath), items.Move);

        public void FirstRowByLastAccessed(List<FileSystemInfo> items, List<string> lastAccessedFileIds, int topN)
            => FileSorter.MoveRecentToFront(items, lastAccessedFileIds, topN, f => FileIdOf(f.FullName));

        private async Task<long> GetItemSizeAsync(FileSystemInfo entry, CancellationToken token = default)
        {
            if (entry is FileInfo fileInfo) return fileInfo.Length;
            if (entry is DirectoryInfo directoryInfo && Instance.CheckFolderSize)
                return await Task.Run(() => FolderSize.Compute(directoryInfo, token), token);
            return 0;
        }

        private long GetDirectorySize(DirectoryInfo directory, CancellationToken token) => FolderSize.Compute(directory, token);

        public void FirstRowByLastAccessed(ObservableCollection<FileItem> items, List<string> lastAccessedFiles, int topN)
        {
            var wrapPanel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
            if (wrapPanel != null)
            {
                double itemWidth = wrapPanel.ItemWidth;
                ItemPerRow = (int)((this.Width) / itemWidth);
            }
            if (items == null || items.Count == 0 || lastAccessedFiles == null || lastAccessedFiles.Count == 0 || topN <= 0)
                return;

            int insertIndex = FileSorter.MoveRecentToFront(items, lastAccessedFiles, topN, i => FileIdOf(i.FullPath), items.Move);

            // Everything after the recent row keeps the rack's normal sort order.
            var sortedRemaining = FileSorter.Sort(items.Skip(insertIndex), (int)Instance.SortBy, Instance.FolderOrder);
            for (int i = 0; i < sortedRemaining.Count; i++)
            {
                int oldIndex = items.IndexOf(sortedRemaining[i]);
                if (oldIndex >= 0 && oldIndex != insertIndex + i)
                    items.Move(oldIndex, insertIndex + i);
            }
        }

        public void SortItems()
        {
            var sortedList = SortFileItems(FileItems, (int)Instance.SortBy, Instance.FolderOrder);

            if (Instance.EnableCustomItemsOrder)
            {
                SortCustomOrderOc(sortedList, Instance.CustomOrderFiles);
            }
            if (Instance.LastAccesedToFirstRow)
            {
                FirstRowByLastAccessed(sortedList, Instance.LastAccessedFiles, ItemPerRow);
            }
            FileItems.Clear();
            foreach (var fileItem in sortedList)
            {
                FileItems.Add(fileItem);
            }
        }

        public Task<string> BytesToStringAsync(long byteCount) => Task.FromResult(FolderSize.Kilobytes(byteCount));
    }
}
