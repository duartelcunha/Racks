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
        private void OpenFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_currentFolderPath) { UseShellExecute = true });
            }
            catch
            { }
        }

        // VirtualDesktop.CurrentChanged is a static event, so the lambda we used to
        // subscribe with captured this window and kept it alive after close. Named
        // handler so Window_Closing can detach it cleanly.
        private void OnVirtualDesktopChanged(object? sender, VirtualDesktopChangedEventArgs args)
        {
            var newDesktop = args.NewDesktop;
            _currentVD = Array.IndexOf(VirtualDesktop.GetDesktops(), newDesktop) + 1;
            if (Instance.ShowOnVirtualDesktops != null && Instance.ShowOnVirtualDesktops.Length != 0 && !Instance.ShowOnVirtualDesktops.Contains(_currentVD))
            {
                Dispatcher.InvokeAsync(() => this.Hide());
            }
            else
            {
                Dispatcher.InvokeAsync(() => this.Show());
            }
            Debug.WriteLine($"Switched to virtual desktop: {_currentVD}");
        }

        public void InitializeFileWatchers()
        {
            if (Instance.Folder != null && Instance.Folder != "empty")
            {
                // Wire the watcher's events to this rack ONCE so external changes to the
                // rack's folder (add / delete / rename a file or folder in RacksWorkspace or
                // the bound folder) refresh the rack live. Guarded so repeated calls to
                // InitializeFileWatchers don't stack duplicate handlers.
                if (!_fileWatcherWired)
                {
                    _fileWatcherService.FileChanged += OnFileChanged;
                    _fileWatcherService.FileRenamed += OnFileRenamed;
                    _fileWatcherService.ParentChanged += OnParentChanged;
                    _fileWatcherService.ParentRenamed += OnParentRenamed;
                    _fileWatcherWired = true;
                }
                // A desktop rack lists the Desktop but its files live in the workspace: watch both, so moving an item out refreshes the rack.
                _fileWatcherService.Initialize(Instance.Folder, _currentFolderPath, Instance.IsDesktopFilterRack ? Racks.Infrastructure.Shell.ShellOperationGateway.Default.WorkspacePath : null);
            }

            if (!Path.Exists(Instance.Folder) && Instance.Folder != "empty")
            {
                missingFolderGrid.Visibility = Visibility.Visible;
                return;
            }
            else
            {
                missingFolderGrid.Visibility = Visibility.Hidden;
            }
        }

        private void OnParentRenamed(object sender, RenamedEventArgs e)
        {
            if (Instance.isWindowClosing || string.IsNullOrEmpty(Instance.Folder)) return;
            string? folderName = Path.GetFileName(Instance.Folder);
            if (string.IsNullOrEmpty(folderName)) return;

            if (!string.IsNullOrEmpty(e.Name) && string.Equals(e.Name, folderName, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (Instance.isWindowClosing) return;
                    if (!Path.Exists(Instance.Folder) && Instance.Folder != "empty")
                    {
                        PathToBackButton.Visibility = Visibility.Collapsed;
                        missingFolderGrid.Visibility = Visibility.Visible;
                        FileItems.Clear();
                    }
                    else
                    {
                        missingFolderGrid.Visibility = Visibility.Hidden;
                        LoadFiles(Instance.Folder);
                        InitializeFileWatchers();
                    }
                });
            }
            if (!string.IsNullOrEmpty(e.OldName) && string.Equals(e.OldName, folderName, StringComparison.OrdinalIgnoreCase))
            {
                var lastInstanceName = Instance.Name;
                Dispatcher.InvokeAsync(() =>
                {
                    if (Instance.isWindowClosing) return;
                    Instance.Folder = e.FullPath;
                    Instance.IsFolderMissing = false;
                    _currentFolderPath = Instance.Folder;
                    Instance.Name = Path.GetFileName(e.Name ?? "");
                    MainWindow._controller.WriteOverInstanceToKey(Instance, lastInstanceName);
                    title.Text = Instance.TitleText == "" ? Instance.Name : Instance.TitleText;
                    PathToBackButton.Visibility = Visibility.Collapsed;
                    missingFolderGrid.Visibility = Visibility.Hidden;
                    foreach (var item in FileItems)
                    {
                        if (item.FullPath != null && !string.IsNullOrEmpty(e.OldName) && !string.IsNullOrEmpty(e.Name))
                            item.FullPath = item.FullPath.Replace(@$"\{e.OldName}\", @$"\{e.Name}\");
                    }
                    InitializeFileWatchers();
                });
            }
        }

        private void OnParentChanged(object sender, FileSystemEventArgs e)
        {
            if (Instance.isWindowClosing || string.IsNullOrEmpty(e.Name) || string.IsNullOrEmpty(Instance.Folder)) return;
            string? folderName = Path.GetFileName(Instance.Folder);
            if (string.IsNullOrEmpty(folderName) || !string.Equals(e.Name, folderName, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (Instance.isWindowClosing) return;
                if (!Path.Exists(Instance.Folder) && Instance.Folder != "empty")
                {
                    PathToBackButton.Visibility = Visibility.Collapsed;
                    missingFolderGrid.Visibility = Visibility.Visible;
                    FileItems.Clear();
                }
                else
                {
                    missingFolderGrid.Visibility = Visibility.Hidden;
                    LoadFiles(Instance.Folder);
                    InitializeFileWatchers();
                }
            });
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Instance.isWindowClosing) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (Instance.isWindowClosing) return;
                if ((!Path.Exists(Instance.Folder) && Instance.Folder != "empty") || e.Name == Instance.Folder)
                {
                    PathToBackButton.Visibility = Visibility.Collapsed;
                    missingFolderGrid.Visibility = Visibility.Visible;
                    return;
                }
                missingFolderGrid.Visibility = Visibility.Hidden;

                if (_watcherDebounce == null)
                {
                    _watcherDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _watcherDebounce.Tick += (_, _) =>
                    {
                        _watcherDebounce!.Stop();
                        if (!Instance.isWindowClosing) LoadFiles(_currentFolderPath);
                    };
                }
                _watcherDebounce.Stop();
                _watcherDebounce.Start();
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"File renamed: {e.OldFullPath} to {e.FullPath}");
                var renamedItem = FileItems.FirstOrDefault(item => item.FullPath == e.OldFullPath);

                if (renamedItem != null)
                {
                    renamedItem.FullPath = e.FullPath;

                    string fileName = Path.GetFileName(e.FullPath);
                    Debug.WriteLine("FILENAME: " + fileName);
                    if (!renamedItem.IsFolder)
                    {
                        Debug.WriteLine("NOT FOLDER");
                        string actualExt = Path.GetExtension(fileName);
                        renamedItem.Name = Instance.ShowFileExtension || string.IsNullOrEmpty(actualExt)
                             ? fileName
                             : fileName.Substring(0, fileName.Length - actualExt.Length);
                    }
                    else
                    {
                        Debug.WriteLine("FOLDER");
                        renamedItem.Name = fileName;
                    }
                }

                SortItems();
            });
        }

        // File filter patterns are saved as raw strings and read back from the registry
        // with no validation on that read path - a hand-edited value, or one carried
        // over from an older build, would otherwise throw on the very next file load.
        // Treat an invalid pattern as "no filter" rather than crashing.
        private static Regex? TryCompileRegex(string? pattern)
        {
            // Compiles with a bounded match timeout so a pathological (ReDoS) pattern can't
            // freeze the UI thread on file load. See Util.SafeRegex.
            return Util.SafeRegex.TryCompile(pattern);
        }

        public async void LoadFiles(string path)
        {
            loadFilesCancellationToken.Cancel();
            loadFilesCancellationToken.Dispose();
            loadFilesCancellationToken = new CancellationTokenSource();
            CancellationToken loadFiles_cts = loadFilesCancellationToken.Token;
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                LoadingProgressRingFade(true);

                var fileEntries = await Task.Run(() =>
                {
                    if (loadFiles_cts.IsCancellationRequested)
                    {
                        LoadingProgressRingFade(false);
                        return new List<FileSystemInfo>();
                    }

                    var filteredFiles = new List<FileSystemInfo>();

                    void ScanDir(string dirPath)
                    {
                        if (!Directory.Exists(dirPath)) return;
                        // A network share or removable drive can disconnect between the
                        // Exists check above and the enumeration below - that race would
                        // otherwise throw IOException/UnauthorizedAccessException uncaught.
                        try
                        {
                            var dirInfo = new DirectoryInfo(dirPath);
                            var files = dirInfo.GetFiles();
                            var directories = dirInfo.GetDirectories();
                            filteredFiles.AddRange(files.Cast<FileSystemInfo>().Concat(directories));
                        }
                        catch (IOException ex) { Debug.WriteLine($"ScanDir failed for '{dirPath}': {ex.Message}"); }
                        catch (UnauthorizedAccessException ex) { Debug.WriteLine($"ScanDir failed for '{dirPath}': {ex.Message}"); }
                    }

                    ScanDir(path);

                    if (Instance.IsDesktopFilterRack)
                    {
                        ScanDir(Racks.Infrastructure.Shell.ShellOperationGateway.Default.WorkspacePath);
                    }

                    // Remove duplicates by name (if a file somehow exists in both, prefer Workspace)
                    filteredFiles = filteredFiles
                        .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.Last()) // the workspace is scanned last, so its copy (the hardlink a Ctrl+drop makes) wins
                        .ToList();

                    _folderCount = filteredFiles.OfType<DirectoryInfo>().Count();
                    _fileCount = filteredFiles.OfType<FileInfo>().Count().ToString();
                    _folderSize = !Instance.CheckFolderSize ? "" : FolderSize.Kilobytes(filteredFiles.OfType<FileInfo>().Sum(file => file.Length));

                    filteredFiles = filteredFiles
                                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                    if (!Instance.ShowHiddenFiles)
                        filteredFiles = filteredFiles.Where(entry => !entry.Attributes.HasFlag(FileAttributes.Hidden)).ToList();
                    var fileFilterRegex = TryCompileRegex(Instance.FileFilterRegex);
                    if (fileFilterRegex != null)
                    {
                        filteredFiles = filteredFiles.Where(entry => Util.SafeRegex.IsMatch(fileFilterRegex, entry.Name, onTimeout: true)).ToList();
                    }

                    if (Instance.IsDesktopFilterRack)
                    {
                        filteredFiles = filteredFiles.Where(entry =>
                        {
                            return Instance.AssignedFiles != null && Instance.AssignedFiles.Contains(entry.Name);
                        }).ToList();
                    }

                    return filteredFiles;
                }, loadFiles_cts);

                if (loadFiles_cts.IsCancellationRequested)
                {
                    LoadingProgressRingFade(false);
                    return;
                }
                if (Instance.LastAccesedToFirstRow)
                {
                    var wrapPanel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
                    if (wrapPanel != null)
                    {
                        double itemWidth = wrapPanel.ItemWidth;
                        ItemPerRow = (int)((this.Width) / itemWidth);
                    }
                    _previousItemPerRow = ItemPerRow;
                }
                fileEntries = await SortFileItemsToList(fileEntries, (int)Instance.SortBy, Instance.FolderOrder);

                if (Instance.EnableCustomItemsOrder)
                {
                    SortCustomOrder(fileEntries, Instance.CustomOrderFiles);
                }
                if (Instance.LastAccesedToFirstRow)
                {
                    FirstRowByLastAccessed(fileEntries, Instance.LastAccessedFiles, ItemPerRow);
                }
                var fileNames = new HashSet<string>(fileEntries.Select(f => f.Name));

                await Dispatcher.InvokeAsync(async () =>
                {
                    if (loadFiles_cts.IsCancellationRequested)
                    {
                        LoadingProgressRingFade(false);
                        return;
                    }
                    var fileFilterHideRegex = TryCompileRegex(Instance.FileFilterHideRegex);
                    bool assignedFilesChanged = false;
                    for (int i = FileItems.Count - 1; i >= 0; i--)  // Remove item that no longer exist
                    {
                        if (loadFiles_cts.IsCancellationRequested)
                        {
                            LoadingProgressRingFade(false);
                            return;
                        }

                        // Check if the exact FullPath still exists in the newly scanned entries
                        bool stillExists = fileEntries.Any(f => string.Equals(f.FullName, FileItems[i].FullPath, StringComparison.OrdinalIgnoreCase));

                        if (!stillExists)
                        {
                            string fileName = Path.GetFileName(FileItems[i].FullPath!);
                            FileItems.RemoveAt(i);

                            // Cleanup: if the file was physically moved/deleted, remove it from the Rack's claim
                            if (Instance.IsDesktopFilterRack && Instance.AssignedFiles != null && Instance.AssignedFiles.Contains(fileName))
                            {
                                Instance.AssignedFiles.Remove(fileName);
                                assignedFilesChanged = true;
                            }
                        }
                    }

                    if (assignedFilesChanged)
                    {
                        MainWindow._controller.WriteInstanceToKey(Instance);
                    }

                    foreach (var entry in fileEntries)
                    {
                        if (loadFiles_cts.IsCancellationRequested)
                        {
                            LoadingProgressRingFade(false);
                            return;
                        }

                        if (Util.SafeRegex.IsMatch(fileFilterHideRegex, entry.Name, onTimeout: false))
                            continue;
                        if (Instance.IsDesktopFilterRack && Instance.AssignedFiles != null && !Instance.AssignedFiles.Contains(entry.Name))
                            continue;

                        var existingItem = FileItems.FirstOrDefault(item => string.Equals(item.FullPath, entry.FullName, StringComparison.OrdinalIgnoreCase));

                        long size = 0;
                        if (entry is FileInfo fileInfo)
                            size = fileInfo.Length;
                        else if (entry is DirectoryInfo directoryInfo && Instance.CheckFolderSize)
                            size = await Task.Run(() => GetDirectorySize(directoryInfo, loadFiles_cts));
                        size = size > int.MaxValue ? int.MaxValue : size;

                        string displaySize = FolderSize.Kilobytes(size);

                        BitmapSource? thumbnail = null;
                        if (existingItem?.Thumbnail != null && existingItem.DateModified == entry.LastWriteTime)
                        {
                            thumbnail = existingItem.Thumbnail;
                        }
                        else
                        {
                            thumbnail = await GetThumbnailAsync(entry.FullName);
                        }

                        bool isFile = entry is FileInfo;
                        string actualExt = isFile ? Path.GetExtension(entry.Name) : string.Empty;
                        if (existingItem == null)
                        {
                            FileItems.Add(new FileItem
                            {
                                Name = Instance.ShowFileExtension || string.IsNullOrEmpty(actualExt)
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
                            existingItem.Name = Instance.ShowFileExtension || string.IsNullOrEmpty(actualExt)
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
                    // Dedup by FullPath before committing
                    var sortedList = FileItems
                        .Where(fi => !Util.SafeRegex.IsMatch(fileFilterHideRegex, fi.Name, onTimeout: false))
                        .GroupBy(fi => fi.FullPath, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();

                    // In-place collection sync avoids wiping the entire WPF visual tree via FileItems.Clear(),
                    // ensuring smooth, professional transitions without flickering or re-animating unchanged tiles.
                    var targetSet = new HashSet<string>(sortedList.Select(x => x.FullPath!), StringComparer.OrdinalIgnoreCase);
                    for (int i = FileItems.Count - 1; i >= 0; i--)
                    {
                        if (string.IsNullOrEmpty(FileItems[i].FullPath) || !targetSet.Contains(FileItems[i].FullPath!))
                        {
                            FileItems.RemoveAt(i);
                        }
                    }
                    for (int i = 0; i < sortedList.Count; i++)
                    {
                        var targetItem = sortedList[i];
                        int currentIndex = -1;
                        for (int j = 0; j < FileItems.Count; j++)
                        {
                            if (string.Equals(FileItems[j].FullPath, targetItem.FullPath, StringComparison.OrdinalIgnoreCase))
                            {
                                currentIndex = j;
                                break;
                            }
                        }
                        if (currentIndex == -1)
                        {
                            FileItems.Insert(Math.Min(i, FileItems.Count), targetItem);
                        }
                        else if (currentIndex != i)
                        {
                            FileItems.Move(currentIndex, i);
                        }
                    }
                    if (Instance.EnableCustomItemsOrder)
                    {
                        SortCustomOrderOc(FileItems, Instance.CustomOrderFiles);
                    }
                    if (Instance.LastAccesedToFirstRow)
                    {
                        FirstRowByLastAccessed(FileItems, Instance.LastAccessedFiles, ItemPerRow);
                    }
                    _lastUpdated = DateTime.Now;
                    int hiddenCount = Int32.Parse(_fileCount) - (FileItems.Count - _folderCount);
                    if (hiddenCount > 0)
                    {
                        _fileCount += $" ({hiddenCount} hidden)";
                    }
                    SortItems();
                    await Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        Dispatcher.Invoke(() =>
                        {
                            LoadingProgressRingFade(false);
                        });
                    });
                    Debug.WriteLine("LOADEDDDDDDDD");
                });
            }
            catch (OperationCanceledException)
            {
                LoadingProgressRingFade(false);
                Debug.WriteLine("LoadFiles was canceled.");
            }
        }

        public BitmapSource? GetThumbnail(string filePath, int size)
        {
            try
            {
                ShellObject shellObject = ShellObject.FromParsingName(filePath);
                ShellThumbnail shellThumbnail = shellObject.Thumbnail;
                shellThumbnail.CurrentSize = new System.Windows.Size(size, size);
                BitmapSource thumbnail = shellThumbnail.BitmapSource;
                thumbnail.Freeze();
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        private async Task<BitmapSource?> GetThumbnailAsync(string path)
        {
            return await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                {
                    return null;
                }
                int iconSize = (int)(Instance.IconSize * _windowsScalingFactor);
                DateTime lastWrite = DateTime.MinValue;
                try
                {
                    if (File.Exists(path)) lastWrite = File.GetLastWriteTimeUtc(path);
                    else if (Directory.Exists(path)) lastWrite = Directory.GetLastWriteTimeUtc(path);
                }
                catch { }

                if (Racks.Services.ThumbnailCache.TryGet(path, iconSize, lastWrite, out var cached) && cached != null)
                {
                    return cached;
                }

                BitmapSource? thumbnail = null;
                if (Path.GetExtension(path).ToLower() == ".svg")
                {
                    try
                    {
                        thumbnail = await LoadSvgThumbnailAsync(path, iconSize);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                    if (thumbnail != null)
                    {
                        Racks.Services.ThumbnailCache.Put(path, iconSize, lastWrite, thumbnail);
                    }
                    return thumbnail;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                bool isLink = ext == ".lnk" || ext == ".url";

                if (isLink)
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            thumbnail = GetThumbnail(path, iconSize);
                        });
                        if (thumbnail != null && Instance.ShowShortcutArrow)
                        {
                            var overlay = Racks.Services.ThumbnailCache.GetShortcutOverlay();
                            if (overlay != null)
                            {
                                thumbnail = Application.Current.Dispatcher.Invoke(() =>
                                {
                                    var visual = new DrawingVisual();
                                    using (var dc = visual.RenderOpen())
                                    {
                                        double scale = iconSize / Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight);
                                        double thumbnailWidth = thumbnail.PixelWidth * scale;
                                        double thumbnailHeight = thumbnail.PixelHeight * scale;

                                        double thumbnailX = (iconSize - thumbnailWidth) / 2.0;
                                        double thumbnailY = (iconSize - thumbnailHeight) / 2.0;

                                        dc.DrawImage(
                                            thumbnail,
                                            new Rect(
                                                thumbnailX,
                                                thumbnailY,
                                                thumbnailWidth,
                                                thumbnailHeight)
                                        );
                                        double overlayScale = (iconSize < 32 ? iconSize / 32.0 : 1.0);
                                        if (_windowsScalingFactor != 1.0)
                                        {
                                            overlayScale *= (1 / _windowsScalingFactor);
                                        }
                                        BitmapSource scaledOverlay = overlay;
                                        if (overlayScale != 1.0)
                                        {
                                            scaledOverlay = new TransformedBitmap(overlay, new ScaleTransform(overlayScale, overlayScale));
                                            scaledOverlay.Freeze();
                                        }
                                        double overlayX = thumbnailX;
                                        double overlayY = thumbnailY + thumbnailHeight - scaledOverlay.PixelHeight;
                                        dc.DrawImage(scaledOverlay,
                                            new Rect(
                                            overlayX,
                                            overlayY,
                                            scaledOverlay.PixelWidth,
                                            scaledOverlay.PixelHeight)
                                        );
                                    }

                                    var rtb = new RenderTargetBitmap(
                                        iconSize,
                                        iconSize,
                                        thumbnail.DpiX,
                                        thumbnail.DpiY,
                                        PixelFormats.Pbgra32);
                                    rtb.Render(visual);
                                    rtb.Freeze();
                                    return rtb;
                                });
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                else
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            thumbnail = GetThumbnail(path, iconSize);
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                if (thumbnail != null)
                {
                    Racks.Services.ThumbnailCache.Put(path, iconSize, lastWrite, thumbnail);
                    return thumbnail;
                }
                return null;
            });
        }

        private async Task<BitmapSource?> LoadSvgThumbnailAsync(string path, int iconSize)
        {
            try
            {
                // Never resolves anything outside the file (INV-IMPORT-4).
                var svgDocument = Racks.Util.SafeSvg.Open(path);

                using (var bitmap = svgDocument.Draw(iconSize, iconSize))
                {
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);

                        BitmapImage bitmapImage = null;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = ms;
                            bitmapImage.DecodePixelWidth = 64;
                            bitmapImage.DecodePixelHeight = 64;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                        });
                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load SVG thumbnail: {ex.Message}");
                return null;
            }
        }

        private void PathToBackButton_Click(object sender, RoutedEventArgs e)
        {
            var parentPath = Path.GetDirectoryName(_currentFolderPath) == Instance.Folder
                ? Instance.Folder : Path.GetDirectoryName(_currentFolderPath);
            Debug.WriteLine(parentPath);
            PathToBackButton.Visibility = parentPath == Instance.Folder
                ? Visibility.Collapsed : Visibility.Visible;
            Search.Margin = PathToBackButton.Visibility == Visibility.Visible ?
                   new Thickness(PathToBackButton.Width + 4, 0, 0, 0) : new Thickness(0, 0, 0, 0);
            FileItems.Clear();
            LoadFiles(parentPath!);
            _currentFolderPath = parentPath!;
            InitializeFileWatchers();
        }

        private void pickMissingFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select a folder",
                ShowNewFolderButton = true
            };
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var lastInstanceName = Instance.Name;
                FileItems.Clear();
                Instance.Folder = folderDialog.SelectedPath;
                Instance.IsFolderMissing = false;
                _currentFolderPath = Instance.Folder;
                Instance.Name = Path.GetFileName(folderDialog.SelectedPath);
                MainWindow._controller.WriteOverInstanceToKey(Instance, lastInstanceName);
                LoadFiles(_currentFolderPath);
                title.Text = Instance.TitleText == "" ? Instance.Name : Instance.TitleText;
                PathToBackButton.Visibility = Visibility.Collapsed;
                missingFolderGrid.Visibility = Visibility.Hidden;
                InitializeFileWatchers();
            }
        }
    }
}
