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
        public static ObservableCollection<FileItem> SortFileItems(ObservableCollection<FileItem> fileItems, int sortBy, int folderOrder)
        {
            IEnumerable<FileItem> items = fileItems;

            var sortOptions = new Dictionary<int, Func<IEnumerable<FileItem>, IOrderedEnumerable<FileItem>>>
            {
                { (int)SortBy.NameAsc, x => x.OrderBy(i => Regex.Replace(i.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                { (int)SortBy.NameDesc,x => x .OrderByDescending(i => Regex.Replace(i.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                { (int)SortBy.DateModifiedAsc, x => x.OrderBy(i => i.DateModified) },
                { (int)SortBy.DateModifiedDesc, x => x.OrderByDescending(i => i.DateModified) },
                { (int)SortBy.DateCreatedAsc, x => x.OrderBy(i => i.DateCreated) },
                { (int)SortBy.DateCreatedDesc, x => x.OrderByDescending(i => i.DateCreated) },
                { (int)SortBy.FileTypeAsc, x => x.OrderBy(i => i.FileType) },
                { (int)SortBy.FileTypeDesc, x => x.OrderByDescending(i => i.FileType) },
                { (int)SortBy.ItemSizeAsc, x => x.OrderBy(i => i.ItemSize) },
                { (int)SortBy.ItemSizeDesc, x => x.OrderByDescending(i => i.ItemSize) },
            };

            if (sortOptions.TryGetValue(sortBy, out var sorter))
                items = sorter(items);

            if (folderOrder == 1)
                items = items.OrderBy(i => !i.IsFolder);
            else if (folderOrder == 2)
                items = items.OrderBy(i => i.IsFolder);

            return new ObservableCollection<FileItem>(items);
        }

        public async Task<List<FileSystemInfo>> SortFileItemsToList(List<FileSystemInfo> fileItems, int sortBy, int folderOrder)
        {
            var fileItemSizes = new List<(FileSystemInfo item, long size)>();

            foreach (var item in fileItems)
            {
                long size = await GetItemSizeAsync(item);
                fileItemSizes.Add((item, size));
            }

            var sortOptions = new Dictionary<int, Func<List<(FileSystemInfo item, long size)>, IOrderedEnumerable<(FileSystemInfo item, long size)>>>
                {
                    { (int)SortBy.NameAsc, x => x.OrderBy(i => Regex.Replace(i.item.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                    { (int)SortBy.NameDesc,x => x .OrderByDescending(i => Regex.Replace(i.item.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                    { (int)SortBy.DateModifiedAsc, x => x.OrderBy(i => i.item.LastWriteTime) },
                    { (int)SortBy.DateModifiedDesc, x => x.OrderByDescending(i => i.item.LastWriteTime) },
                    { (int)SortBy.DateCreatedAsc, x => x.OrderBy(i => i.item.CreationTime) },
                    { (int)SortBy.DateCreatedDesc, x => x.OrderByDescending(i => i.item.CreationTime) },
                    { (int)SortBy.FileTypeAsc, x => x.OrderBy(i => i.item.Extension) },
                    { (int)SortBy.FileTypeDesc, x => x.OrderByDescending(i => i.item.Extension) },
                    { (int)SortBy.ItemSizeAsc, x => x.OrderBy(i => i.size) },
                    { (int)SortBy.ItemSizeDesc, x => x.OrderByDescending(i => i.size) },
                };

            var sortedItems = sortOptions.TryGetValue(sortBy, out var sorter)
                ? sorter(fileItemSizes).ToList()
                : fileItemSizes.ToList();

            if (folderOrder == 1)
                sortedItems = sortedItems.OrderBy(i => i.item is FileInfo).ToList();
            else if (folderOrder == 2)
                sortedItems = sortedItems.OrderBy(i => i.item is DirectoryInfo).ToList();

            var sortedFileInfos = sortedItems.Select(x => x.item).ToList();

            return sortedFileInfos;
        }

        public void SortCustomOrder(List<FileSystemInfo> items, List<Tuple<string, string>> customOrderedItems)
        {
            if (items == null || items.Count == 0 || customOrderedItems == null || customOrderedItems.Count == 0)
            {
                return;
            }
            foreach (var t in customOrderedItems)
            {
                string fileId = t.Item1;
                if (!int.TryParse(t.Item2, out int targetIndex))
                {
                    continue;
                }
                var itemToMove = items.FirstOrDefault(f => GetFileId(f.FullName!).ToString() == fileId);

                if (itemToMove == null)
                {
                    continue;
                }

                int currentIndex = items.IndexOf(itemToMove);

                if (currentIndex == targetIndex)
                {
                    continue;
                }
                if (targetIndex < 0 || targetIndex >= items.Count)
                {
                    continue;
                }
                items.RemoveAt(currentIndex);
                items.Insert(targetIndex, itemToMove);
            }
        }

        public void SortCustomOrderOc(ObservableCollection<FileItem> items, List<Tuple<string, string>> customOrderedItems)
        {

            if (items == null || items.Count == 0 || customOrderedItems == null || customOrderedItems.Count == 0)
            {
                return;
            }
            foreach (var t in customOrderedItems)
            {
                string fileId = t.Item1;
                if (!int.TryParse(t.Item2, out int targetIndex)) continue;

                var itemToMove = items.FirstOrDefault(f => GetFileId(f.FullPath!).ToString() == fileId);
                if (itemToMove == null) continue;

                int currentIndex = items.IndexOf(itemToMove);
                if (currentIndex != targetIndex && targetIndex >= 0 && targetIndex < items.Count)
                {
                    items.Move(currentIndex, targetIndex);
                }
            }
        }

        public void FirstRowByLastAccessed(List<FileSystemInfo> items, List<string> lastAccessedFileIds, int topN)
        {
            if (items == null || items.Count == 0 || lastAccessedFileIds == null || lastAccessedFileIds.Count == 0 || topN <= 0)
                return;

            var fileLookup = items
                .Where(f => f.FullName != null)
                .GroupBy(f => GetFileId(f.FullName).ToString())
                .ToDictionary(g => g.Key, g => g.ToList());

            var topIds = lastAccessedFileIds
                .Where(id => fileLookup.ContainsKey(id))
                .Take(topN)
                .ToList();

            var topFiles = new List<FileSystemInfo>();
            foreach (var id in topIds)
            {
                if (!fileLookup.ContainsKey(id))
                    continue;
                topFiles.AddRange(fileLookup[id]);
            }

            var remainingFiles = items.Except(topFiles).ToList();
            items.Clear();
            items.AddRange(topFiles);
            items.AddRange(remainingFiles);
        }

        private async Task<long> GetItemSizeAsync(FileSystemInfo entry, CancellationToken token = default)
        {
            if (entry is FileInfo fileInfo)
            {
                return fileInfo.Length;
            }
            else if (entry is DirectoryInfo directoryInfo && Instance.CheckFolderSize)
            {
                return await Task.Run(() => GetDirectorySize(directoryInfo, token), token);
            }

            return 0;
        }

        private long GetDirectorySize(DirectoryInfo directory, CancellationToken token)
        {
            long size = 0;

            try
            {
                foreach (var file in directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    size += file.Length;
                }

                // Skip reparse points (symlinks/junctions): a link back to an ancestor
                // directory would otherwise recurse forever - StackOverflowException,
                // which can't be caught, kills the whole app. Not following links also
                // matches how disk-usage tools normally measure a folder's own size.
                var subDirs = directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint));

                Parallel.ForEach(subDirs, (subDir) =>
                {
                    token.ThrowIfCancellationRequested();
                    Interlocked.Add(ref size, GetDirectorySize(subDir, token));
                });
            }
            catch
            {
            }

            return size;
        }

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
            var fileLookup = items
                .Where(i => i.FullPath != null)
                .GroupBy(i => GetFileId(i.FullPath!).ToString())
                .ToDictionary(g => g.Key, g => g.ToList());

            var topIds = lastAccessedFiles
                .Where(id => fileLookup.ContainsKey(id))
                .Take(topN)
                .ToList();

            int insertIndex = 0;
            foreach (var id in topIds)
            {
                foreach (var item in fileLookup[id])
                {
                    int oldIndex = items.IndexOf(item);
                    if (oldIndex >= 0 && oldIndex != insertIndex)
                        items.Move(oldIndex, insertIndex);
                    insertIndex++;
                }
            }
            var remainingItems = new ObservableCollection<FileItem>(items.Skip(insertIndex));
            var sortedRemaining = SortFileItems(remainingItems, (int)Instance.SortBy, Instance.FolderOrder);
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

        public Task<string> BytesToStringAsync(long byteCount)
        {
            return Task.Run(() =>
            {
                double kilobytes = byteCount / 1024.0;
                string formattedKilobytes;
                try
                {
                    formattedKilobytes = kilobytes.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }
                catch
                {
                    try
                    {
                        formattedKilobytes = kilobytes.ToString("#,0", System.Globalization.CultureInfo.CurrentCulture).Replace(",", " ");
                    }
                    catch
                    {
                        formattedKilobytes = kilobytes.ToString("#,0").Replace(",", " ");
                    }
                }
                return formattedKilobytes + " KB";
            });
        }
    }
}
