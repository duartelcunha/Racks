using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Racks.Core;
using Racks.Services;
using Racks.Util;

namespace Racks.ViewModels
{
    public class RackViewModel : INotifyPropertyChanged
    {
        private readonly Instance _instance;
        public Instance Instance => _instance;
        private readonly InstanceController _controller;
        
        private CancellationTokenSource _loadFilesCancellationToken = new CancellationTokenSource();

        private ObservableCollection<FileItem> _fileItems = new ObservableCollection<FileItem>();
        public ObservableCollection<FileItem> FileItems
        {
            get => _fileItems;
            set
            {
                _fileItems = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _folderCount = "0";
        public string FolderCount
        {
            get => _folderCount;
            set { _folderCount = value; OnPropertyChanged(); }
        }

        private string _fileCount = "0";
        public string FileCount
        {
            get => _fileCount;
            set { _fileCount = value; OnPropertyChanged(); }
        }

        private string _folderSize = "";
        public string FolderSize
        {
            get => _folderSize;
            set { _folderSize = value; OnPropertyChanged(); }
        }

        public RackViewModel(Instance instance, InstanceController controller)
        {
            _instance = instance;
            _controller = controller;
        }

        public async Task LoadFilesAsync(string path, int itemPerRow, double windowsScalingFactor)
        {
            _loadFilesCancellationToken.Cancel();
            _loadFilesCancellationToken.Dispose();
            _loadFilesCancellationToken = new CancellationTokenSource();
            CancellationToken loadFiles_cts = _loadFilesCancellationToken.Token;
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                IsLoading = true;

                var fileEntries = await Task.Run(() =>
                {
                    if (loadFiles_cts.IsCancellationRequested)
                    {
                        return new List<FileSystemInfo>();
                    }
                    
                    var filteredFiles = new List<FileSystemInfo>();
                    
                    void ScanDir(string dirPath)
                    {
                        if (!Directory.Exists(dirPath)) return;
                        var dirInfo = new DirectoryInfo(dirPath);
                        var files = dirInfo.GetFiles();
                        var directories = dirInfo.GetDirectories();
                        filteredFiles.AddRange(files.Cast<FileSystemInfo>().Concat(directories));
                    }
                    
                    ScanDir(path);
                    
                    if (_instance.IsDesktopFilterRack)
                    {
                        ScanDir(DesktopIconManager.RacksWorkspacePath);
                    }

                    // Remove duplicates by name (if a file somehow exists in both, prefer Workspace)
                    filteredFiles = filteredFiles
                        .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();

                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        FolderCount = filteredFiles.OfType<DirectoryInfo>().Count().ToString();
                        FileCount = filteredFiles.OfType<FileInfo>().Count().ToString();
                    });

                    if (_instance.CheckFolderSize)
                    {
                        var totalSize = filteredFiles.OfType<FileInfo>().Sum(file => file.Length);
                        var sizeStr = Task.Run(() => BytesToStringAsync(totalSize)).Result;
                        System.Windows.Application.Current.Dispatcher.Invoke(() => { FolderSize = sizeStr; });
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => { FolderSize = ""; });
                    }
                    
                    filteredFiles = filteredFiles
                                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                    
                    if (!_instance.ShowHiddenFiles)
                        filteredFiles = filteredFiles.Where(entry => !entry.Attributes.HasFlag(FileAttributes.Hidden)).ToList();
                    if (_instance.FileFilterRegex != null)
                    {
                        var regex = new Regex(_instance.FileFilterRegex);
                        filteredFiles = filteredFiles.Where(entry => regex.IsMatch(entry.Name)).ToList();
                    }

                    if (_instance.IsDesktopFilterRack)
                    {
                        filteredFiles = filteredFiles.Where(entry =>
                        {
                            return _instance.AssignedFiles != null && _instance.AssignedFiles.Contains(entry.Name);
                        }).ToList();
                    }

                    return filteredFiles;
                }, loadFiles_cts);

                if (loadFiles_cts.IsCancellationRequested)
                {
                    IsLoading = false;
                    return;
                }

                fileEntries = await SortFileItemsToList(fileEntries, (int)_instance.SortBy, _instance.FolderOrder, loadFiles_cts);

                if (_instance.EnableCustomItemsOrder)
                {
                    SortCustomOrder(fileEntries, _instance.CustomOrderFiles);
                }
                if (_instance.LastAccesedToFirstRow)
                {
                    FirstRowByLastAccessed(fileEntries, _instance.LastAccessedFiles, itemPerRow);
                }
                var fileNames = new HashSet<string>(fileEntries.Select(f => f.Name));


                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (loadFiles_cts.IsCancellationRequested)
                    {
                        IsLoading = false;
                        return;
                    }
                    bool assignedFilesChanged = false;
                    for (int i = FileItems.Count - 1; i >= 0; i--)  // Remove item that no longer exist
                    {
                        if (loadFiles_cts.IsCancellationRequested)
                        {
                            IsLoading = false;
                            return;
                        }
                        
                        // Check if the exact FullPath still exists in the newly scanned entries
                        bool stillExists = fileEntries.Any(f => string.Equals(f.FullName, FileItems[i].FullPath, StringComparison.OrdinalIgnoreCase));
                        
                        if (!stillExists)
                        {
                            string fileName = Path.GetFileName(FileItems[i].FullPath!);
                            FileItems.RemoveAt(i);
                            
                            // Cleanup: if the file was physically moved/deleted, remove it from the Rack's claim
                            if (_instance.IsDesktopFilterRack && _instance.AssignedFiles != null && _instance.AssignedFiles.Contains(fileName))
                            {
                                _instance.AssignedFiles.Remove(fileName);
                                assignedFilesChanged = true;
                            }
                        }
                    }
                    
                    if (assignedFilesChanged)
                    {
                        _controller.WriteInstanceToKey(_instance);
                    }

                    foreach (var entry in fileEntries)
                    {
                        if (loadFiles_cts.IsCancellationRequested)
                        {
                            IsLoading = false;
                            return;
                        }

                        var existingItem = FileItems.FirstOrDefault(item => item.FullPath == entry.FullName);

                        long size = 0;
                        if (entry is FileInfo fileInfo)
                            size = fileInfo.Length;
                        else if (entry is DirectoryInfo directoryInfo && _instance.CheckFolderSize)
                            size = await Task.Run(() => GetDirectorySize(directoryInfo, loadFiles_cts));
                        size = size > int.MaxValue ? int.MaxValue : size;

                        string displaySize = entry is FileInfo ? await BytesToStringAsync(size)
                                                               : _instance.CheckFolderSize ? await BytesToStringAsync(size)
                                                                                          : "";
                        var thumbnail = await ThumbnailService.GetThumbnailAsync(entry.FullName, _instance.IconSize, _instance.ShowShortcutArrow, windowsScalingFactor);
                        bool isFile = entry is FileInfo;
                        string actualExt = isFile ? Path.GetExtension(entry.Name) : string.Empty;
                        if (existingItem == null)
                        {
                            if (!string.IsNullOrEmpty(_instance.FileFilterHideRegex) &&
                                new Regex(_instance.FileFilterHideRegex).IsMatch(entry.Name))
                            {
                                continue;
                            }
                            
                            // If it's a DesktopFilterRack, ensure it hasn't just been removed from AssignedFiles during the cleanup phase
                            if (_instance.IsDesktopFilterRack && _instance.AssignedFiles != null && !_instance.AssignedFiles.Contains(entry.Name))
                            {
                                continue;
                            }

                            FileItems.Add(new FileItem
                            {
                                Name = _instance.ShowFileExtension || string.IsNullOrEmpty(actualExt)
                                    ? entry.Name
                                    : entry.Name.Substring(0, entry.Name.Length - actualExt.Length),
                                FullPath = entry.FullName,
                                IsFolder = !isFile,
                                DateModified = entry.LastWriteTime,
                                DateCreated = entry.CreationTime,
                                FileType = isFile ? actualExt : string.Empty,
                                ItemSize = (int)size,
                                DisplaySize = displaySize,
                                Thumbnail = thumbnail
                            });
                        }
                        else
                        {
                            existingItem.Name = _instance.ShowFileExtension || string.IsNullOrEmpty(actualExt)
                                    ? entry.Name
                                    : entry.Name.Substring(0, entry.Name.Length - actualExt.Length);
                            existingItem.FullPath = entry.FullName;
                            existingItem.IsFolder = string.IsNullOrEmpty(Path.GetExtension(entry.FullName));
                            existingItem.DateModified = entry.LastWriteTime;
                            existingItem.DateCreated = entry.CreationTime;
                            existingItem.FileType = entry is FileInfo ? entry.Extension : string.Empty;
                            existingItem.ItemSize = (int)size;
                            existingItem.DisplaySize = displaySize;
                            existingItem.Thumbnail = thumbnail;
                        }
                    }
                    var sortedList = FileItems.ToList();

                    FileItems.Clear();
                    foreach (var fileItem in sortedList)
                    {
                        if (_instance.FileFilterHideRegex != null && _instance.FileFilterHideRegex != ""
                          && new Regex(_instance.FileFilterHideRegex).IsMatch(fileItem.Name))
                        {
                            continue;
                        }
                        FileItems.Add(fileItem);
                    }
                    if (_instance.EnableCustomItemsOrder)
                    {
                        SortCustomOrderOc(FileItems, _instance.CustomOrderFiles);
                    }
                    if (_instance.LastAccesedToFirstRow)
                    {
                        FirstRowByLastAccessedOc(FileItems, _instance.LastAccessedFiles, itemPerRow);
                    }
                });

                IsLoading = false;
            }
            catch (Exception)
            {
                IsLoading = false;
            }
        }

        private async Task<List<FileSystemInfo>> SortFileItemsToList(List<FileSystemInfo> fileItems, int sortBy, int folderOrder, CancellationToken token)
        {
            var fileItemSizes = new List<(FileSystemInfo item, long size)>();

            foreach (var item in fileItems)
            {
                long size = await GetItemSizeAsync(item, token);
                fileItemSizes.Add((item, size));
            }

            var sortOptions = new Dictionary<int, Func<List<(FileSystemInfo item, long size)>, IOrderedEnumerable<(FileSystemInfo item, long size)>>>
                {
                    { (int)RackWindow.SortBy.NameAsc, x => x.OrderBy(i => Regex.Replace(i.item.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                    { (int)RackWindow.SortBy.NameDesc,x => x .OrderByDescending(i => Regex.Replace(i.item.Name ?? "", @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)},
                    { (int)RackWindow.SortBy.DateModifiedAsc, x => x.OrderBy(i => i.item.LastWriteTime) },
                    { (int)RackWindow.SortBy.DateModifiedDesc, x => x.OrderByDescending(i => i.item.LastWriteTime) },
                    { (int)RackWindow.SortBy.DateCreatedAsc, x => x.OrderBy(i => i.item.CreationTime) },
                    { (int)RackWindow.SortBy.DateCreatedDesc, x => x.OrderByDescending(i => i.item.CreationTime) },
                    { (int)RackWindow.SortBy.FileTypeAsc, x => x.OrderBy(i => i.item.Extension) },
                    { (int)RackWindow.SortBy.FileTypeDesc, x => x.OrderByDescending(i => i.item.Extension) },
                    { (int)RackWindow.SortBy.ItemSizeAsc, x => x.OrderBy(i => i.size) },
                    { (int)RackWindow.SortBy.ItemSizeDesc, x => x.OrderByDescending(i => i.size) },
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

        private void SortCustomOrder(List<FileSystemInfo> items, List<Tuple<string, string>> customOrderedItems)
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
                var itemToMove = items.FirstOrDefault(f => Interop.GetFileId(f.FullName!).ToString() == fileId);

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

        private void SortCustomOrderOc(ObservableCollection<FileItem> items, List<Tuple<string, string>> customOrderedItems)
        {
            if (items == null || items.Count == 0 || customOrderedItems == null || customOrderedItems.Count == 0)
            {
                return;
            }
            foreach (var t in customOrderedItems)
            {
                string fileId = t.Item1;
                if (!int.TryParse(t.Item2, out int targetIndex)) continue;

                var itemToMove = items.FirstOrDefault(f => Interop.GetFileId(f.FullPath!).ToString() == fileId);
                if (itemToMove == null) continue;

                int currentIndex = items.IndexOf(itemToMove);
                if (currentIndex != targetIndex && targetIndex >= 0 && targetIndex < items.Count)
                {
                    items.Move(currentIndex, targetIndex);
                }
            }
        }

        private void FirstRowByLastAccessed(List<FileSystemInfo> items, List<string> lastAccessedFileIds, int topN)
        {
            if (items == null || items.Count == 0 || lastAccessedFileIds == null || lastAccessedFileIds.Count == 0 || topN <= 0)
                return;

            var fileLookup = items
                .Where(f => f.FullName != null)
                .GroupBy(f => Interop.GetFileId(f.FullName).ToString())
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
        
        private void FirstRowByLastAccessedOc(ObservableCollection<FileItem> items, List<string> lastAccessedFileIds, int topN)
        {
            if (items == null || items.Count == 0 || lastAccessedFileIds == null || lastAccessedFileIds.Count == 0 || topN <= 0)
                return;

            var fileLookup = items
                .Where(f => f.FullPath != null)
                .GroupBy(f => Interop.GetFileId(f.FullPath!).ToString())
                .ToDictionary(g => g.Key, g => g.ToList());

            var topIds = lastAccessedFileIds
                .Where(id => fileLookup.ContainsKey(id))
                .Take(topN)
                .ToList();

            var topFiles = new List<FileItem>();
            foreach (var id in topIds)
            {
                if (!fileLookup.ContainsKey(id))
                    continue;
                topFiles.AddRange(fileLookup[id]);
            }

            var remainingFiles = items.Except(topFiles).ToList();
            items.Clear();
            foreach(var f in topFiles) items.Add(f);
            foreach(var f in remainingFiles) items.Add(f);
        }

        private async Task<long> GetItemSizeAsync(FileSystemInfo entry, CancellationToken token = default)
        {
            if (entry is FileInfo fileInfo)
            {
                return fileInfo.Length;
            }
            else if (entry is DirectoryInfo directoryInfo && _instance.CheckFolderSize)
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

                Parallel.ForEach(directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly), (subDir) =>
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

        public async Task<string> BytesToStringAsync(long byteCount)
        {
            return await Task.Run(() =>
            {
                string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
                if (byteCount == 0)
                    return "0 " + suf[0];
                long bytes = Math.Abs(byteCount);
                int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
                double num = Math.Round(bytes / Math.Pow(1024, place), 1);
                return (Math.Sign(byteCount) * num).ToString() + " " + suf[place];
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
