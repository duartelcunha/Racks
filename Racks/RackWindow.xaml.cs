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
    public partial class RackWindow : System.Windows.Window
    {
        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<Ellipse> visuals = new List<Ellipse>();

        private GrayscaleEffect _grayscaleEffect;
        ShellContextMenu scm = new ShellContextMenu();
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
        bool _dragMovingWinddow = false;


        
        #pragma warning disable CS0649
        private FileItem? _draggedItem;
#pragma warning restore CS0649
        
        private List<FileItem> _selectedItems = new List<FileItem>();
        
        private FileItem _itemUnderCursor;
        private FileItem _itemCurrentlyRenaming;
        string _dropIntoFolderPath;
        FrameworkElement _lastBorder;
        private bool _isRenaming = true;
        private bool _isTopmost = false;
        private bool _inHandleWindowMove = false;
        private bool _inSnapToGrid = false;
        private bool _isRenamingFromContextMenu = false;
        private bool _canChangeItemPosition = false;
        private bool _bringForwardForMove = false;
        private bool _isDragging = false;
        private bool _mouseIsOver;
        private bool _contextMenuIsOpen = false;
        private bool _fixIsOnBottomInit = true;
        private bool _didFixIsOnBottom = false;
        private bool _isMinimized = false;
        private bool _isIngrid = true;
        private bool _grabbedOnLeft;
        private int _snapDistance = 8;
        private int _gridSnapDistance = 10;
        private int _currentVD;
        int _oriPosX, _oriPosY;
        private bool _isBlack = true;
        
        private bool _canAutoClose = true;
        private bool _isLocked = false;
        private bool _isOnTop = false;
        private bool _isOnBottom = false;
        private bool _isLeftButtonDown = false;
        bool _canAnimate = true;
        private double _originalHeight;
        public int _previousItemPerRow = 0;
        private double _previousHeight = -1;
        public bool isMouseDown = false;
        private ICollectionView _collectionView;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private CancellationTokenSource loadFilesCancellationToken = new CancellationTokenSource();
        private CancellationTokenSource _changeIconSizeCts = new CancellationTokenSource();
        private CancellationTokenSource _adjustPositionCts;
        private Util.PhysicsBody _physics;
        // Drag velocity tracking for flick-to-throw (updated in Window_LocationChanged).
        private double _dragVelX, _dragVelY, _lastDragLeft, _lastDragTop;
        private long _lastDragTicks;
        private bool _isResizing;  // true while the user is resizing via the border (suppresses physics)

        // Placement mode (follow-the-cursor-then-click-to-drop) was removed. It wrote
        // screen coordinates to a window that SetAsDesktopChild reparents as a WS_CHILD of
        // the desktop (whose Left/Top are parent-client coords), so the rack never tracked
        // the cursor correctly, and its per-frame CompositionTarget.Rendering loop fought
        // the push physics and HandleWindowMove on the single UI thread, freezing the app.
        // New racks now simply appear at the cursor-centered position the creator sets in
        // Instance.PosX/PosY (converted to client coords by SetAsDesktopChild).

        ContextMenu contextMenu = new ContextMenu();
        MenuItem nameMenuItem;
        MenuItem dateModifiedMenuItem;
        MenuItem dateCreatedMenuItem;
        MenuItem fileTypeMenuItem;
        MenuItem fileSizeMenuItem;
        MenuItem ascendingMenuItem;
        MenuItem descendingMenuItem;
        MenuItem CustomItemOrderMenuItem;

        MenuItem folderOrderMenuItem;
        MenuItem folderFirstMenuItem;
        MenuItem folderLastMenuItem;
        MenuItem folderNoneMenuItem;

        private string _fileCount;
        private int _folderCount = 0;
        private DateTime _lastUpdated;
        private string _folderSize;

        private double _windowsScalingFactor;

        public enum SortBy
        {
            NameAsc = 1,
            NameDesc = 2,
            DateModifiedAsc = 3,
            DateModifiedDesc = 4,
            DateCreatedAsc = 5,
            DateCreatedDesc = 6,
            FileTypeAsc = 7,
            FileTypeDesc = 8,
            ItemSizeAsc = 9,
            ItemSizeDesc = 10,
        }

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
        private void MouseLeaveWindow(bool animateActiveColor = true)
        {
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 1;
            timer.Tick += (s, e) =>
            {
                if (animateActiveColor && !IsCursorWithinWindowBounds() && (GetAsyncKeyState(0x01) & 0x8000) == 0)
                {
                    _mouseIsOver = false;
                    AnimateActiveColor(Instance.AnimationSpeed);
                    if (Instance.HideTitleBarIconsWhenInactive)
                    {
                        TitleBarIconsFadeAnimation(false);
                    }
                    if (!_contextMenuIsOpen)
                    {
                        _selectedItems.Clear();
                        foreach (var fileItem in FileItems)
                        {
                            fileItem.IsSelected = false;
                            fileItem.Background = Brushes.Transparent;
                        }
                    }
                    if (!_isRenamingFromContextMenu)
                    {
                        _itemCurrentlyRenaming = null;
                    }
                }
                if (!IsCursorWithinWindowBounds() && (GetAsyncKeyState(0x01) & 0x8000) == 0) // Left mouse button is not down
                {

                    if (_canAutoClose)
                    {
                        FilterTextBox.Text = null;
                        //   FilterTextBox.Visibility = Visibility.Collapsed;
                    }
                    if (!_isTopmost)
                    {
                        this.SetNoActivate();
                    }
                    if (_didFixIsOnBottom) _fixIsOnBottomInit = false;

                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1)
                    };
                    timer.Tick += (s, args) =>
                    {
                        if (!_dragdropIntoFolder)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                FileListView.SelectedIndex = -1;
                                foreach (var item in FileListView.Items)
                                {
                                    var container = FileListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                                    if (container != null) container.IsSelected = false;
                                }
                            });
                            timer.Stop();
                        }
                    };
                    timer.Start();

                    if ((Instance.AutoExpandonCursor) && !_isMinimized && _canAutoClose)
                    {
                        AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
                        Minimize_MouseLeftButtonDown(null, null);
                        Task.Run(() =>
                        {
                            try
                            {
                                if (!_contextMenuIsOpen)
                                {
                                    _selectedItems.Clear();
                                    foreach (var fileItem in FileItems)
                                    {
                                        fileItem.IsSelected = false;
                                        fileItem.Background = Brushes.Transparent;
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
                    }
                }
                if (!_mouseIsOver)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }
        private void HandleRightClick(Window root, IntPtr lParam)
        {
            POINT pt = new POINT
            {
                X = (short)(lParam.ToInt32() & 0xFFFF),
                Y = (short)((lParam.ToInt32() >> 16) & 0xFFFF)
            };

            System.Windows.Point relativePt = root.PointFromScreen(new System.Windows.Point(pt.X, pt.Y));

            if (root.InputHitTest(relativePt) is DependencyObject hit)
            {
                var listView = FindParentOrChild<ListView>(hit);
                if (listView != null)
                {
                    var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                    {
                        RoutedEvent = UIElement.MouseRightButtonUpEvent,
                        Source = listView
                    };
                    FileListView_MouseRightButtonUp(listView, mouseArgs);
                }
            }
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
        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!(HwndSource.FromHwnd(hWnd).RootVisual is Window rootVisual))
                return IntPtr.Zero;
          
            if (msg == 0x0005) // WM_SIZE
            {
                if (_dragMovingWinddow)
                {
                    handled = true;
                    return -1;
                }
            }
            // The old WM_WINDOWPOSCHANGING handler here clamped the dragged rack to a
            // neighbor's edge (a solid collision wall). That directly prevented the
            // pushable-physics model (Window_LocationChanged) from ever seeing an
            // overlap, so racks bumped into an invisible wall instead of pushing each
            // other apart. Removed: push physics is now the single rack-vs-rack system.

            if (_isLeftButtonDown && _bringForwardForMove && msg == 0x0003) // WM_MOVE
            {
                BringFrameToFront(new WindowInteropHelper(this).Handle, true);
                _bringForwardForMove = false;
                return -1;
            }

            if (msg == 0x0084 && !Instance.IsLocked) // WM_NCHITTEST
            {
                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                System.Windows.Point pt = PointFromScreen(new System.Windows.Point(x, y));

                double cornerWidth = 14;
                double edgeWidth = 7;

                bool left = pt.X <= edgeWidth;
                bool right = pt.X >= ActualWidth - edgeWidth;
                bool top = pt.Y <= edgeWidth;
                bool bottom = pt.Y >= ActualHeight - edgeWidth;

                bool cornerLeft = pt.X <= cornerWidth;
                bool cornerRight = pt.X >= ActualWidth - cornerWidth;
                bool cornerTop = pt.Y <= cornerWidth;
                bool cornerBottom = pt.Y >= ActualHeight - cornerWidth;

                if (cornerTop && cornerLeft) { handled = true; return (IntPtr)13; } // HTTOPLEFT
                if (cornerTop && cornerRight) { handled = true; return (IntPtr)14; } // HTTOPRIGHT
                if (cornerBottom && cornerLeft) { handled = true; return (IntPtr)16; } // HTBOTTOMLEFT
                if (cornerBottom && cornerRight) { handled = true; return (IntPtr)17; } // HTBOTTOMRIGHT

                if (left) { handled = true; return (IntPtr)10; } // HTLEFT
                if (right) { handled = true; return (IntPtr)11; } // HTRIGHT
                if (top) { handled = true; return (IntPtr)12; } // HTTOP
                if (bottom) { handled = true; return (IntPtr)15; } // HTBOTTOM
            }
            if (msg == 0x020A && (GetAsyncKeyState(0x11) & 0x8000) != 0) // WM_MOUSEWHEEL && control down
            {
                _changeIconSizeCts.Cancel();
                _changeIconSizeCts = new CancellationTokenSource();
                var token = _changeIconSizeCts.Token;
                int delta = (short)((int)wParam >> 16);
                if (delta < 0) Instance.IconSize -= 4;
                else if (delta > 0) Instance.IconSize += 4;
                Task.Run(async () =>
                {
                    await Task.Delay(500, token);
                    if (!token.IsCancellationRequested)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LoadingProgressRingFade(true);
                        });
                        foreach (var item in FileItems)
                        {
                            item.Thumbnail = await GetThumbnailAsync(item.FullPath!);
                        }
                        Dispatcher.Invoke(() =>
                        {
                            FileWrapPanel.Items.Refresh();
                            Task.Run(async () =>
                            {
                                await Task.Delay(200, token);
                                Dispatcher.Invoke(() =>
                                {
                                    LoadingProgressRingFade(false);
                                });
                            });
                        });
                    }
                });
                handled = true;
                return 4;
            }


            else if (msg == 0x020A && Mouse.GetPosition(this).Y <= titleBar.Height)
            {

                int delta = (short)((int)wParam >> 16);
                if (delta > 0 && !_isTopmost)
                {
                    // TODO: redo this when proper PDI scaling is merged
                    Debug.WriteLine("Bring frame above other windows");
                    _isTopmost = true;
                    var dpi = VisualTreeHelper.GetDpi(this);

                    SetParent(new WindowInteropHelper(this).Handle, IntPtr.Zero);

                    SetWindowPos(new WindowInteropHelper(this).Handle,
                        IntPtr.Zero,
                        (int)(Instance.PosX * dpi.DpiScaleX),
                         (int)(Instance.PosY * dpi.DpiScaleY),
                        0,
                        0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

                    this.Height = Instance.Height;
                    this.Width = Instance.Width;

                    BackgroundType(true);
                    this.Activate();
                    this.Show();
                    this.Topmost = true;
                }
                else if (delta < 0 && _isTopmost)
                {
                    Debug.WriteLine("Push frame behind other windows");
                    _isTopmost = false;
                    this.Topmost = false;
                    BackgroundType(false);
                    SetAsDesktopChild();

                    HandleWindowMove(true);
                    // force redraw
                    this.Width += 1;
                    this.Width -= 1;
                }
            }

            if (msg == 0x0201) // WM_LBUTTONDOWN
            {
                _isLeftButtonDown = true;
                _bringForwardForMove = true;
                _grabbedOnLeft = Mouse.GetPosition(this).X < this.Width / 2;
            }
            if (msg == 0x0202) // WM_LBUTTONUP
            {
                _isLeftButtonDown = false;
                _bringForwardForMove = false;
            }
            if (msg == 0x0205) // WM_RBUTTONUP
            {
                HandleRightClick(rootVisual, lParam);
                handled = true;
            }
            if (msg == 0x0205) // WM_RBUTTONUP
            {
                int x = lParam.ToInt32() & 0xFFFF;
                int y = (lParam.ToInt32() >> 16) & 0xFFFF;
                var screenPoint = new System.Windows.Point(x, y);
                var relativePoint = FileWrapPanel.PointFromScreen(screenPoint);
                if (VisualTreeHelper.HitTest(FileWrapPanel, relativePoint) == null)
                {
                    var curPos = System.Windows.Forms.Cursor.Position;
                    try
                    {
                        var windowHelper = new WindowInteropHelper(this);
                        Point cursorPosition = System.Windows.Forms.Cursor.Position;
                        System.Windows.Point wpfPoint = new System.Windows.Point(cursorPosition.X, cursorPosition.Y);
                        Point drawingPoint = new Point((int)wpfPoint.X, (int)wpfPoint.Y);
                        DirectoryInfo folder = new DirectoryInfo(_currentFolderPath);
                        _contextMenuIsOpen = true;
                        scm.ContextMenuClosed += () =>
                        {
                            _contextMenuIsOpen = false;
                        };
                        if (_itemCurrentlyRenaming != null)
                        {
                            _itemCurrentlyRenaming.IsRenaming = false;
                        }
                        _lastRightClickedPath = _currentFolderPath;
                        scm.ShowContextMenu(windowHelper.Handle, new DirectoryInfo(_currentFolderPath), drawingPoint, true, RackProtectsFromDelete);
                        handled = true;
                    }
                    catch
                    {
                    }

                }
            }
            if (msg == 0x0100 && wParam.ToInt32() == 0x71) // F2 down
            {
                if (_itemUnderCursor != null)
                {
                    if (_itemCurrentlyRenaming != null)
                    {
                        _itemCurrentlyRenaming.IsRenaming = false;
                    }
                    _itemCurrentlyRenaming = _itemUnderCursor;
                    _itemCurrentlyRenaming.IsRenaming = true;
                    DependencyObject container;
                    if (Instance.ShowInGrid)
                    {
                        container = FileWrapPanel.ItemContainerGenerator.ContainerFromItem(_itemCurrentlyRenaming);
                    }
                    else
                    {
                        container = FileListView.ItemContainerGenerator.ContainerFromItem(_itemCurrentlyRenaming);
                        FileListView.SelectedItem = _itemCurrentlyRenaming;
                    }
                    var renameTextBox = FindParentOrChild<TextBox>(container);
                    renameTextBox!.Text = _itemCurrentlyRenaming.Name;
                    _isRenaming = true;
                    renameTextBox.Focus();

                    var text = renameTextBox.Text;
                    var dotIndex = text.LastIndexOf('.');
                    if (dotIndex <= 0) renameTextBox.SelectAll();
                    else renameTextBox.Select(0, dotIndex);
                }
            }

            // WM_SIZING only fires while resizing (not moving), so it's the reliable "this is
            // a resize" signal; WM_EXITSIZEMOVE ends the whole modal loop. Guarding physics on
            // _isResizing stops a resize from being read as a drag (false velocity) and stops
            // the collision loop from fighting a rack whose edges are being dragged.
            if (msg == 0x0232) _isResizing = false; // WM_EXITSIZEMOVE
            if (msg == 0x0214) // WM_SIZING
            {
                _isResizing = true;
                int edge = wParam.ToInt32();
                if (_isMinimized && (edge != 1 && edge != 2)) // block resizing except left or right edges
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    Interop.RECT currentRect;
                    Interop.GetWindowRect(hwnd, out currentRect);
                    Marshal.StructureToPtr(currentRect, lParam, true);
                    handled = true;
                    return IntPtr.Zero;
                }
                Interop.RECT rect = Marshal.PtrToStructure<Interop.RECT>(lParam);

                Instance.PosX = this.Left;
                Instance.PosY = this.Top;

                Instance.Width = this.Width;
                double height = rect.Bottom - rect.Top;
                if (height <= 102 && !_isMinimized)
                {
                    this.Height = 102;
                    rect.Bottom = rect.Top + 102;
                    Marshal.StructureToPtr(rect, lParam, true);
                    handled = true;
                    return (IntPtr)4;
                }
                else if (!_isMinimized && this.ActualHeight != titleBar.Height && _canAnimate)
                {
                    Instance.Height = this.ActualHeight;
                }

                if (Instance.LastAccesedToFirstRow)
                {
                    var wrapPanel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
                    if (wrapPanel != null)
                    {
                        double width = rect.Right - rect.Left;
                        double newWidth = rect.Right - rect.Left;

                        if (Instance.SnapWidthToIconWidth)
                        {
                            newWidth = Math.Round(width / wrapPanel.ItemWidth) * wrapPanel.ItemWidth + 4; // +4 margin
                            if (Instance.SnapWidthToIconWidth)
                            {
                                FileWrapPanel.Margin = new Thickness(6, 5, 0, 5);
                                newWidth += 15;
                            }
                        }
                        if (!Instance.SnapWidthToIconWidth)
                        {
                            FileWrapPanel.Margin = new Thickness(0, 0, 0, 0);
                        }
                        int newItemPerRow = (int)Math.Floor(newWidth / wrapPanel.ItemWidth);
                        if (_previousItemPerRow != newItemPerRow)
                        {
                            ItemPerRow = newItemPerRow;
                            FirstRowByLastAccessed(FileItems, Instance.LastAccessedFiles, ItemPerRow);
                            _previousItemPerRow = newItemPerRow;
                        }
                    }
                }

                if (Instance.SnapWidthToIconWidth)
                {
                    double width = rect.Right - rect.Left;
                    var item = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
                    double newWidth = Math.Round(width / item.ItemWidth) * item.ItemWidth + 4; // +4 margin

                    if (Instance.SnapWidthToIconWidth_PlusScrollbarWidth)
                    {
                        newWidth += 15;
                        FileWrapPanel.Margin = new Thickness(6, 5, 0, 5);
                    }
                    else
                    {
                        FileWrapPanel.Margin = new Thickness(0, 0, 0, 0);
                    }
                    if (width != newWidth)
                    {
                        int diff = (int)(newWidth - width);
                        int w = (int)wParam;

                        if (w == 1 || w == 5 || w == 7) // left sides
                        {
                            rect.Left -= diff;
                        }
                        if (w == 2 || w == 6 || w == 8) // right sides
                        {
                            rect.Right += diff;
                        }

                        Marshal.StructureToPtr(rect, lParam, true);
                        Instance.Width = this.Width;
                    }
                }
            }

            if (msg == 0x0005 && _isOnBottom) // WM_SIZE
            {
                double newHeight = (lParam.ToInt32() >> 16) & 0xFFFF;
                if (_previousHeight != -1 && _previousHeight != newHeight)
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;

                    var workingArea = Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;

                    Interop.GetWindowRect(hwnd, out RECT windowRect);
                    POINT pt = new POINT { X = windowRect.Left, Y = windowRect.Top };
                    ScreenToClient(GetParent(hwnd), ref pt);
                    double delta = newHeight - _previousHeight;
                    int newTop = (int)((pt.Y - delta) - windowRect.Bottom <= workingArea.Bottom ?
                        (int)(pt.Y -= (int)delta) :
                        Instance.Height - workingArea.Bottom - titleBar.Height);

                    if (delta > 0) // UP
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            Interop.SetWindowPos(hwnd, IntPtr.Zero, pt.X,
                                    newTop,
                                    0, 0,
                                   SWP_NOSIZE
                                  );

                        }, DispatcherPriority.Normal);
                    }
                    else
                    {
                        Interop.SetWindowPos(hwnd, IntPtr.Zero, pt.X,
                                newTop,
                                0, 0,
                               SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOREDRAW
                              );
                    }
                    if (this.Top + titleBar.Height > workingArea.Bottom)
                    {
                        // this.Top = workingArea.Bottom - 30;
                        _didFixIsOnBottom = true;
                        Interop.SetWindowPos(hwnd, IntPtr.Zero, pt.X,
                              (int)(workingArea.Bottom - titleBar.Height),
                              0, 0,
                             SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOREDRAW
                            );
                    }
                    if (_fixIsOnBottomInit && pt.Y + this.Height != workingArea.Bottom)
                    {
                        Interop.SetWindowPos(hwnd, IntPtr.Zero, pt.X,
                           (int)(workingArea.Bottom - this.Height + 1), // +1 pixel because otherwise it hovers  by 1 px above the desktop
                           0, 0,
                          SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOREDRAW
                         );
                    }
                }
                _previousHeight = newHeight;
                return 4;
            }

            if (msg == 70)
            {
                Interop.WINDOWPOS structure = Marshal.PtrToStructure<Interop.WINDOWPOS>(lParam);
                structure.flags |= 4U;
                Marshal.StructureToPtr<Interop.WINDOWPOS>(structure, lParam, false);
            }
            if (msg == 0x0003 &&  // WM_MOVE
                ((GetAsyncKeyState(0xA4) & 0x8000) == 0 && (GetAsyncKeyState(0xA5) & 0x8000) == 0)) // left and right alt isn't down
            {
                _isIngrid = false;

                // Only this rack repositions itself here; neighbors are moved (if at all)
                // by the push physics in Window_LocationChanged. Propagating HandleWindowMove
                // to neighbors was part of the removed docking system and caused the
                // mid-drag reentrancy storm that froze the app.
                HandleWindowMove(false);
            }
            if (_isLeftButtonDown &&
                ((GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0) && // left or right is alt down
                msg == 0x0003) // WM_MOVE
            {
                SnapToGrid();
            }

            return IntPtr.Zero;
        }
        private void SnapToGrid()
        {
            // SetWindowPos further down can synchronously re-fire WM_MOVE while Alt is
            // still held, re-entering this method (own guard, not _inHandleWindowMove,
            // since this deliberately calls HandleWindowMove(false) as its last step and
            // that call should still go through).
            if (_inSnapToGrid) return;
            _inSnapToGrid = true;
            try
            {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            Interop.RECT windowRect;
            Interop.GetWindowRect(hwnd, out windowRect);

            int windowLeft = windowRect.Left;
            int windowTop = windowRect.Top;
            int windowRight = windowRect.Right;
            int windowBottom = windowRect.Bottom;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            int newWindowLeft = windowLeft;
            int newWindowTop = windowTop;
            int newWindowBottom = windowBottom;
            foreach (var otherWindow in MainWindow._controller._subWindows)
            {
                if (otherWindow == this) continue;

                IntPtr otherHwnd = new WindowInteropHelper(otherWindow).Handle;
                Interop.RECT otherWindowRect;
                Interop.GetWindowRect(otherHwnd, out otherWindowRect);

                int otherLeft = otherWindowRect.Left;
                int otherTop = otherWindowRect.Top;
                int otherRight = otherWindowRect.Right;
                int otherBottom = otherWindowRect.Bottom;
                bool didSnap = false;
                if (Math.Abs(windowLeft - otherRight) <= _gridSnapDistance && Math.Abs(windowTop - otherTop) <= titleBar.Height)
                {
                    newWindowLeft = otherRight + _gridSnapDistance;
                    newWindowTop = otherTop;
                    if (_grabbedOnLeft) didSnap = true;
                }
                else if (Math.Abs(windowRight - otherLeft) <= _gridSnapDistance && Math.Abs(windowTop - otherTop) <= titleBar.Height)
                {
                    newWindowLeft = otherLeft - (windowRight - windowLeft) - _gridSnapDistance;
                    newWindowTop = otherTop;
                    if (_grabbedOnLeft) didSnap = true;
                }
                if (_grabbedOnLeft && !didSnap)
                {
                    if (Math.Abs(windowTop - otherBottom) <= _gridSnapDistance && Math.Abs(windowLeft - otherLeft) <= _snapDistance)
                    {
                        newWindowTop = otherBottom + _gridSnapDistance;
                        newWindowLeft = otherLeft;

                    }
                    else if (Math.Abs(windowBottom - otherTop) <= _gridSnapDistance && Math.Abs(windowLeft - otherLeft) <= _snapDistance)
                    {
                        newWindowTop = otherTop - (windowBottom - windowTop) - _gridSnapDistance;
                        newWindowLeft = otherLeft;
                    }
                }

                if (Math.Abs(windowRight - otherRight) <= _gridSnapDistance && Math.Abs(windowTop - otherBottom) <= _snapDistance)
                {
                    newWindowTop = otherBottom + _gridSnapDistance;
                    newWindowLeft = otherRight - (windowRight - windowLeft);
                }
                else if (Math.Abs(windowRight - otherRight) <= _gridSnapDistance && Math.Abs(windowBottom - otherTop) <= _snapDistance)
                {
                    newWindowTop = otherTop - (windowBottom - windowTop) - _gridSnapDistance;
                    newWindowLeft = otherRight - (windowRight - windowLeft);
                }
            }

            if (newWindowLeft != windowLeft || newWindowTop != windowTop || newWindowBottom != windowBottom)
            {
                POINT pt = new POINT { X = newWindowLeft, Y = newWindowTop };
                ScreenToClient(GetParent(hwnd), ref pt);
                SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, 0, 0,
                             SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);

                HandleWindowMove(false);
                _isIngrid = true;
            }
            else
            {
                _isIngrid = false;
            }
            }
            finally
            {
                _inSnapToGrid = false;
            }
        }
        public void HandleWindowMove(bool initWindow)
        {
            // Screen-edge snapping + this rack's own corner radii. SetWindowPos below can
            // synchronously re-fire WM_MOVE and re-enter this method; the guard bottoms
            // that out. (Rack-to-rack docking that used to also live here was removed;
            // racks now push each other apart via Window_LocationChanged instead.)
            if (_isTopmost || _inHandleWindowMove)
            {
                return;
            }
            _inHandleWindowMove = true;
            try
            {
            Interop.RECT windowRect;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Interop.GetWindowRect(hwnd, out windowRect);

            int windowLeft = windowRect.Left;
            int windowTop = windowRect.Top;
            int windowRight = windowRect.Right;
            int windowBottom = windowRect.Bottom;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            int newWindowLeft = windowLeft;
            int newWindowTop = windowTop;
            int newWindowBottom = windowBottom;


            var workingArea = Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;

            //if (Math.Abs(windowLeft - workingArea.Left) <= _snapDistance)
            //{
            //    newWindowLeft = (int)workingArea.Left;
            //    _isOnEdge = true;
            //}
            //else if (Math.Abs(windowRight - workingArea.Right) <= _snapDistance)
            //{
            //    newWindowLeft = (int)(workingArea.Right - (windowRight - windowLeft));
            //    _isOnEdge = true;
            //}
            //else
            //{
            //    _isOnEdge = false;
            //}
            // Debug.WriteLine(windowBottom + " " + (workingArea.Bottom <= windowBottom));
            if (_isLeftButtonDown || initWindow)
            {
                POINT pt = new POINT { X = newWindowLeft, Y = newWindowTop };
                ScreenToClient(GetParent(hwnd), ref pt);
                windowTop = pt.Y;
                windowBottom = pt.Y + (windowBottom - windowTop);
                if (Math.Abs(windowTop - workingArea.Top) <= _snapDistance)
                {
                    newWindowTop = (int)workingArea.Top;
                    WindowBackground.CornerRadius = new CornerRadius(0, 0, 5, 5);
                    _isOnBottom = false;
                    _isOnTop = true;
                }
                else if (Math.Abs(windowBottom - workingArea.Bottom) - 2 <= _snapDistance
                   || (Math.Abs(windowBottom - workingArea.Bottom + Instance.Height - titleBar.Height) - 2 <= _snapDistance && initWindow)
                   )
                {
                    newWindowTop = (int)(workingArea.Bottom - (windowBottom - windowTop));
                    newWindowBottom = (int)workingArea.Bottom;
                    WindowBackground.CornerRadius = new CornerRadius(5, 5, 0, 0);
                    _isOnTop = false;
                    _isOnBottom = true;
                }
                else if (!_isOnBottom)
                {
                    _isOnTop = false;
                    WindowBackground.CornerRadius = new CornerRadius(5);
                    titleBar.CornerRadius = new CornerRadius(5, 5, 0, 0);
                }
                if (workingArea.Bottom <= windowBottom)
                {
                    newWindowBottom = (int)workingArea.Bottom;
                    WindowBackground.CornerRadius = new CornerRadius(5, 5, 0, 0);
                    _isOnTop = false;
                    _isOnBottom = true;
                }
                else if (_isLeftButtonDown)
                {
                    _isOnBottom = false;
                }
                if (Math.Abs(windowLeft - workingArea.Left) <= _snapDistance)
                {
                    newWindowLeft = workingArea.Left;
                }
                else if (Math.Abs(workingArea.Right - windowRight) <= _snapDistance)
                {
                    newWindowLeft = (int)(workingArea.Right - this.ActualWidth);
                }
            }
            // Rack-to-rack edge docking used to live here: it snapped the dragged rack's
            // edges to nearby racks (WonRight/WonLeft) and merged their corner radii into
            // a seamless panel. It fought the pushable-physics model (each dock moved a
            // neighbor, which re-fired WM_MOVE and re-ran this whole pass on every rack,
            // saturating the UI thread mid-drag so the app couldn't even be quit). Removed:
            // racks now push each other apart (Window_LocationChanged) and never dock, so
            // this only has to keep THIS rack's own corners correct against the screen edge.
            if (!_isMinimized)
            {
                if (_isOnBottom)
                {
                    WindowBorder.CornerRadius = new CornerRadius(5, 5, 0, 0);
                    WindowBackground.CornerRadius = WindowBorder.CornerRadius;
                    titleBar.CornerRadius = new CornerRadius(5, 5, 5, 5);
                }
                else
                {
                    WindowBorder.CornerRadius = new CornerRadius(
                        topLeft: _isOnTop ? 0 : 5,
                        topRight: _isOnTop ? 0 : 5,
                        bottomRight: 5,
                        bottomLeft: 5
                    );
                    WindowBackground.CornerRadius = WindowBorder.CornerRadius;
                    titleBar.CornerRadius = new CornerRadius(
                        topLeft: WindowBorder.CornerRadius.TopLeft,
                        topRight: WindowBorder.CornerRadius.TopRight,
                        bottomRight: 0,
                        bottomLeft: 0
                    );
                }
            }
            else
            {
                if (_isOnBottom)
                {
                    WindowBorder.CornerRadius = new CornerRadius(5, 5, 0, 0);
                }
                else
                {
                    WindowBorder.CornerRadius = new CornerRadius(
                        topLeft: _isOnTop ? 0 : 5,
                        topRight: _isOnTop ? 0 : 5,
                        bottomRight: 5,
                        bottomLeft: 5
                    );
                }
                WindowBackground.CornerRadius = WindowBorder.CornerRadius;
                titleBar.CornerRadius = WindowBorder.CornerRadius;
            }

            if ((initWindow && _isOnBottom) ||
                (!_isIngrid && !_isOnBottom
                    && (newWindowLeft != windowLeft || newWindowTop != windowTop || newWindowBottom != windowBottom && !_isLeftButtonDown)))
            {
                POINT pt = new POINT { X = newWindowLeft, Y = newWindowTop };
                ScreenToClient(GetParent(hwnd), ref pt);
                SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, 0, 0,
                             SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            }
            finally
            {
                _inHandleWindowMove = false;
            }
        }

        public void SetCornerRadius(Border border, double topLeft, double topRight, double bottomLeft, double bottomRight)
        {
            border.CornerRadius = new CornerRadius(topLeft, topRight, bottomLeft, bottomRight);
        }

        private void SetAsDesktopChild()
        {
            // Explorer briefly has no SHELLDLL_DefView while it's restarting (a common
            // user troubleshooting step). This used to retry with no delay and no cap -
            // a 100%-CPU spin on the UI thread that never gave up, hanging the whole
            // app since WPF has one dispatcher. Retry with a real pause instead, and
            // give up after a bounded wait rather than spinning forever.
            const int maxAttempts = 10;
            for (int attempt = 0; shellView == IntPtr.Zero && attempt < maxAttempts; attempt++)
            {
                EnumWindows((tophandle, _) =>
                {
                    IntPtr shellViewIntPtr = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellViewIntPtr != IntPtr.Zero)
                    {
                        shellView = shellViewIntPtr;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (shellView == IntPtr.Zero) Thread.Sleep(1000);
            }
            if (shellView == IntPtr.Zero) throw new InvalidOperationException("SHELLDLL_DefView not found.");

            var interopHelper = new WindowInteropHelper(this);
            interopHelper.EnsureHandle();
            IntPtr hwnd = interopHelper.Handle;
            SetParent(hwnd, shellView);

            int style = (int)GetWindowLong(hwnd, GWL_STYLE);
            style &= ~WS_POPUP; // remove flag, to make sure it doesn't interfere
            style |= WS_CHILD; // add flag
            SetWindowLong(hwnd, GWL_STYLE, style);

            // convert coords to parent-relative coords
            uint dpi = GetDpiForWindow(hwnd);
            _windowsScalingFactor = dpi / 96.0;
            POINT pt = new POINT
            {
                X = (int)(Instance.PosX * _windowsScalingFactor),
                Y = (int)(Instance.PosY * _windowsScalingFactor)
            };
            ScreenToClient(shellView, ref pt);

            SetWindowPos(hwnd, IntPtr.Zero,
                         pt.X, pt.Y,
                         0, 0,
                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public async Task AdjustPositionAsync()
        {
            _adjustPositionCts?.Cancel();
            if (isMouseDown) return;

            _adjustPositionCts = new CancellationTokenSource();
            var token = _adjustPositionCts.Token;
            var interopHelper = new WindowInteropHelper(this);
            interopHelper.EnsureHandle();
            IntPtr hwnd = interopHelper.Handle;
            double posX = Instance.PosX;
            double posY = Instance.PosY;

            try
            {
                await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return;

                    uint dpi = GetDpiForWindow(hwnd);
                    _windowsScalingFactor = dpi / 96.0;

                    POINT pt = new POINT
                    {
                        X = (int)(posX * _windowsScalingFactor),
                        Y = (int)(posY * _windowsScalingFactor)
                    };

                    if (token.IsCancellationRequested) return;
                    ScreenToClient(shellView, ref pt);

                    SetWindowPos(hwnd, IntPtr.Zero,
                                 pt.X, pt.Y,
                                (int)Instance.Width, (int)Instance.Height,
                                  SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }, token);
            }
            catch { }
        }
        public async void AdjustPosition()
        {
            SetParent(hwnd, IntPtr.Zero);
            SetAsDesktopChild();
            if (Instance.Minimized)
            {
                this.Height = titleBar.Height;
            }
            RescueIfOffscreen();
            var interopHelper = new WindowInteropHelper(this);
            interopHelper.EnsureHandle();
            IntPtr _hwnd = interopHelper.Handle;
            double currentScale = GetDpiForWindow(_hwnd) / 96.0;
            if (_windowsScalingFactor != currentScale)
            {
                _windowsScalingFactor = currentScale;
                foreach (var item in FileItems)
                {
                    item.Thumbnail = await GetThumbnailAsync(item.FullPath!);
                }
            }
        }

        // If the saved position is no longer on any working area (monitor unplugged,
        // laptop undocked, resolution changed), snap the rack back to the primary
        // monitor instead of leaving it stranded somewhere the user can't reach.
        private void RescueIfOffscreen()
        {
            try
            {
                var titleRect = new System.Drawing.Rectangle(
                    (int)this.Left, (int)this.Top,
                    Math.Max(40, (int)this.Width),
                    Math.Max(20, (int)titleBar.Height));

                bool anyVisible = false;
                foreach (var screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(titleRect)) { anyVisible = true; break; }
                }
                if (anyVisible) return;

                var primary = Screen.PrimaryScreen!.WorkingArea;
                double scale = _windowsScalingFactor > 0 ? _windowsScalingFactor : 1.0;
                this.Left = (primary.Left + 80) / scale;
                this.Top = (primary.Top + 80) / scale;
                Instance.PosX = this.Left;
                Instance.PosY = this.Top;
            }
            catch (Exception ex) { Debug.WriteLine($"RescueIfOffscreen failed: {ex.Message}"); }
        }

        public void SetAsToolWindow()
        {
            WindowInteropHelper wih = new WindowInteropHelper(this);
            IntPtr dwNew = new IntPtr(((long)Interop.GetWindowLong(wih.Handle, Interop.GWL_EXSTYLE).ToInt32() | 128L | 0x00200000L) & 4294705151L);
            Interop.SetWindowLong((nint)new HandleRef(this, wih.Handle), Interop.GWL_EXSTYLE, dwNew);
        }
        public void SetNoActivate()
        {
            if (_isTopmost)
            {
                return;
            }
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr style = Interop.GetWindowLong(hwnd, Interop.GWL_EXSTYLE);
            IntPtr newStyle = new IntPtr(style.ToInt64() | Interop.WS_EX_NOACTIVATE);
            Interop.SetWindowLong(hwnd, Interop.GWL_EXSTYLE, newStyle);
        }
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            
            int exStyle = (int)Interop.GetWindowLong(hwnd, Interop.GWL_EXSTYLE);
            Interop.SetWindowLong(hwnd, Interop.GWL_EXSTYLE, exStyle | Interop.WS_EX_NOACTIVATE);
            WindowChrome.SetWindowChrome(this, Instance.IsLocked ?
                new WindowChrome
                {
                    ResizeBorderThickness = new Thickness(0),
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(0)
                } :
                new WindowChrome
                {
                    GlassFrameThickness = new Thickness(0),
                    CaptionHeight = 0,
                    ResizeBorderThickness = new Thickness(10), // Increased from 5 to 10 for easier grab
                    CornerRadius = new CornerRadius(0)
                }
            );
            KeepWindowBehind();
            SetAsDesktopChild();
            SetNoActivate();
            SetAsToolWindow();
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);
            MouseLeaveWindow(false);
            FileListView.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_StatusChanged;
            FileWrapPanel.ItemContainerGenerator.StatusChanged += FileWrapPanel_GeneratorStatusChanged;
        }

        // Find the AnimatedTilePanel once it's materialized and wire up its drag
        // events. Idempotent — multiple StatusChanged fires won't double-subscribe.
        private bool _tilePanelWired;
        private void FileWrapPanel_GeneratorStatusChanged(object sender, EventArgs e)
        {
            if (_tilePanelWired) return;
            if (FileWrapPanel.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
            var panel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
            if (panel == null) return;
            panel.ItemMoveRequested += OnTilePanelItemMoveRequested;
            panel.DragCompleted += OnTilePanelDragCompleted;
            panel.OutgoingDragRequested += OnTilePanelOutgoingDragRequested;
            _tilePanelWired = true;
        }

        // Quick-drag (no long-press) on a tile → start an OLE outgoing drag so
        // the user can drop the file onto Explorer, another rack, etc. Mirrors
        // what FileItem_LeftMouseButtonDown used to do inline.
        private void OnTilePanelOutgoingDragRequested(UIElement child)
        {
            if (child == null) return;
            if (child is not ContentPresenter cp) return;
            var fileItem = cp.Content as FileItem ?? cp.DataContext as FileItem;
            if (fileItem?.FullPath == null) return;
            try
            {
                _isDragging = true;
                bool desktopHadFileBefore = DesktopHasFile(fileItem.Name);
                var data = new DataObject(DataFormats.FileDrop, new[] { fileItem.FullPath });
                var effect = DragDrop.DoDragDrop(child, data, DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);

                if (effect != DragDropEffects.None)
                {
                    // Where the pointer released - drag-out should land the item there.
                    Racks.Util.Interop.GetCursorPos(out Racks.Util.Interop.POINT dropPt);

                    // Desktop rack: the item's real file lives in the RacksWorkspace sandbox.
                    // We do the move ourselves (delete-workspace + keep-desktop / or physically
                    // move) so it never ends up duplicated, and drop it exactly at the cursor.
                    HandleDesktopRackDragOut(fileItem.FullPath, fileItem.Name, desktopHadFileBefore, dropPt);

                    // Virtual (shortcut) rack: if Explorer moved the shortcut out, remove the
                    // orphan from our sandbox.
                    if (effect == DragDropEffects.Move && Instance.IsShortcutsOnly && !string.IsNullOrEmpty(Instance.Folder))
                    {
                        string shortcutPath = System.IO.Path.Combine(Instance.Folder, fileItem.Name);
                        if (System.IO.File.Exists(shortcutPath))
                        {
                            try { System.IO.File.Delete(shortcutPath); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Outgoing drag failed: {ex.Message}"); }
            finally { _isDragging = false; }
        }

        // Snapshot, taken BEFORE an outgoing drag starts, of whether a same-named file
        // already sat on the desktop. Passed to HandleDesktopRackDragOut so a pre-existing
        // desktop file can't be mistaken for "the item was dropped here".
        private bool DesktopHasFile(string fileName)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string p = Path.Combine(desktopPath, fileName);
                return File.Exists(p) || Directory.Exists(p);
            }
            catch { return false; }
        }

        // Reconcile a drag-out from a desktop rack. For a DesktopFilterRack the item's real
        // file lives in RacksWorkspace; dropping it on the desktop makes Explorer copy it
        // there, leaving a duplicate (desktop copy + workspace original still claimed by the
        // rack). We only reconcile when the item NEWLY appeared on the desktop (it wasn't
        // there before the drag) - that's the reliable signal it was dropped on the desktop
        // rather than into another app. A same-named file that already existed before the
        // drag is NOT treated as our drop, so we never delete the wrong file.
        private void HandleDesktopRackDragOut(string workspaceFullPath, string fileName, bool desktopHadFileBefore, Racks.Util.Interop.POINT dropPt)
        {
            if (!Instance.IsDesktopFilterRack || string.IsNullOrEmpty(workspaceFullPath)) return;
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string desktopTarget = Path.Combine(desktopPath, fileName);
                bool newOnDesktop = !desktopHadFileBefore && DesktopHasFile(fileName);

                if (newOnDesktop)
                {
                    // Explorer already COPIED it to the desktop. Delete the sandbox original so
                    // only the desktop copy remains (no duplicate), then drop the rack's claim.
                    try
                    {
                        if (File.Exists(workspaceFullPath)) File.Delete(workspaceFullPath);
                        else if (Directory.Exists(workspaceFullPath)) Directory.Delete(workspaceFullPath, true);
                    }
                    catch (Exception ex) { Debug.WriteLine($"Drag-out workspace cleanup failed: {ex.Message}"); }
                }
                else
                {
                    // Nothing new landed on the desktop. If the drop was ON the desktop area
                    // (not into another app), physically move the file out of the sandbox to
                    // the desktop ourselves; otherwise leave it in the rack untouched.
                    if (!DroppedOnDesktop(dropPt)) return;
                    if (File.Exists(desktopTarget) || Directory.Exists(desktopTarget)) return; // name clash: bail safely

                    if (Util.SafeMove.TryMove(workspaceFullPath, desktopTarget, out _) != Util.SafeMove.Result.Moved)
                        return;
                }

                // Position the returned item exactly where the pointer released.
                try { Util.DesktopIconPositioner.SetDesktopIconPosition(desktopTarget, dropPt.X, dropPt.Y); }
                catch { }

                // Drop the rack's claim so the item stops showing in the rack.
                if (Instance.AssignedFiles != null && Instance.AssignedFiles.Remove(fileName))
                {
                    MainWindow._controller.WriteInstanceToKey(Instance);
                }

                LoadFiles(_currentFolderPath);
            }
            catch (Exception ex) { Debug.WriteLine($"HandleDesktopRackDragOut failed: {ex.Message}"); }
        }

        // True if the drop point is over the desktop (the wallpaper / SHELLDLL_DefView),
        // not over another application window. Used so drag-out only "returns to desktop"
        // when the user actually dropped on the desktop.
        private static bool DroppedOnDesktop(Racks.Util.Interop.POINT pt)
        {
            try
            {
                IntPtr hwnd = Interop.WindowFromPoint(pt);
                if (hwnd == IntPtr.Zero) return false;
                var sb = new System.Text.StringBuilder(64);
                for (IntPtr h = hwnd; h != IntPtr.Zero; h = Interop.GetParent(h))
                {
                    Interop.GetClassName(h, sb, sb.Capacity);
                    string cls = sb.ToString();
                    if (cls == "SHELLDLL_DefView" || cls == "Progman" || cls == "WorkerW") return true;
                }
                return false;
            }
            catch { return false; }
        }

        // Called synchronously by the panel as the dragged tile crosses into a new
        // slot. Mutate FileItems (the source ObservableCollection) so the items
        // generator shuffles the visual containers to match — no manual children
        // mutation in the panel.
        private void OnTilePanelItemMoveRequested(int from, int to)
        {
            if (from < 0 || to < 0) return;
            if (from >= FileItems.Count || to >= FileItems.Count) return;
            if (from == to) return;
            try { FileItems.Move(from, to); }
            catch (Exception ex) { Debug.WriteLine($"Tile drag move failed: {ex.Message}"); }
        }

        // Called once when the drag drops. Persist the full visual order as the
        // new custom order so it survives a relaunch. We renumber every item
        // (not just the dragged one) because indexes after the drop point have
        // all shifted; AddToCustomOrder's one-at-a-time approach would lose them.
        private void OnTilePanelDragCompleted()
        {
            if (!Instance.EnableCustomItemsOrder) return;
            try
            {
                var newOrder = new List<Tuple<string, string>>(FileItems.Count);
                for (int i = 0; i < FileItems.Count; i++)
                {
                    var fi = FileItems[i];
                    if (fi?.FullPath == null) continue;
                    string id = GetFileId(fi.FullPath).ToString();
                    newOrder.Add(new Tuple<string, string>(id, i.ToString()));
                }
                Instance.CustomOrderFiles = newOrder;
            }
            catch (Exception ex) { Debug.WriteLine($"Persist custom order failed: {ex.Message}"); }
        }

        private void ItemContainerGenerator_StatusChanged(object sender, EventArgs e)
        {
            if (FileListView.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                foreach (var item in FileListView.Items)
                {
                    var container = FileListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                    if (container != null)
                    {
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
        public RackWindow(Instance instance)
        {
            InitializeComponent();
            this.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
            this.MinWidth = 98;
            this.Loaded += MainWindow_Loaded;
            this.SourceInitialized += MainWindow_SourceInitialized!;
            hwnd = new WindowInteropHelper(this).Handle;
            this.StateChanged += (sender, args) =>
            {
                this.WindowState = WindowState.Normal;
            };

            Instance = instance;
            ViewModel = new RackViewModel(instance, MainWindow._controller);
            scm.DeleteBlocked += OnRackDeleteBlocked;
            scm.OpenInExplorerRequested += OnOpenInExplorerRequested;

            // Ice-rink physics body for this rack. Anchored (never slid) while locked or
            // pinned-topmost; persists its position once it comes to rest.
            _physics = new Util.PhysicsBody
            {
                Window = this,
                IsAnchored = () => _isLocked || _isTopmost || Instance.isWindowClosing,
                OnSettled = () => { Instance.PosX = this.Left; Instance.PosY = this.Top; }
            };
            Util.RackPhysics.Register(_physics);

            _grayscaleEffect = (GrayscaleEffect)FindResource("ImageGrayscaleEffect");
            _grayscaleEffect.Strength = Instance.GrayScaleEnabled ? Instance.MaxGrayScaleStrength : 0;

            this.Width = instance.Width;
            this.Opacity = Instance.IdleOpacity;
            _currentFolderPath = instance.Folder;
            _isLocked = instance.IsLocked;
            _oriPosX = (int)instance.PosX;
            _oriPosY = (int)instance.PosY;
            this.Top = instance.PosY;
            this.Left = instance.PosX;

            title.FontSize = Instance.TitleFontSize;
            title.TextWrapping = TextWrapping.Wrap;
            double titleBarHeight = Math.Max(30, Instance.TitleFontSize * 1.5);
            titleBar.Height = titleBarHeight;

            double scrollViewerMargin = titleBarHeight + 5;
            scrollViewer.Margin = new Thickness(0, scrollViewerMargin, 0, 0);

            if ((int)instance.Height <= titleBar.Height) _isMinimized = true;
            if (instance.Minimized)
            {
                _isMinimized = instance.Minimized;
                this.Height = titleBarHeight;
            }
            else
            {
                this.Height = instance.Height;
            }
            titleStackPanel.MouseEnter += (s, e) => AnimateSymbolIcon(frameTypeSymbol, Instance.TitleFontSize, 1, 5);
            titleStackPanel.MouseLeave += (s, e) => AnimateSymbolIcon(frameTypeSymbol, 0, 0, 0);

            
            // Restore persistent pin-to-top.
            if (Instance.PinToTop)
            {
                _isTopmost = true;
                this.Topmost = true;
            }
            // Recognize sandboxed virtual racks. Match either the current Racks
            // sandbox or the legacy DeskFrame AppData path so users upgrading from
            // the old build keep working racks.
            string legacyAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFrame");
            if (!string.IsNullOrEmpty(instance.Folder)
                && (InstanceController.IsInsideVirtualFramesRoot(instance.Folder)
                    || instance.Folder.StartsWith(legacyAppDataPath, StringComparison.OrdinalIgnoreCase)))
            {
                Instance.IsShortcutsOnly = true;
                Instance.ShowShortcutArrow = false;
            }
            if (instance.Folder == "empty")
            {
                showFolder.Visibility = Visibility.Hidden;
                addFolder.Visibility = Visibility.Visible;
            }
            else if (!instance.IsFolderMissing)
            {
                LoadingProgressRing.Visibility = Visibility.Visible;
                LoadFiles(instance.Folder);
                title.Text = Instance.TitleText == "" ? Instance.Name : Instance.TitleText;

                DataContext = this;
            }
            else if (instance.IsFolderMissing)
            {
                title.Text = Instance.TitleText == "" ? Instance.Name : Instance.TitleText;
                DataContext = this;
                missingFolderGrid.Visibility = Visibility.Visible;
            }
            InitializeFileWatchers();

            if (Instance.SnapWidthToIconWidth_PlusScrollbarWidth)
            {
                FileWrapPanel.Margin = new Thickness(6, 5, 0, 5);
            }
            else
            {
                FileWrapPanel.Margin = new Thickness(0, 0, 0, 0);
            }

            _collectionView = CollectionViewSource.GetDefaultView(FileItems);
            _originalHeight = Instance.Height;
            titleBar.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(Instance.TitleBarColor));
            title.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(Instance.TitleTextColor));
            titleBarIcons.Opacity = Instance.HideTitleBarIconsWhenInactive ? 0 : 1;

            if (Instance.TitleFontFamily != null)
            {
                try
                {
                    title.FontFamily = new System.Windows.Media.FontFamily(Instance.TitleFontFamily);
                }
                catch
                {
                }
            }
            if (Instance.ItemFontFamily != null)
            {
                try
                {
                    this.Resources["ItemFont"] = new System.Windows.Media.FontFamily(Instance.ItemFontFamily);
                }
                catch
                {
                }
            }
            if (Instance.ShowInGrid)
            {
                showFolder.Visibility = Visibility.Visible;
                showFolderInGrid.Visibility = Visibility.Hidden;
            }
            else
            {
                showFolder.Visibility = Visibility.Hidden;
                showFolderInGrid.Visibility = Visibility.Visible;
            }
            ChangeBackgroundOpacity(Instance.Opacity);
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            double clickY = e.GetPosition(this).Y;
            // Only the title-bar strip acts as a drag handle. Clicks inside the
            // items area used to call DragMove() unconditionally, which meant a
            // missed long-press on a tile dragged the whole rack instead of
            // letting AnimatedTilePanel handle it. Cap drag to titleBar.Height.
            bool inTitleBar = clickY <= titleBar.Height;
            if (e.ClickCount == 2)
            {
                if (inTitleBar)
                {
                    Minimize_MouseLeftButtonDown(null, null);
                    return;
                }
            }
            else if (e.ButtonState == MouseButtonState.Pressed && inTitleBar)
            {
                KeepWindowBehind();
                if (!_isLocked)
                {
                    _dragMovingWinddow = true;
                    _dragVelX = _dragVelY = 0;
                    _lastDragLeft = this.Left; _lastDragTop = this.Top;
                    _lastDragTicks = DateTime.UtcNow.Ticks;
                    this.DragMove(); // blocks until the button is released

                    // Flick-to-throw: if the rack was still moving when released, hand its
                    // velocity to the physics loop so it glides on with the same ice-rink
                    // friction/bounce as a push.
                    _dragMovingWinddow = false;
                    if (_physics != null && !_isLocked && !_isTopmost)
                    {
                        // Only throw if the rack was ACTUALLY moving at the moment of release.
                        // _dragVelX/Y is only refreshed in Window_LocationChanged, which stops
                        // firing when the mouse holds still - so it keeps the last non-zero speed
                        // from before the pause and would fling a rack the user just parked. If
                        // there's been no movement for a beat, the rack is at rest: zero it out.
                        double sinceMove = (DateTime.UtcNow.Ticks - _lastDragTicks) / (double)TimeSpan.TicksPerSecond;
                        if (sinceMove > 0.07) { _dragVelX = _dragVelY = 0; }
                        _physics.Vx = _dragVelX;
                        _physics.Vy = _dragVelY;
                        if (_physics.Moving) Util.RackPhysics.Kick();
                    }
                }
                return;
            }
        }
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragMovingWinddow = false;
        }
        private void AnimateSymbolIcon(UIElement target, double widthTo, double opacityTo, double marginTo)
        {
            var marginAnimation = new ThicknessAnimation
            {
                To = new Thickness(0, 0, marginTo, 0),
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            var widthAnimation = new DoubleAnimation
            {
                To = widthTo,
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var opacityAnimation = new DoubleAnimation
            {
                To = opacityTo,
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            target.BeginAnimation(FrameworkElement.MarginProperty, marginAnimation);
            target.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
            target.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        private void AnimateChevron(bool flip, bool onLoad, double animationSpeed)
        {


            var rotateTransform = ChevronRotate;

            int angleToAnimateTo;
            int duration;
            if (onLoad)
            {
                angleToAnimateTo = flip ? 0 : 180;
                duration = 10;
            }
            else
            {
                angleToAnimateTo = (rotateTransform.Angle == 180) ? 0 : 180;
                duration = (int)(200 / animationSpeed);
            }
            if (_isLocked) duration = (int)(200 / animationSpeed);

            var rotateAnimation = new DoubleAnimation
            {
                From = rotateTransform.Angle,
                To = angleToAnimateTo,
                Duration = (animationSpeed == 0) ?
                    TimeSpan.FromMilliseconds(40) :
                    TimeSpan.FromMilliseconds(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            _canAnimate = false;
            rotateAnimation.Completed += (s, e) => _canAnimate = true;

            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
        }

        private void Minimize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {

            AnimateChevron(_isMinimized, false, Instance.AnimationSpeed);
            if (showFolder.Visibility == Visibility.Hidden && showFolderInGrid.Visibility == Visibility.Hidden)
            {
                return;
            }
            if (!_isMinimized)
            {
                _originalHeight = this.ActualHeight;
                _isMinimized = true;
                Instance.Minimized = true;
                // Debug.WriteLine("minimize: " + Instance.Height);
                AnimateWindowHeight(titleBar.Height, Instance.AnimationSpeed);
            }
            else
            {
                WindowBackground.CornerRadius = new CornerRadius(
                         topLeft: WindowBackground.CornerRadius.TopLeft,
                         topRight: WindowBackground.CornerRadius.TopRight,
                         bottomRight: 5.0,
                         bottomLeft: 5.0
                      );
                _isMinimized = false;
                Instance.Minimized = false;

                // Debug.WriteLine("unminimize: " + Instance.Height);
                AnimateWindowHeight(Instance.Height, Instance.AnimationSpeed);
            }
            HandleWindowMove(false);
        }

        private void ToggleFileExtension_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleFileExtension();
            LoadFiles(_currentFolderPath);
            UpdateFileExtensionIcon();
        }

        private void ToggleHiddenFiles_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleHiddenFiles();
            LoadFiles(_currentFolderPath);
            UpdateHiddenFilesIcon();
        }
        private void OpenFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_currentFolderPath) { UseShellExecute = true });
            }
            catch
            { }
        }
        private void UpdateFileExtensionIcon()
        {
            if (Instance.ShowFileExtension)
            {
                FileExtensionIcon.Symbol = SymbolRegular.DocumentSplitHint24;
            }
            else
            {
                FileExtensionIcon.Symbol = SymbolRegular.DocumentSplitHintOff24;
            }
        }

        private void UpdateHiddenFilesIcon()
        {
            if (Instance.ShowHiddenFiles)
            {
                HiddenFilesIcon.Symbol = SymbolRegular.Eye24;
            }
            else
            {
                HiddenFilesIcon.Symbol = SymbolRegular.EyeOff24;
            }
        }
        public void AnimateWindowOpacity(double value, double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            var animation = new DoubleAnimation
            {
                To = value,
                Duration = animationSpeed == 0 ?
                    TimeSpan.FromSeconds(0.1) :
                    TimeSpan.FromSeconds(0.2 / animationSpeed),
            };
            this.BeginAnimation(OpacityProperty, animation);
        }
        public void AnimateGrayScale(double oldValue, double newValue)
        {
            var animation = new DoubleAnimation
            {
                From = oldValue,
                To = newValue,
                Duration = Instance.DisableAnimations ? TimeSpan.Zero : TimeSpan.FromSeconds(0.1),
                FillBehavior = FillBehavior.HoldEnd
            };
            _grayscaleEffect.BeginAnimation(GrayscaleEffect.StrengthProperty, animation);
        }
        public void AnimateActiveColor(double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            if (Instance.ActiveBackgroundEnabled
                || Instance.ActiveBorderEnabled
                || Instance.ActiveTitleTextEnabled
                || Instance.GrayScaleEnabled && Instance.GrayScaleEnabled_InactiveOnly)
            {
                _mouseIsOver = IsCursorWithinWindowBounds();
            }
            if (Instance.GrayScaleEnabled && Instance.GrayScaleEnabled_InactiveOnly)
            {
                var animation = new DoubleAnimation
                {
                    From = _mouseIsOver ? Instance.MaxGrayScaleStrength : 0.0,
                    To = _mouseIsOver ? 0.0 : Instance.MaxGrayScaleStrength,
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed),
                    FillBehavior = FillBehavior.HoldEnd
                };

                _grayscaleEffect.BeginAnimation(GrayscaleEffect.StrengthProperty, animation);
            }
            if (Instance.ActiveBorderEnabled)
            {
                if (!Instance.BorderEnabled)
                {
                    WindowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00000000"));
                    WindowBorder.BorderThickness = new Thickness(1.3);
                }

                // rebind to unfreeze the brush so that the animation can be applied
                WindowBorder.SetBinding(Border.BorderBrushProperty, new Binding("Instance.BorderColor")
                {
                    Source = this,
                });

                var backgroundColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? !Instance.BorderEnabled
                                            ? (Color)ColorConverter.ConvertFromString("#00000000")
                                            : (Color)ColorConverter.ConvertFromString(Instance.BorderColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ActiveBorderColor),

                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveBorderColor)
                                        : !Instance.BorderEnabled
                                            ? (Color)ColorConverter.ConvertFromString("#00000000")
                                            : (Color)ColorConverter.ConvertFromString(Instance.BorderColor),

                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                WindowBorder.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, backgroundColorAnimation);
                backgroundColorAnimation.Completed += (sender, e) =>
                {

                    WindowBorder.SetBinding(Border.BorderThicknessProperty, new Binding("Instance.BorderEnabled")
                    {
                        Source = this,
                        Converter = (IValueConverter)Resources["BooleanToBorderThicknessConverter"]
                    });
                };
            }
            else
            {

                WindowBorder.SetBinding(Border.BorderThicknessProperty, new Binding("Instance.BorderEnabled")
                {
                    Source = this,
                    Converter = (IValueConverter)Resources["BooleanToBorderThicknessConverter"]
                });
            }

            if (Instance.ActiveBackgroundEnabled)
            {
                var borderColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ActiveBackgroundColor),
                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveBackgroundColor)
                                        : (Color)ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor),
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                   : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                WindowBackground.Background.BeginAnimation(SolidColorBrush.ColorProperty, borderColorAnimation);
            }
            if (Instance.ActiveTitleTextEnabled)
            {
                var titleBarItemsColorAnimation = new ColorAnimation
                {
                    From = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.TitleTextColor)
                                       : (Color)ColorConverter.ConvertFromString(Instance.ActiveTitleTextColor),
                    To = _mouseIsOver ? (Color)ColorConverter.ConvertFromString(Instance.ActiveTitleTextColor)
                                       : (Color)ColorConverter.ConvertFromString(Instance.TitleTextColor),
                    Duration = animationSpeed == 0 ? TimeSpan.FromSeconds(0)
                                                  : TimeSpan.FromSeconds(0.2 / animationSpeed)
                };
                title.Foreground.BeginAnimation(SolidColorBrush.ColorProperty, titleBarItemsColorAnimation);
            }
        }

        private void AnimateWindowHeight(double targetHeight, double animationSpeed)
        {
            if (Instance.DisableAnimations) animationSpeed = 0;
            double currentHeight = this.ActualHeight;

            var freezeAnimation = new DoubleAnimation
            {
                To = currentHeight,
                Duration = TimeSpan.Zero,
                FillBehavior = FillBehavior.HoldEnd
            };
            this.BeginAnimation(HeightProperty, freezeAnimation);

            var animation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = animationSpeed == 0 ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(0.2 / animationSpeed),
                EasingFunction = new QuadraticEase()
            };
            animation.Completed += (s, e) =>
            {
                _canAnimate = true;
                if (targetHeight == titleBar.Height)
                {
                    scrollViewer.ScrollToTop();
                }
                //WindowChrome.SetWindowChrome(this, Instance.IsLocked ?
                //new WindowChrome
                //{
                //    ResizeBorderThickness = new Thickness(0),
                //    CaptionHeight = 0
                //}
                //: _isOnBottom ?
                //    new WindowChrome
                //    {
                //        GlassFrameThickness = new Thickness(5),
                //        CaptionHeight = 0,
                //        ResizeBorderThickness = new Thickness(0, Instance.Minimized ? 0 : 5, 5, 0),
                //        CornerRadius = new CornerRadius(5)
                //    } :
                //    new WindowChrome
                //    {
                //        GlassFrameThickness = new Thickness(5),
                //        CaptionHeight = 0,
                //        ResizeBorderThickness = new Thickness(5, 0, 5, Instance.Minimized ? 0 : 5),
                //        CornerRadius = new CornerRadius(5)
                //    }
                // );
            };
            _canAnimate = false;
            this.BeginAnimation(HeightProperty, animation);
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
        private bool _fileWatcherWired;
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
                _fileWatcherService.Initialize(Instance.Folder, _currentFolderPath);
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
            if (e.Name!.Equals(Path.GetFileName(Instance.Folder), StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() =>
                {
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
            if (e.OldName!.Equals(Path.GetFileName(Instance.Folder), StringComparison.OrdinalIgnoreCase))
            {

                var lastInstanceName = Instance.Name;
                Dispatcher.Invoke(() =>
                {
                    Instance.Folder = e.FullPath;
                    Instance.IsFolderMissing = false;
                    _currentFolderPath = Instance.Folder;
                    Instance.Name = Path.GetFileName(e.Name!);
                    MainWindow._controller.WriteOverInstanceToKey(Instance, lastInstanceName);
                    title.Text = Instance.TitleText == "" ? Instance.Name : Instance.TitleText;
                    PathToBackButton.Visibility = Visibility.Collapsed;
                    missingFolderGrid.Visibility = Visibility.Hidden;
                    foreach (var item in FileItems)
                    {
                        item.FullPath = item.FullPath!.Replace(@$"\{e.OldName}\", @$"\{e.Name}\");
                    }
                    InitializeFileWatchers();

                });
            }
        }
        private void OnParentChanged(object sender, FileSystemEventArgs e)
        {

            if (e.Name.Equals(Path.GetFileName(Instance.Folder), StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() =>
                {
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
        }
        // Coalesces a burst of file-system events (a copy/save can fire many in a row) into
        // a single LoadFiles ~250ms after the last one, on the UI thread. Async (InvokeAsync)
        // so the watcher's threadpool callback never blocks, and debounced so we never stack
        // full folder rescans - the CPU-storm class of bug from earlier builds.
        private DispatcherTimer? _watcherDebounce;
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
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


        private void KeepWindowBehind()
        {
            if (_isTopmost)
            {
                return;
            }
            IntPtr HWND_BOTTOM = new IntPtr(1);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Interop.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, Interop.SWP_NOREDRAW | Interop.SWP_NOACTIVATE | Interop.SWP_NOMOVE | Interop.SWP_NOSIZE);
        }
        //public void KeepWindowBehind()
        //{
        //    bool keepOnBottom = this._keepOnBottom;
        //    this._keepOnBottom = false;
        //    Interop.SetWindowPos(new WindowInteropHelper(this).Handle, 1, 0, 0, 0, 0, 19U);
        //    this._keepOnBottom = keepOnBottom;
        //}

        private void ToggleHiddenFiles() => Instance.ShowHiddenFiles = !Instance.ShowHiddenFiles;
        // Apply the current Instance.IsLocked value to the running window (chrome + size
        // nudge). Idempotent — read this when an outside caller (e.g. the tray Lock-all
        // toggle) has already set the persistent flag and needs the runtime chrome to
        // catch up. Differs from ToggleIsLocked, which still flips both flags.
        public void ApplyLockedState()
        {
            _isLocked = Instance.IsLocked;
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            SetParent(helper.Handle, IntPtr.Zero);
            WindowChrome.SetWindowChrome(this, Instance.IsLocked
                ? new WindowChrome { ResizeBorderThickness = new Thickness(0), CaptionHeight = 0, CornerRadius = new CornerRadius(0) }
                : new WindowChrome { GlassFrameThickness = new Thickness(0), CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), CornerRadius = new CornerRadius(0) });
            SetAsDesktopChild();
            HandleWindowMove(true);
            this.Width += 1;
            this.Width -= 1;
        }

        private void ToggleIsLocked()
        {
            Instance.IsLocked = !Instance.IsLocked;
            // Keep the local _isLocked field in sync. Window_MouseLeftButtonDown
            // gates DragMove on _isLocked; if we leave it stale the user clicks
            // Unlock, the registry flips, the visible toggle flips, but DragMove
            // still refuses because _isLocked is the old value.
            _isLocked = Instance.IsLocked;
            var interopHelper = new WindowInteropHelper(this);
            interopHelper.EnsureHandle();
            IntPtr hwnd = interopHelper.Handle;
            SetParent(hwnd, IntPtr.Zero);
            WindowChrome.SetWindowChrome(this, Instance.IsLocked ?
                new WindowChrome
                {
                    ResizeBorderThickness = new Thickness(0),
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(0)
                } :
                new WindowChrome
                {
                    GlassFrameThickness = new Thickness(0),
                    CaptionHeight = 0,
                    ResizeBorderThickness = new Thickness(5),
                    CornerRadius = new CornerRadius(0)
                }
            );
            SetAsDesktopChild();
            HandleWindowMove(true);

            this.Width += 1;
            this.Width -= 1;
        }
        private void ToggleFileExtension() => Instance.ShowFileExtension = !Instance.ShowFileExtension;

        // File filter patterns are saved as raw strings and read back from the registry
        // with no validation on that read path - a hand-edited value, or one carried
        // over from an older build, would otherwise throw on the very next file load.
        // Treat an invalid pattern as "no filter" rather than crashing.
        private static Regex? TryCompileRegex(string? pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            try { return new Regex(pattern); }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Invalid saved filter regex '{pattern}': {ex.Message}");
                return null;
            }
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
                        ScanDir(DesktopIconManager.RacksWorkspacePath);
                    }

                    // Remove duplicates by name (if a file somehow exists in both, prefer Workspace)
                    filteredFiles = filteredFiles
                        .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();

                    _folderCount = filteredFiles.OfType<DirectoryInfo>().Count();
                    _fileCount = filteredFiles.OfType<FileInfo>().Count().ToString();
                    _folderSize = !Instance.CheckFolderSize ? "" : Task.Run(() => BytesToStringAsync(filteredFiles.OfType<FileInfo>().Sum(file => file.Length))).Result; 
                    
                    filteredFiles = filteredFiles
                                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                    
                    if (!Instance.ShowHiddenFiles)
                        filteredFiles = filteredFiles.Where(entry => !entry.Attributes.HasFlag(FileAttributes.Hidden)).ToList();
                    var fileFilterRegex = TryCompileRegex(Instance.FileFilterRegex);
                    if (fileFilterRegex != null)
                    {
                        filteredFiles = filteredFiles.Where(entry => fileFilterRegex.IsMatch(entry.Name)).ToList();
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

                        var existingItem = FileItems.FirstOrDefault(item => item.FullPath == entry.FullName);

                        long size = 0;
                        if (entry is FileInfo fileInfo)
                            size = fileInfo.Length;
                        else if (entry is DirectoryInfo directoryInfo && Instance.CheckFolderSize)
                            size = await Task.Run(() => GetDirectorySize(directoryInfo, loadFiles_cts));
                        size = size > int.MaxValue ? int.MaxValue : size;

                        string displaySize = entry is FileInfo ? await BytesToStringAsync(size)
                                                               : Instance.CheckFolderSize ? await BytesToStringAsync(size)
                                                                                          : "";
                        var thumbnail = await GetThumbnailAsync(entry.FullName);
                        bool isFile = entry is FileInfo;
                        string actualExt = isFile ? Path.GetExtension(entry.Name) : string.Empty;
                        if (existingItem == null)
                        {
                            if (fileFilterHideRegex != null && fileFilterHideRegex.IsMatch(entry.Name))
                            {
                                continue;
                            }

                            // If it's a DesktopFilterRack, ensure it hasn't just been removed from AssignedFiles during the cleanup phase
                            if (Instance.IsDesktopFilterRack && Instance.AssignedFiles != null && !Instance.AssignedFiles.Contains(entry.Name))
                            {
                                continue;
                            }

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
                    var sortedList = FileItems.ToList();

                    FileItems.Clear();
                    foreach (var fileItem in sortedList)
                    {
                        if (fileFilterHideRegex != null && fileFilterHideRegex.IsMatch(fileItem.Name))
                        {
                            continue;
                        }
                        FileItems.Add(fileItem);
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
        private void LoadingProgressRingFade(bool showLoading)
        {
            Storyboard fadeOut = (Storyboard)this.Resources["FadeOutLoadingProgressRingStoryboard"];
            Storyboard fadeIn = (Storyboard)this.Resources["FadeInLoadingProgressRingStoryboard"];

            if (showLoading)
            {
                LoadingProgressRing.IsIndeterminate = true;
                fadeIn.Begin();
            }
            else
            {
                // Turn the spin off DIRECTLY, not via fadeOut.Completed. An indeterminate
                // ProgressRing animates continuously - pinning a CPU core through nonstop
                // composition even while collapsed or at zero opacity. The old Completed
                // handler both leaked (its "-=" removed a different lambda, never the real
                // one) and could fail to fire, leaving the ring spinning forever. The fade
                // is only cosmetic opacity, so stopping the spin up front is fine.
                LoadingProgressRing.IsIndeterminate = false;
                fadeOut.Begin();
            }
        }

        public void TitleBarIconsFadeAnimation(bool show)
        {
            Storyboard fadeIn = (Storyboard)this.Resources["FadeIn_titleBarIcons_Storyboard"];
            Storyboard fadeOut = (Storyboard)this.Resources["FadeOut_titleBarIcons_Storyboard"];

            if (show)
            {
                fadeIn.Begin();
            }
            else
            {
                fadeOut.Completed += (s, e) =>
                {
                    fadeOut.Completed -= (s, e) => { }; // cleanup
                };
                fadeOut.Begin();
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

            if (Instance.IsDesktopFilterRack)
            {
                var names = FileItems.Select(f => f.Name).ToList();
                Racks.Core.DesktopIconManager.SetHiddenFilesForInstance(Instance, names);
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
                scm.ContextMenuClosed += () =>
                {
                    _contextMenuIsOpen = false;
                };
                scm.ShowContextMenu(windowHelper.Handle, files, drawingPoint, (clickedFileItem.FullPath! == _currentFolderPath), RackProtectsFromDelete);
            }
        }

        // Shown when a user tries to delete an item from inside a protected rack. Keeps it
        // short and points them at the escape hatch (drag it out, or remove the whole rack).
        private bool _deleteBlockedNoticeOpen;
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

        // Path of the item the shell menu was opened on, so "Open in File Explorer" reveals it.
        private string? _lastRightClickedPath;
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
        // A rack is a "safe space" - its items can't be deleted from the shell menu - when
        // it physically owns the files: a sandboxed virtual rack (shortcuts in AppData) or a
        // desktop-filter rack (files parked in RacksWorkspace). Folder-backed racks that point
        // at a real user folder are NOT protected: that's the user's own folder and blocking
        // delete there would be surprising. Removing the whole rack still returns everything.
        private bool RackProtectsFromDelete =>
            Instance.IsDesktopFilterRack
            || (Instance.IsShortcutsOnly && InstanceController.IsInsideVirtualFramesRoot(Instance.Folder));

        private void MoveItemToPosition()
        {
            if (_itemUnderCursor == null || _draggedItem == null)
            {
                return;
            }
            _canChangeItemPosition = false;
            if (_draggedItem != _itemUnderCursor)
            {
                try
                {
                    int fromIndex = FileItems.IndexOf(_draggedItem);
                    int toIndex = FileItems.IndexOf(_itemUnderCursor);
                    FileItems.Move(fromIndex, toIndex);
                    _itemUnderCursor.IsMoveBarVisible = false;
                    _draggedItem.IsSelected = false;
                    _itemUnderCursor.Background = Brushes.Transparent;
                    AddToCustomOrder(_draggedItem.FullPath!, toIndex);
                }
                catch
                {
                    Debug.WriteLine("Failed to swap items");
                }
            }
        }
        private void AddToCustomOrder(string path, int index)
        {
            var fileId = GetFileId(path).ToString();
            var newList = new List<Tuple<string, string>>(Instance.CustomOrderFiles);
            newList.RemoveAll(t => t.Item1 == fileId);
            newList.Add(new Tuple<string, string>(fileId, index.ToString()));
            Instance.CustomOrderFiles = newList;
        }

        private void ReassignDesktopFileToThisRack(string fullPath)
        {
            if (Instance.AssignedFiles == null) Instance.AssignedFiles = new List<string>();
            string fileName = Path.GetFileName(fullPath);
            if (!Instance.AssignedFiles.Contains(fileName))
            {
                Instance.AssignedFiles.Add(fileName);
            }

            // Remove from other racks
            foreach (var inst in MainWindow._controller.Instances.Where(i => i != Instance && i.IsDesktopFilterRack))
            {
                if (inst.AssignedFiles != null && inst.AssignedFiles.Contains(fileName))
                {
                    inst.AssignedFiles.Remove(fileName);
                    MainWindow._controller.WriteInstanceToKey(inst);
                }
            }

            MainWindow._controller.WriteInstanceToKey(Instance);
            
            // Force refresh of all Desktop racks
            foreach (var window in MainWindow._controller._subWindows)
            {
                if (window.Instance.IsDesktopFilterRack)
                {
                    window.Dispatcher.Invoke(() => window.LoadFiles(window.Instance.Folder));
                }
            }

            // Update C++ DLL hook via Shared Memory
            MainWindow._mainWindow.RefreshGlobalHiddenFiles();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            _dragdropIntoFolder = false;
            _canAutoClose = false;
            Task.Run(async () =>
            {
                Thread.Sleep(300);
                _canAutoClose = true;
            });
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (_canChangeItemPosition)
                {
                    MoveItemToPosition();
                    return;
                }
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                // Drop semantics:
                //   DEFAULT  → MOVE the dropped item into the rack. Desktop is
                //              visually cleared; the file lives in the sandbox.
                //              File pickers reach it via %USERPROFILE%\Racks\
                //              (mirror folder pinned to Quick Access).
                //   LinkOnDrop toggle (per rack) → LINK instead (hardlink for
                //              files, junction for folders, .lnk fallback).
                //              Original stays on Desktop.
                //   Hold Ctrl  → LINK for this drop only.
                //   Hold Shift → MOVE for this drop (overrides LinkOnDrop=true).
                //
                // Safety: rack removal only ever recurses into VirtualFramesRoot.
                // SafeDelete is used so junctions inside the sandbox (created by
                // an explicit LinkOnDrop toggle) are unlinked without descending
                // into their Desktop targets.
                bool ctrlDown  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
                bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                bool wantsLinkInsteadOfMove = (Instance.LinkOnDrop || ctrlDown) && !shiftDown;

                // Collect source-parent directories so we can ping the shell once at the
                // end and force the Desktop view (and any other open Explorer windows
                // pointing at the same folder) to redraw without F5.
                var sourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Aggregate any SafeMove rejection/skip reasons across this batch
                // and show them once at the end — one toast for "you dropped 3
                // weird things" beats three modals.
                var dropMessages = new List<string>();

                foreach (var file in files)
                {
                    // Bail when the rack isn't pointing at any real folder yet AND we
                    // can't safely set it up below. The original guard's AND-chain was
                    // logically impossible and never triggered.
                    if (string.IsNullOrEmpty(_currentFolderPath))
                    {
                        Debug.WriteLine("Dropped onto un-initialized frame, ignoring.");
                        return;
                    }

                    // Bootstrap path for an un-initialized "empty" rack. Two cases:
                    //   1. User dropped a folder → bind the rack to that folder (folder mode).
                    //      The bind IS the action — no further move/shortcut step.
                    //   2. User dropped a file → spin up a virtual sandbox under AppData
                    //      and route this file through CreateShortcut. (The original code
                    //      relied on a thrown exception to take this path, which never
                    //      happened with the new safe-shortcut default, leaving the
                    //      shortcut to be written to the CWD with "empty" as the folder.)
                    if (_currentFolderPath == "empty")
                    {
                        if (Directory.Exists(file))
                        {
                            _currentFolderPath = file;
                            title.Text = Path.GetFileName(_currentFolderPath);
                            Instance.Folder = file;
                            Instance.Name = Path.GetFileName(_currentFolderPath);
                            MainWindow._controller.WriteInstanceToKey(Instance);
                            LoadFiles(_currentFolderPath);
                            DataContext = this;
                            InitializeFileWatchers();
                            showFolder.Visibility = Visibility.Visible;
                            LoadingProgressRing.Visibility = Visibility.Visible;
                            addFolder.Visibility = Visibility.Hidden;
                            continue; // bind only; don't also try to move/shortcut self
                        }
                        else
                        {
                            BootstrapAsVirtualRack(file, wantsLinkInsteadOfMove);
                            continue;
                        }
                    }
                    string destinationDir = _currentFolderPath;
                    
                    if (Instance.IsDesktopFilterRack)
                    {
                        destinationDir = DesktopIconManager.RacksWorkspacePath;
                    }
                    
                    string destinationPath = Path.Combine(destinationDir, Path.GetFileName(file));
                    if (!string.IsNullOrEmpty(_dropIntoFolderPath))
                        destinationPath = Path.Combine(_dropIntoFolderPath, Path.GetFileName(file));

                    try
                    {
                        // Avoid moving onto itself (drag within the same rack folder).
                        if (file.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (Instance.IsDesktopFilterRack)
                            {
                                // The file is already in the workspace, but it was dropped into this rack.
                                ReassignDesktopFileToThisRack(destinationPath);
                                continue;
                            }
                            else
                            {
                                Debug.WriteLine("Drop source == destination, skipping.");
                            }
                            continue;
                        }

                        string parent = Path.GetDirectoryName(file);
                        if (!string.IsNullOrEmpty(parent)) sourceParents.Add(parent);

                        bool srcIsDir = Directory.Exists(file);
                        
                        // Handle name collisions in the destination by generating a unique name (like Windows Explorer)
                        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                        {
                            string name = Path.GetFileNameWithoutExtension(destinationPath);
                            string ext = Path.GetExtension(destinationPath);
                            string dir = Path.GetDirectoryName(destinationPath)!;
                            int counter = 1;
                            while (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                            {
                                destinationPath = Path.Combine(dir, $"{name} ({counter}){ext}");
                                counter++;
                            }
                        }
                        
                        if (Instance.IsDesktopFilterRack)
                        {
                            // If it's a Desktop rack, we always move it physically to the RacksWorkspace
                            // and claim it in AssignedFiles. (Unless they hold Ctrl for a link).
                            if (wantsLinkInsteadOfMove)
                            {
                                CreateShortcut(file, destinationDir);
                                ReassignDesktopFileToThisRack(destinationPath + ".lnk"); // Shortcuts get .lnk appended
                            }
                            else
                            {
                                var moveResult = SafeMove.TryMove(file, destinationPath, out string moveReason);
                                if (moveResult == SafeMove.Result.Moved)
                                {
                                    Util.Interop.NotifyShellMove(file, destinationPath, isDirectory: srcIsDir);
                                    ReassignDesktopFileToThisRack(destinationPath);
                                }
                                else
                                {
                                    Debug.WriteLine($"SafeMove {moveResult}: {moveReason}");
                                    if (!string.IsNullOrEmpty(moveReason)) dropMessages.Add(moveReason);
                                }
                            }
                            continue;
                        }
                        else
                        {
                            if (wantsLinkInsteadOfMove)
                            {
                                CreateShortcut(file, _currentFolderPath);
                            }
                            else
                            {
                                var moveResult = SafeMove.TryMove(file, destinationPath, out string moveReason);
                                if (moveResult == SafeMove.Result.Moved)
                                {
                                    Util.Interop.NotifyShellMove(file, destinationPath, isDirectory: srcIsDir);
                                }
                                else
                                {
                                    Debug.WriteLine($"SafeMove {moveResult}: {moveReason}");
                                    if (!string.IsNullOrEmpty(moveReason)) dropMessages.Add(moveReason);
                                    continue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error moving file: " + ex.Message);
                        if (!Path.Exists(Instance.Folder) && Instance.Folder != "empty")
                        {
                            PathToBackButton.Visibility = Visibility.Collapsed;
                            missingFolderGrid.Visibility = Visibility.Visible;
                            FileItems.Clear();
                        }
                        // The bootstrap-on-error fallback is intentionally gone now; the
                        // "empty" rack case is handled proactively above (BootstrapAsVirtualRack),
                        // so reaching here means a real I/O failure on an already-initialized rack.
                    }
                }

                // Once all moves are done, tell the shell to refresh every source
                // parent folder. This makes the Desktop view drop the now-gone icons
                // immediately instead of after Explorer's lazy cache expires.
                foreach (var parent in sourceParents)
                {
                    Util.Interop.NotifyShellUpdateDir(parent);
                }

                // Surface any guard-rail messages from SafeMove in a single dialog
                // so the user knows why a drop didn't take. Marshalled to the UI
                // thread because Window_Drop runs continuation work via Task.Run.
                if (dropMessages.Count > 0)
                {
                    var combined = string.Join("\n", dropMessages);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Racks.Views.RacksMessageBox.Show(combined, "Drop blocked");
                        }
                        catch (Exception ex) { Debug.WriteLine($"Drop-message dialog failed: {ex.Message}"); }
                    }));
                }
            }
        }

        // Promote an un-initialized "empty" rack into a virtual rack by creating
        // a sandbox folder under AppData and MOVING the first dropped item into
        // it (default semantics). If the user held Ctrl, the caller already set
        // wantsLinkInsteadOfMove=true to create a hardlink/junction/.lnk instead.
        private void BootstrapAsVirtualRack(string firstDroppedFile, bool wantsLinkInsteadOfMove)
        {
            Directory.CreateDirectory(InstanceController.VirtualFramesRoot);
            string sandbox = Path.Combine(InstanceController.VirtualFramesRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);

            Instance.Folder = sandbox;
            _currentFolderPath = sandbox;
            Instance.IsShortcutsOnly = true;
            Instance.ShowShortcutArrow = false;
            Instance.Name = Path.GetFileName(sandbox);
            string displayName = Path.GetFileNameWithoutExtension(firstDroppedFile);
            if (string.IsNullOrEmpty(displayName))
                displayName = Path.GetFileName(firstDroppedFile);
            Instance.TitleText = string.IsNullOrEmpty(displayName) ? "New rack" : displayName;
            title.Text = Instance.TitleText;
            MainWindow._controller.WriteInstanceToKey(Instance);

            try
            {
                if (wantsLinkInsteadOfMove)
                {
                    CreateShortcut(firstDroppedFile, sandbox);
                }
                else
                {
                    string dest = Path.Combine(sandbox, Path.GetFileName(firstDroppedFile));
                    bool srcIsDir = Directory.Exists(firstDroppedFile);
                    var moveResult = SafeMove.TryMove(firstDroppedFile, dest, out string moveReason);
                    if (moveResult == SafeMove.Result.Moved)
                    {
                        Util.Interop.NotifyShellMove(firstDroppedFile, dest, isDirectory: srcIsDir);
                        Util.Interop.NotifyShellUpdateDir(Path.GetDirectoryName(firstDroppedFile)!);
                    }
                    else
                    {
                        // SafeMove blocked the move (special folder, collision, etc.).
                        // Fall back to a shortcut so the user's gesture still produced
                        // *something* useful in the new rack, and surface the reason.
                        Debug.WriteLine($"Bootstrap SafeMove {moveResult}: {moveReason}");
                        CreateShortcut(firstDroppedFile, sandbox);
                        if (!string.IsNullOrEmpty(moveReason))
                        {
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    Racks.Views.RacksMessageBox.Show(moveReason + "\n\nCreated a shortcut in the rack instead.", "Dropped as shortcut");
                                }
                                catch (Exception ex2) { Debug.WriteLine($"Bootstrap dialog failed: {ex2.Message}"); }
                            }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bootstrap move failed, falling back to shortcut: {ex.Message}");
                CreateShortcut(firstDroppedFile, sandbox);
            }

            LoadFiles(sandbox);
            DataContext = this;
            InitializeFileWatchers();
            showFolder.Visibility = Visibility.Visible;
            LoadingProgressRing.Visibility = Visibility.Visible;
            addFolder.Visibility = Visibility.Hidden;
        }


        // Create a "reference" to filePath inside shortcutFolder that the user
        // perceives as the file/folder itself, not a shortcut. Strategies, in
        // order:
        //   1. .url      → copy as-is (already a reference file).
        //   2. File + same NTFS volume   → NTFS hardlink. Same inode, no .lnk,
        //      no shortcut-arrow overlay, no .lnk extension. Visible to file
        //      pickers under both names.
        //   3. Folder + same NTFS volume → directory junction. Looks like a
        //      real folder, no .lnk extension; file pickers can traverse it.
        //   4. Cross-volume / special filesystem → .lnk shortcut fallback.
        // In all cases the SOURCE on Desktop is left in place, so the user can
        // still find the file via any "browse to Desktop" file picker.
        void CreateShortcut(string filePath, string shortcutFolder = null)
        {
            string folder = !string.IsNullOrEmpty(shortcutFolder) ? shortcutFolder : Path.GetDirectoryName(filePath);

            if (Path.GetExtension(filePath).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(filePath, Path.Combine(folder, Path.GetFileName(filePath)));
                return;
            }

            string sameNameDest = Path.Combine(folder, Path.GetFileName(filePath));
            bool destExists = File.Exists(sameNameDest) || Directory.Exists(sameNameDest);

            if (File.Exists(filePath))
            {
                if (!destExists && HardlinkHelper.TryCreate(filePath, sameNameDest))
                    return;
                // Hardlink failed (cross-volume, special filesystem, race) — fall
                // through to .lnk so the gesture still produces something useful.
            }
            else if (Directory.Exists(filePath))
            {
                if (!destExists && JunctionHelper.TryCreate(filePath, sameNameDest))
                    return;
                // Junction failed (cross-volume, ACL, race) — fall through to
                // a folder .lnk. Note a .lnk to a directory is openable but
                // not traversable by Win32 file pickers, so this is a worse
                // experience; hopefully rare in practice.
            }

            string shortcutPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(filePath) + ".lnk");
            ShellLinkHelper.Create(
                shortcutPath: shortcutPath,
                targetPath: filePath,
                workingDirectory: Path.GetDirectoryName(filePath),
                description: Path.GetFileName(filePath));
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
                scm.ContextMenuClosed += () =>
                {
                    _selectedItems.Clear();
                    foreach (var item in FileItems)
                    {
                        item.IsSelected = false;
                    }
                    _contextMenuIsOpen = false;
                };
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
                scm.ContextMenuRenameSelected += renameHandler;
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

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            if (_isMinimized)
            {
                AnimateWindowHeight(titleBar.Height, Instance.AnimationSpeed);
            }
            if (!IsCursorWithinWindowBounds() && !_isDragging)
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive)
                {
                    TitleBarIconsFadeAnimation(true);
                }
            }
            AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
            _dragdropIntoFolder = false;
        }
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (!_mouseIsOver && IsCursorWithinWindowBounds())
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive)
                {
                    TitleBarIconsFadeAnimation(true);
                }
            }
            AnimateWindowHeight(Instance.Height, Instance.AnimationSpeed); AnimateWindowOpacity(1, Instance.AnimationSpeed);
            var sourceElement = e.OriginalSource as DependencyObject;
            var currentBorder = new Border();
            if (showFolderInGrid.Visibility == Visibility.Visible)
            {
                currentBorder = sourceElement as Border ?? FindParentOrChild<Border>(sourceElement);
            }
            else
            {
                currentBorder = sourceElement as Border ?? FindParent<Border>(sourceElement);
            }
            _dragdropIntoFolder = true;
            if (Instance.Folder == "empty") StartParticles();
            if (currentBorder != _lastBorder)
            {
                if (_lastBorder != null)
                {
                    // _isDragging = true;
                    FileItem_MouseLeave(_lastBorder, null);
                }
                _lastBorder = currentBorder;
            }
            if (currentBorder != null)
            {
                FileItem_MouseEnter(currentBorder, null);
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
                IntPtr hBitmap = IntPtr.Zero;
                BitmapSource? thumbnail = null;
                int iconSize = (int)(Instance.IconSize * _windowsScalingFactor);
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
                        if (Instance.ShowShortcutArrow)
                        {
                            return Application.Current.Dispatcher.Invoke(() =>
                            {
                                IntPtr[] overlayIcons = new IntPtr[1];
                                int overlayExtracted = ExtractIconEx(
                                    Environment.SystemDirectory + "\\shell32.dll",
                                    29,
                                    overlayIcons,
                                    null,
                                    1);

                                if (overlayExtracted > 0 && overlayIcons[0] != IntPtr.Zero)
                                {
                                    var overlay = Imaging.CreateBitmapSourceFromHIcon(
                                                  overlayIcons[0],
                                                  Int32Rect.Empty,
                                                  BitmapSizeOptions.FromEmptyOptions());
                                    DestroyIcon(overlayIcons[0]);

                                    var visual = new DrawingVisual();
                                    using (var dc = visual.RenderOpen())
                                    {
                                        Debug.WriteLine("iconsize: " + iconSize);
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
                                        if (overlayScale != 1.0)
                                        {
                                            overlay = new TransformedBitmap(overlay, new ScaleTransform(overlayScale, overlayScale));
                                            overlay.Freeze();
                                        }
                                        double overlayX = thumbnailX;
                                        double overlayY = thumbnailY + thumbnailHeight - overlay.PixelHeight;
                                        dc.DrawImage(overlay,
                                            new Rect(
                                            overlayX,
                                            overlayY,
                                            overlay.PixelWidth,
                                            overlay.PixelHeight)
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
                                }
                                return thumbnail;
                            });
                        }
                        return thumbnail;
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
                        int attempt = 0;
                        while (attempt < 3 && thumbnail == null)
                        {
                            ShellObject? shellObj = null;
                            shellObj = Directory.Exists(path) ? ShellObject.FromParsingName(path) : ShellFile.FromFilePath(path);
                            if (shellObj != null)
                            {
                                try
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        thumbnail = GetThumbnail(path, iconSize);
                                    });
                                    if (thumbnail != null)
                                    {
                                        return thumbnail;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("Failed to fetch thumbnail:" + ex.Message);
                                }
                                finally
                                {
                                    shellObj?.Dispose();
                                }
                            }
                            attempt++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                if (thumbnail != null)
                {
                    return thumbnail;
                }

                Debug.WriteLine("Failed to retrieve thumbnail after 3 attempts.");
                return null;
            });
        }



        private async Task<BitmapSource?> LoadSvgThumbnailAsync(string path, int iconSize)
        {
            try
            {
                var svgDocument = Svg.SvgDocument.Open(path);

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
        public async Task<BitmapSource?> LoadUrlIconAsync(string path)
        {
            try
            {
                string iconFile = "";
                int iconIndex = 0;
                bool hasHttp = false;
                bool hasHttps = false;
                foreach (var line in File.ReadAllLines(path))
                {
                    // Debug.WriteLine(line);
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        iconFile = line.Substring("IconFile=".Length).Trim();
                    }
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring("IconIndex=".Length).Trim(), out int i))
                        {
                            iconIndex = i;
                        }
                    }
                    else if (iconFile == "")
                    {
                        if (line.StartsWith("URL=http://"))
                        {
                            hasHttp = true;
                            break;
                        }
                        else if (line.StartsWith("URL=https://"))
                        {
                            hasHttps = true;
                            break;
                        }
                    }
                }
                if (iconFile == "")
                {
                    if (hasHttp)
                    {
                        iconFile = GetDefaultBrowserPath("http");
                    }
                    else if (hasHttps)
                    {
                        iconFile = GetDefaultBrowserPath("https");
                    }
                }
                if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
                {
                    return await Task.Run(() =>
                    {
                        IntPtr[] icons = new IntPtr[1];
                        int extracted = Interop.ExtractIconEx(iconFile, iconIndex, icons, null, 1);
                        if (extracted > 0 && icons[0] != IntPtr.Zero)
                        {
                            var source = Imaging.CreateBitmapSourceFromHIcon(
                                icons[0],
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            Interop.DestroyIcon(icons[0]);
                            if (Instance.ShowShortcutArrow)
                            {
                                IntPtr[] overlayIcons = new IntPtr[1];
                                int overlayExtracted = Interop.ExtractIconEx(
                                    Environment.SystemDirectory + "\\shell32.dll",
                                    29,
                                    overlayIcons,
                                    null,
                                    1);

                                if (overlayExtracted > 0 && overlayIcons[0] != IntPtr.Zero)
                                {
                                    var overlay = Imaging.CreateBitmapSourceFromHIcon(
                                        overlayIcons[0],
                                        Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                    Interop.DestroyIcon(overlayIcons[0]);

                                    var visual = new DrawingVisual();
                                    using (var dc = visual.RenderOpen())
                                    {
                                        dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                                        dc.DrawImage(overlay, new Rect(
                                            source.PixelWidth - overlay.PixelWidth,
                                            source.PixelHeight - overlay.PixelHeight,
                                            overlay.PixelWidth,
                                            overlay.PixelHeight));
                                    }

                                    var rtb = new RenderTargetBitmap(
                                        source.PixelWidth,
                                        source.PixelHeight,
                                        source.DpiX,
                                        source.DpiY,
                                        PixelFormats.Pbgra32);
                                    rtb.Render(visual);
                                    rtb.Freeze();

                                    return rtb;
                                }
                            }
                            source.Freeze();
                            return source;
                        }
                        return null;
                    });
                }
                return null;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error loading URL icon: " + e.Message);
                return await GetThumbnailAsync(path);
            }
        }
        private string GetDefaultBrowserPath(string protocol)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@$"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{protocol}\UserChoice"))
                {
                    if (key != null)
                    {
                        object progId = key.GetValue("Progid");

                        if (progId == null)
                        {
                            return "";
                        }
                        using (RegistryKey commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
                        {
                            if (commandKey != null)
                            {
                                object command = commandKey.GetValue("");

                                if (command == null)
                                {
                                    return "";
                                }
                                return Regex.Match(command.ToString()!, "^\"([^\"]+)\"").Groups[1].Value;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return "";
            }
            return "";
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateFileExtensionIcon();
            UpdateHiddenFilesIcon();
            UpdateIconVisibility();
            AnimateChevron(_isMinimized, true, 0.01); // When 0 docked window won't open
            KeepWindowBehind();
            RegistryHelper rgh = new RegistryHelper(InstanceController.appName);
            
            //if (rgh.KeyExistsRoot("blurBackground"))
            //{
            //    toBlur = (bool)rgh.ReadKeyValueRoot("blurBackground");
            //}
            // BackgroundType(toBlur);
        }

        public void ChangeBackgroundOpacity(int num)
        {
            try
            {
                if (Instance.IsTransparent)
                {
                    WindowBackground.Background = System.Windows.Media.Brushes.Transparent;
                    BackgroundType(false);
                    return;
                }

                // Prefer image background when one is configured and the file still exists.
                if (!string.IsNullOrWhiteSpace(Instance.BackgroundImagePath)
                    && File.Exists(Instance.BackgroundImagePath))
                {
                    try
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.UriSource = new Uri(Instance.BackgroundImagePath, UriKind.Absolute);
                        img.EndInit();
                        img.Freeze();
                        WindowBackground.Background = new ImageBrush(img)
                        {
                            Stretch = Stretch.UniformToFill,
                            Opacity = Math.Clamp(Instance.Opacity / 255.0, 0.0, 1.0),
                        };
                        return;
                    }
                    catch (Exception ex) { Debug.WriteLine($"BG image load failed: {ex.Message}"); }
                }

                if (Instance.DropShadowEnabled)
                {
                    WindowBackground.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 15,
                        ShadowDepth = 3,
                        Direction = 270,
                        Color = Colors.Black,
                        Opacity = 0.4
                    };
                    WindowBackground.Margin = new Thickness(10);
                    // Adjust sizing due to margin
                    this.Width = Instance.Width + 20;
                    this.Height = Instance.Height + 20;
                }
                else
                {
                    WindowBackground.Effect = null;
                    WindowBackground.Margin = new Thickness(0);
                    this.Width = Instance.Width;
                    this.Height = Instance.Height;
                }

                var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor);
                if (Instance.GradientBackgroundEnabled && !Instance.ActiveBackgroundEnabled)
                {
                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(0, 1)
                    };
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Instance.Opacity, c.R, c.G, c.B), 0.0));
                    
                    var bottomColor = c;
                    bottomColor.R = (byte)Math.Max(0, c.R - 30);
                    bottomColor.G = (byte)Math.Max(0, c.G - 30);
                    bottomColor.B = (byte)Math.Max(0, c.B - 30);
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Instance.Opacity, bottomColor.R, bottomColor.G, bottomColor.B), 1.0));

                    WindowBackground.Background = gradient;
                }
                else
                {
                    WindowBackground.Background = new SolidColorBrush(Color.FromArgb((byte)Instance.Opacity, c.R, c.G, c.B));
                }
                
                BackgroundType(_isTopmost);
            }
            catch
            {

            }
        }
        public void ChangeIsBlack(bool value)
        {
            _isBlack = value;
        }
        public void BackgroundType(bool toBlur)
        {
            if (Instance.IsTransparent)
            {
                toBlur = false;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            var accent = new Interop.AccentPolicy
            {
                AccentState = toBlur ? Interop.AccentState.ACCENT_ENABLE_BLURBEHIND :
                                       Interop.AccentState.ACCENT_DISABLED
            };

            var data = new Interop.WindowCompositionAttributeData
            {
                Attribute = Interop.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = Marshal.SizeOf(accent),
                Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent))
            };

            Marshal.StructureToPtr(accent, data.Data, false);
            Interop.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
        }



        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var cursorPos = System.Windows.Forms.Cursor.Position;
            var windowPos = this.PointToScreen(new System.Windows.Point(0, 0));
            var windowWidth = this.ActualWidth;
            var windowHeight = this.ActualHeight;
            if (cursorPos.X - 10 < windowPos.X || cursorPos.X + 10 > windowPos.X + windowWidth ||
                cursorPos.Y - 10 < windowPos.Y || cursorPos.Y + 10 > windowPos.Y + windowHeight)
            {
                if (!_contextMenuIsOpen && !_mouseIsOver)
                {
                    _selectedItems.Clear();
                    foreach (var fileItem in FileItems)
                    {
                        fileItem.IsSelected = false;
                        fileItem.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            // Only run drag/physics when the window is being MOVED, not resized. A resize also
            // changes Left/Top (dragging the top/left edge) and would otherwise be read as a
            // throw and start the physics loop.
            if ((_dragMovingWinddow || _isLeftButtonDown) && !_isResizing)
            {
                // Snap-to-grid. Hold Alt while dragging to bypass for one-off precision.
                if (Instance.SnapToGrid && Instance.GridSize > 1
                    && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
                {
                    int g = Instance.GridSize;
                    double snappedLeft = Math.Round(this.Left / g) * g;
                    double snappedTop = Math.Round(this.Top / g) * g;
                    if (Math.Abs(snappedLeft - this.Left) > 0.5 || Math.Abs(snappedTop - this.Top) > 0.5)
                    {
                        this.Left = snappedLeft;
                        this.Top = snappedTop;
                        return; // setting Left/Top re-enters LocationChanged with the snapped values
                    }
                }
                
                // Track drag velocity (exponential smoothing) for flick-to-throw on release.
                long nowT = DateTime.UtcNow.Ticks;
                double dtT = (nowT - _lastDragTicks) / (double)TimeSpan.TicksPerSecond;
                if (_lastDragTicks != 0 && dtT > 0.0001 && dtT < 0.2)
                {
                    double vx = (this.Left - _lastDragLeft) / dtT;
                    double vy = (this.Top - _lastDragTop) / dtT;
                    _dragVelX = _dragVelX * 0.4 + vx * 0.6;
                    _dragVelY = _dragVelY * 0.4 + vy * 0.6;
                }
                _lastDragLeft = this.Left; _lastDragTop = this.Top; _lastDragTicks = nowT;

                // --- Ice-rink physics ---
                // While dragging, hand VELOCITY to any rack we overlap (not an instant nudge):
                // it then glides on its own via the shared RackPhysics loop, slowing by
                // friction and bouncing off screen edges. The pushed rack, once moving, imparts
                // to its own neighbours through the same loop, so pushes chain A -> B -> C.
                var myRect = new Rect(this.Left, this.Top, this.Width, this.Height);
                foreach (var win in Application.Current.Windows)
                {
                    if (win is not RackWindow other || other == this
                        || other._isTopmost || other._isLocked || other._physics == null) continue;
                    var otherRect = new Rect(other.Left, other.Top, other.Width, other.Height);
                    if (myRect.IntersectsWith(otherRect))
                        Util.RackPhysics.Impart(other._physics, otherRect, myRect);
                }

                Instance.PosX = this.Left;
                Instance.PosY = this.Top;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            KeepWindowBehind();

            double idleOpacity = Instance.IdleOpacity > 0 ? Instance.IdleOpacity : 1.0;

            // "Disable Animations (Performance)" skips the pop-in entirely: snap straight
            // to full scale and idle opacity so the rack just appears.
            if (Instance.DisableAnimations)
            {
                RootScaleTransform.ScaleX = 1.0;
                RootScaleTransform.ScaleY = 1.0;
                this.Opacity = idleOpacity;
            }
            else
            {
            // Pop-in: a gentle spring from a near-full scale reads as confident and
            // premium rather than a cartoonish 0.5->1.0 bounce. Opacity eases in over a
            // slightly shorter window so the rack "arrives" before it finishes settling.
            RootScaleTransform.ScaleX = 0.88;
            RootScaleTransform.ScaleY = 0.88;
            this.Opacity = 0;

            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.36),
                EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 1, Springiness = 7, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = idleOpacity,
                Duration = TimeSpan.FromSeconds(0.22),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            RootScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
            RootScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
            this.BeginAnimation(OpacityProperty, opacityAnim);
            }
            //WindowChrome.SetWindowChrome(this, Instance.IsLocked ?
            //new WindowChrome
            //{
            //    ResizeBorderThickness = new Thickness(0),
            //    CaptionHeight = 0
            //}
            //: _isOnBottom ?
            //    new WindowChrome
            //    {
            //        GlassFrameThickness = new Thickness(5),
            //        CaptionHeight = 0,
            //        ResizeBorderThickness = new Thickness(0, Instance.Minimized ? 0 : 5, 5, 0),
            //        CornerRadius = new CornerRadius(5)
            //    } :
            //    new WindowChrome
            //    {
            //        GlassFrameThickness = new Thickness(5),
            //        CaptionHeight = 0,
            //        ResizeBorderThickness = new Thickness(5, 0, 5, Instance.Minimized ? 0 : 5),
            //        CornerRadius = new CornerRadius(5)
            //    }
            //);
            HandleWindowMove(true);
            try
            {

                _currentVD = Array.IndexOf(VirtualDesktop.GetDesktops(), VirtualDesktop.Current) + 1;
                Debug.WriteLine($"Start to desktop number: {_currentVD}");
                if (Instance.ShowOnVirtualDesktops != null && Instance.ShowOnVirtualDesktops.Length != 0 && !Instance.ShowOnVirtualDesktops.Contains(_currentVD))
                {
                    this.Hide();
                }
                else
                {
                    this.Show();
                }
                VirtualDesktop.CurrentChanged += OnVirtualDesktopChanged;
                VirtualDesktopSupported = true;
            }
            catch
            {
                VirtualDesktopSupported = false;
            }
            if (Instance.Folder == "empty")
            {
                ParticleCanvas.Margin = new Thickness(0, titleBar.Height + 10, 0, 0);
                // The particle prompt only appears WHILE you drag something over an
                // empty rack. The per-frame render loop that drives it is started on
                // drag-over (StartParticles) and stopped once the drag ends and the
                // particles decay - subscribing it here at setup left it running
                // forever on every empty rack, a runaway CompositionTarget.Rendering
                // loop that pinned a whole CPU core per rack.
            }
            else
            {
                ParticleCanvas.Visibility = Visibility.Hidden;
            }
        }

        private bool _particleRenderingActive = false;

        // Subscribe the particle animation to the per-frame render callback. Guarded so
        // repeated DragOver events can't double-subscribe (which would run it twice a frame).
        private void StartParticles()
        {
            if (_particleRenderingActive) return;
            _particleRenderingActive = true;
            ParticleCanvas.Visibility = Visibility.Visible;
            CompositionTarget.Rendering += UpdateParticle!;
        }

        private void StopParticles()
        {
            if (!_particleRenderingActive) return;
            _particleRenderingActive = false;
            CompositionTarget.Rendering -= UpdateParticle;
        }

        private void UpdateParticle(object sender, EventArgs e)
        {
            double cx = ParticleCanvas.ActualWidth / 2;
            double cy = (ParticleCanvas.ActualHeight + titleBar.Height) / 2;

            if (_dragdropIntoFolder && Instance.Folder == "empty")
            {
                for (int i = 0; i < 20 && particles.Count < 20; i++)
                {
                    CreateParticle();
                }
            }
            else if (particles.Count == 0 && !_dragdropIntoFolder)
            {
                // Drag ended and every particle has decayed: stop the render loop so an
                // idle rack costs zero CPU. It restarts on the next drag-over.
                StopParticles();
                if (Instance.Folder != "empty") ParticleCanvas.Visibility = Visibility.Hidden;
                return;
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                p.Update(cx, cy, 400);
                Ellipse v = visuals[i];
                v.Opacity = p.Opacity;
                Canvas.SetLeft(v, p.X);
                Canvas.SetTop(v, p.Y);

                if (!p.ToRemove)
                {
                    ParticleCanvas.Children.Remove(v);
                    visuals.RemoveAt(i);
                    particles.RemoveAt(i);
                }
            }
        }
        private void CreateParticle()
        {
            Particle p = new Particle(ParticleCanvas.ActualWidth, ParticleCanvas.ActualHeight);
            particles.Add(p);

            Ellipse e = new Ellipse
            {
                Width = 6,
                Height = 6,
                Opacity = 1,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Colors.White, 0.0),
                        new GradientStop(Colors.White, 0.6),
                        new GradientStop(Colors.Transparent, 1.0)
                    }
                }
            };
            visuals.Add(e);
            ParticleCanvas.Children.Add(e);
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            _previousHeight = Instance.Height;
            KeepWindowBehind();
        }

        private void UpdateIcons()
        {
            nameMenuItem.Icon = (Instance.SortBy == 1 || Instance.SortBy == 2)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            dateModifiedMenuItem.Icon = (Instance.SortBy == 3 || Instance.SortBy == 4)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            dateCreatedMenuItem.Icon = (Instance.SortBy == 5 || Instance.SortBy == 6)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            fileTypeMenuItem.Icon = (Instance.SortBy == 7 || Instance.SortBy == 8)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            fileSizeMenuItem.Icon = (Instance.SortBy == 9 || Instance.SortBy == 10)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            ascendingMenuItem.Icon = (Instance.SortBy % 2 != 0)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            descendingMenuItem.Icon = (Instance.SortBy % 2 == 0)
                ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };

            if (folderNoneMenuItem != null)
            {
                folderNoneMenuItem.Icon = (Instance.FolderOrder == 0)
                    ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                    : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };
            }
            if (folderFirstMenuItem != null)
            {
                folderFirstMenuItem.Icon = (Instance.FolderOrder == 1)
                    ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                    : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };
            }
            if (folderLastMenuItem != null)
            {
                folderLastMenuItem.Icon = (Instance.FolderOrder == 2)
                    ? new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Filled = true }
                    : new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent };
            }
        }
        // Rename is invoked from the title-bar context menu only. Clicks on the
        // title text now bubble to Window_MouseLeftButtonDown (DragMove) — the
        // old double-click-to-rename was unreliable because DragMove's modal
        // pump ate the second click.
        private void BeginTitleRename()
        {
            titleRenameBox.Text = Instance.TitleText ?? title.Text ?? "";
            title.Visibility = Visibility.Collapsed;
            titleRenameBox.Visibility = Visibility.Visible;
            _isRenaming = true;
            titleRenameBox.Focus();
            Keyboard.Focus(titleRenameBox);
            titleRenameBox.SelectAll();
        }

        private void TitleRenameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitTitleRename(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { CommitTitleRename(false); e.Handled = true; }
        }

        private void TitleRenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (titleRenameBox.Visibility == Visibility.Visible) CommitTitleRename(true);
        }

        private void CommitTitleRename(bool save)
        {
            if (save)
            {
                string newTitle = titleRenameBox.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(newTitle))
                {
                    Instance.TitleText = newTitle;
                    title.Text = newTitle;
                }
            }
            titleRenameBox.Visibility = Visibility.Collapsed;
            title.Visibility = Visibility.Visible;
            _isRenaming = false;
        }

private void titleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            contextMenu = new ContextMenu();
            if (_itemCurrentlyRenaming != null)
            {
                _itemCurrentlyRenaming.IsRenaming = false;
            }
            ToggleSwitch toggleHiddenFiles = new ToggleSwitch { Content = Lang.TitleBarContextMenu_HiddenFiles };
            toggleHiddenFiles.Click += (s, args) => { ToggleHiddenFiles(); LoadFiles(_currentFolderPath); };

            ToggleSwitch toggleFileExtension = new ToggleSwitch { Content = Lang.TitleBarContextMenu_FileExtensions };
            toggleFileExtension.Click += (_, _) => { ToggleFileExtension(); LoadFiles(_currentFolderPath); };

            toggleHiddenFiles.IsChecked = Instance.ShowHiddenFiles;
            toggleFileExtension.IsChecked = Instance.ShowFileExtension;

            // Per-rack drop semantic toggle. Default = MOVE (Desktop visually
            // clean; file lives in sandbox, findable via the Racks mirror in
            // Quick access). Toggle ON to LINK instead (keeps original on
            // Desktop; uses hardlinks for files, junctions for folders).
            ToggleSwitch linkOnDropToggle = new ToggleSwitch
            {
                Content = "Link on drop",
                ToolTip = "Keep originals on Desktop. Ctrl per-drop, Shift forces move.",
                IsChecked = Instance.LinkOnDrop,
            };

            // What clicking a sub-folder inside the rack should do.
            //   OFF (default) → open the folder in Windows Explorer (normal).
            //   ON            → navigate into it inside the rack window.
            ToggleSwitch openInsideToggle = new ToggleSwitch
            {
                Content = "Open sub-folders in rack",
                ToolTip = "When off, sub-folder clicks open in Windows Explorer.",
                IsChecked = Instance.FolderOpenInsideFrame,
            };

            ToggleSwitch lockToggle = new ToggleSwitch
            {
                Content = "Lock rack",
                ToolTip = "Prevent moving and resizing.",
                IsChecked = Instance.IsLocked,
            };
            lockToggle.Click += (_, _) =>
            {
                if ((lockToggle.IsChecked == true) != Instance.IsLocked)
                    ToggleIsLocked();
            };
            openInsideToggle.Click += (_, _) =>
            {
                Instance.FolderOpenInsideFrame = openInsideToggle.IsChecked == true;
            };
            linkOnDropToggle.Click += (_, _) =>
            {
                Instance.LinkOnDrop = linkOnDropToggle.IsChecked == true;
            };

            ToggleSwitch snapToGridToggle = new ToggleSwitch
            {
                Content = $"Snap to grid ({Instance.GridSize}px) — hold Alt to bypass",
                IsChecked = Instance.SnapToGrid,
            };
            snapToGridToggle.Click += (_, _) =>
            {
                Instance.SnapToGrid = snapToGridToggle.IsChecked == true;
            };

            ToggleSwitch pinToTopToggle = new ToggleSwitch
            {
                Content = "Pin to top",
                IsChecked = Instance.PinToTop,
            };
            pinToTopToggle.Click += (_, _) =>
            {
                bool on = pinToTopToggle.IsChecked == true;
                Instance.PinToTop = on;
                _isTopmost = on;
                this.Topmost = on;
                if (!on) KeepWindowBehind();
            };

            MenuItem themeMenu = new MenuItem
            {
                Header = "Theme",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Color20),
            };
            foreach (var preset in ThemePresets.All)
            {
                var captured = preset;
                var item = new MenuItem { Header = captured.Name, Height = 30 };
                item.Click += (_, _) =>
                {
                    ThemePresets.Apply(Instance, captured);
                    // Re-apply runtime visuals that read from Instance colors.
                    ChangeBackgroundOpacity(Instance.Opacity);
                };
                themeMenu.Items.Add(item);
            }

            MenuItem showInExplorerItem = new MenuItem
            {
                Header = "Show in Explorer",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.FolderOpen20),
            };
            showInExplorerItem.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(Instance.Folder) || Instance.Folder == "empty"
                    || !Directory.Exists(Instance.Folder)) return;
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Instance.Folder}\"") { UseShellExecute = true }); }
                catch (Exception ex) { Debug.WriteLine($"Show in Explorer failed: {ex.Message}"); }
            };

            MenuItem resetPositionItem = new MenuItem
            {
                Header = "Reset position",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Target20),
            };
            resetPositionItem.Click += (_, _) =>
            {
                try
                {
                    var screen = Screen.PrimaryScreen!.WorkingArea;
                    double scale = _windowsScalingFactor > 0 ? _windowsScalingFactor : 1.0;
                    double centerX = (screen.Left + (screen.Width - this.Width * scale) / 2) / scale;
                    double centerY = (screen.Top + (screen.Height - this.Height * scale) / 2) / scale;
                    this.Left = centerX;
                    this.Top = centerY;
                    Instance.PosX = centerX;
                    Instance.PosY = centerY;
                }
                catch (Exception ex) { Debug.WriteLine($"Reset position failed: {ex.Message}"); }
            };

            MenuItem refreshThumbsItem = new MenuItem
            {
                Header = "Refresh thumbnails",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.ArrowSync20),
            };
            refreshThumbsItem.Click += async (_, _) =>
            {
                try
                {
                    foreach (var item in FileItems)
                    {
                        if (string.IsNullOrEmpty(item.FullPath)) continue;
                        item.Thumbnail = await GetThumbnailAsync(item.FullPath);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Refresh thumbnails failed: {ex.Message}"); }
            };

            MenuItem duplicateItem = new MenuItem
            {
                Header = "Duplicate",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Copy20),
            };
            duplicateItem.Click += (_, _) =>
            {
                try { MainWindow._controller.DuplicateInstance(Instance); }
                catch (Exception ex) { Debug.WriteLine($"Duplicate failed: {ex.Message}"); }
            };

            MenuItem backgroundImageItem = new MenuItem
            {
                Header = string.IsNullOrEmpty(Instance.BackgroundImagePath)
                    ? "Set background image…"
                    : "Background image…",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Image20),
            };
            backgroundImageItem.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick a background image (Cancel to clear)",
                    Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog() == true)
                {
                    Instance.BackgroundImagePath = dlg.FileName;
                }
                else if (!string.IsNullOrEmpty(Instance.BackgroundImagePath))
                {
                    // Cancel on a rack that already has an image = clear it (one-step).
                    Instance.BackgroundImagePath = "";
                }
                ChangeBackgroundOpacity(Instance.Opacity);
            };

            // Per-rack auto-routing rule. Anything created on the Desktop whose name
            // matches this regex gets a .lnk auto-created in this rack. Empty = off.
            MenuItem autoRouteItem = new MenuItem
            {
                Header = string.IsNullOrEmpty(Instance.AutoRouteRegex)
                    ? "Auto-route from Desktop…"
                    : $"Auto-route: /{Instance.AutoRouteRegex}/",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Filter20),
            };
            autoRouteItem.Click += async (_, _) =>
            {
                var input = new TextBox
                {
                    Text = Instance.AutoRouteRegex ?? "",
                    MinWidth = 280,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                var help = new TextBlock
                {
                    Text = "Regex matched against file names on the Desktop. " +
                           "Examples:  \\.png$   ^report-.*\\.pdf$   .*screenshot.*",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var panel = new StackPanel();
                panel.Children.Add(help);
                panel.Children.Add(input);
                var dlg = new MessageBox
                {
                    Title = "Auto-route rule",
                    Content = panel,
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Clear rule",
                    CloseButtonText = "Cancel",
                };
                var res = await dlg.ShowDialogAsync();
                if (res == MessageBoxResult.Primary)
                    Instance.AutoRouteRegex = input.Text ?? "";
                else if (res == MessageBoxResult.Secondary)
                    Instance.AutoRouteRegex = "";
            };

            MenuItem frameSettings = new MenuItem
            {
                Header = "Settings…",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Settings20)
            };
            frameSettings.Click += (s, args) =>
            {
                bool itWasMin = _isMinimized;
                if (itWasMin)
                {
                    Minimize_MouseLeftButtonDown(null, null);
                }
                var dialog = new RackSettingsDialog(this);
                dialog.ShowDialog();
                if (dialog.DialogResult == true)
                {
                    MainWindow._controller.WriteInstanceToKey(Instance);
                    if (itWasMin)
                    {
                        Minimize_MouseLeftButtonDown(null, null);
                    }
                    LoadFiles(_currentFolderPath);
                }
            };

            MenuItem reloadItems = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_Reload,
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.ArrowSync20)
            };
            reloadItems.Click += (s, args) =>
            {
                FileItems.Clear();
                LoadFiles(Instance.Folder);
                _currentFolderPath = Instance.Folder;
                InitializeFileWatchers();

            };
            reloadItems.Visibility = (Instance.Folder == "empty" || string.IsNullOrEmpty(Instance.Folder)) ? Visibility.Collapsed : Visibility.Visible;

            MenuItem lockFrame = new MenuItem
            {
                Header = Instance.IsLocked ? Lang.TitleBarContextMenu_UnlockFrame : Lang.TitleBarContextMenu_LockFrame,
                Height = 34,
                Icon = Instance.IsLocked ? new SymbolIcon(SymbolRegular.LockClosed20) : new SymbolIcon(SymbolRegular.LockOpen20)
            };
            lockFrame.Click += (s, args) =>
            {
                _isLocked = !_isLocked;
                ToggleIsLocked();
                //WindowChrome.SetWindowChrome(this, Instance.IsLocked ?
                //new WindowChrome
                //{
                //    ResizeBorderThickness = new Thickness(0),
                //    CaptionHeight = 0
                //}
                //: _isOnBottom ?
                //    new WindowChrome
                //    {
                //        GlassFrameThickness = new Thickness(5),
                //        CaptionHeight = 0,
                //        ResizeBorderThickness = new Thickness(5, Instance.Minimized ? 0 : 5, 5, 0),
                //        CornerRadius = new CornerRadius(5)
                //    } :
                //    new WindowChrome
                //    {
                //        GlassFrameThickness = new Thickness(5),
                //        CaptionHeight = 0,
                //        ResizeBorderThickness = new Thickness(5, 0, 5, Instance.Minimized ? 0 : 5),
                //        CornerRadius = new CornerRadius(5)
                //    }
                //);

            };

            MenuItem exitItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_Remove,
                Height = 34,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFC6060")),
                Icon = new SymbolIcon(SymbolRegular.Delete20)

            };

            exitItem.Click += async (s, args) =>
            {
                // Make the user understand what removal actually does. Three cases:
                //   1. Virtual rack in sandbox — its .lnk shortcuts will be deleted
                //      from AppData; original files are untouched.
                //   2. Folder-backed rack — only the rack is removed, the folder on
                //      disk is left exactly as it was.
                //   3. Anything else — same as 2.
                int itemCount = 0;
                try
                {
                    if (Instance.IsDesktopFilterRack)
                    {
                        itemCount = Instance.AssignedFiles?.Count ?? 0;
                    }
                    else if (!string.IsNullOrEmpty(Instance.Folder) && Directory.Exists(Instance.Folder))
                    {
                        itemCount = Directory.EnumerateFileSystemEntries(Instance.Folder).Count();
                    }
                }
                catch { }

                string body;
                bool isSandboxed = Instance.IsShortcutsOnly
                    && InstanceController.IsInsideVirtualFramesRoot(Instance.Folder);
                string deskPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                bool isOnDesktop = !string.IsNullOrEmpty(Instance.Folder) &&
                    System.IO.Path.GetDirectoryName(Instance.Folder.TrimEnd('\\', '/'))?.Equals(deskPath, StringComparison.OrdinalIgnoreCase) == true;

                if (Instance.IsDesktopFilterRack)
                {
                    body = itemCount > 0 
                        ? $"Remove this rack? {itemCount} item(s) will be returned to your Desktop."
                        : "Remove this empty rack?";
                }
                else if (isSandboxed)
                {
                    body = itemCount > 0
                        ? $"Remove this rack? {itemCount} shortcut(s) will be deleted from the sandbox. Original files are not touched."
                        : "Remove this empty rack?";
                }
                else if (isOnDesktop)
                {
                    body = itemCount > 0 
                        ? $"Remove this rack? {itemCount} item(s) will be returned to your Desktop, and the folder '{System.IO.Path.GetFileName(Instance.Folder)}' will be deleted."
                        : $"Remove this empty rack? The folder '{System.IO.Path.GetFileName(Instance.Folder)}' will be deleted.";
                }
                else
                {
                    body = $"Remove this rack? The folder on disk ({Instance.Folder}) is left untouched.";
                }

                bool confirmed = Racks.Views.RacksMessageBox.Confirm(
                    body,
                    Lang.TitleBarContextMenu_RemoveMessageBox_Title,
                    Lang.TitleBarContextMenu_RemoveMessageBox_Yes,
                    Lang.TitleBarContextMenu_RemoveMessageBox_No);

                if (confirmed)
                {
                    // Collect everything that lands back on the desktop so we can arrange it
                    // into a clean grid afterwards (files just moved back land wherever
                    // Explorer decides, which looks messy).
                    var returnedToDesktop = new List<string>();
                    if (Instance.IsDesktopFilterRack && Instance.AssignedFiles != null)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        foreach (string fileName in Instance.AssignedFiles)
                        {
                            string wpPath = System.IO.Path.Combine(DesktopIconManager.RacksWorkspacePath, fileName);
                            string destPath = System.IO.Path.Combine(desktopPath, fileName);
                            if (System.IO.File.Exists(wpPath) || System.IO.Directory.Exists(wpPath))
                            {
                                if (Util.SafeMove.TryMove(wpPath, destPath, out _) == Util.SafeMove.Result.Moved)
                                    returnedToDesktop.Add(destPath);
                            }
                        }
                    }
                    else if (isOnDesktop && System.IO.Directory.Exists(Instance.Folder))
                    {
                        foreach (string file in System.IO.Directory.GetFileSystemEntries(Instance.Folder))
                        {
                            string dest = System.IO.Path.Combine(deskPath, System.IO.Path.GetFileName(file));
                            if (Util.SafeMove.TryMove(file, dest, out _) == Util.SafeMove.Result.Moved)
                                returnedToDesktop.Add(dest);
                        }
                        try { System.IO.Directory.Delete(Instance.Folder, false); } catch { }
                    }
                    if (returnedToDesktop.Count > 0)
                        Util.DesktopIconPositioner.ArrangeInGrid(returnedToDesktop);

                    RegistryKey key = Registry.CurrentUser.OpenSubKey(Instance.GetKeyLocation(), true)!;
                    if (key != null)
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(Instance.GetKeyLocation());
                    }
                    MainWindow._controller.RemoveInstance(Instance, this);
                    // Only nuke the backing folder if it's actually under our
                    // VirtualFrames sandbox in AppData. Otherwise we'd delete
                    // whatever real folder the rack happens to be pointing at —
                    // that's how users were losing data on the old build.
                    // SafeDelete walks reparse points (junctions to Desktop
                    // folders) WITHOUT descending — a plain Directory.Delete
                    // recursive would obliterate the junction targets.
                    if (Instance.IsShortcutsOnly
                        && !string.IsNullOrEmpty(Instance.Folder)
                        && InstanceController.IsInsideVirtualFramesRoot(Instance.Folder)
                        && Directory.Exists(Instance.Folder))
                    {
                        try { Util.SafeDelete.DeleteDirectoryRecursive(Instance.Folder); }
                        catch (Exception ex) { Debug.WriteLine($"Sandbox delete failed: {ex.Message}"); }
                    }
                    this.Close();

                }
            };

            MenuItem sortByMenuItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_Sortby,
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.ArrowSort20)
            };
            nameMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_Name, Height = 34, StaysOpenOnClick = true };
            dateModifiedMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_DateModified, Height = 34, StaysOpenOnClick = true };
            dateCreatedMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_DateCreated, Height = 34, StaysOpenOnClick = true };
            fileTypeMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_FileType, Height = 34, StaysOpenOnClick = true };
            fileSizeMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_FileSize, Height = 34, StaysOpenOnClick = true };
            ascendingMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_Ascending, Height = 34, StaysOpenOnClick = true };
            descendingMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_Descending, Height = 34, StaysOpenOnClick = true };



            nameMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 != 0 || Instance.SortBy != 1) Instance.SortBy = 1;
                else Instance.SortBy = 2;
                UpdateIcons();
                SortItems();
            };
            dateModifiedMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 != 0 || Instance.SortBy != 3) Instance.SortBy = 3;
                else Instance.SortBy = 4;
                UpdateIcons();
                SortItems();
            };

            dateCreatedMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 != 0 || Instance.SortBy != 5) Instance.SortBy = 5;
                else Instance.SortBy = 6;
                UpdateIcons();
                SortItems();
            };
            fileTypeMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 != 0 || Instance.SortBy != 7) Instance.SortBy = 7;
                else Instance.SortBy = 8;
                UpdateIcons();
                SortItems();
            };
            fileSizeMenuItem.Click += (s, args) =>
            {
                if (Instance.SortBy % 2 != 0 && Instance.SortBy != 9) Instance.SortBy = 9;
                else Instance.SortBy = 10;
                UpdateIcons();
                SortItems();
            };

            ascendingMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 == 0) Instance.SortBy -= 1;
                UpdateIcons();
                SortItems();
            };

            descendingMenuItem.Click += async (s, args) =>
            {
                if (Instance.SortBy % 2 != 0) Instance.SortBy += 1;
                UpdateIcons();
                SortItems();
            };

            MenuItem FrameInfoItem = new MenuItem
            {
                StaysOpenOnClick = true,
                IsEnabled = false,
            };
            TextBlock InfoText = new TextBlock
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };

            InfoText.Inlines.Add(new Run(Lang.TitleBarContextMenu_Info_Files) { Foreground = Brushes.White });
            InfoText.Inlines.Add(new Run($"{ViewModel.FileCount}") { Foreground = Brushes.CornflowerBlue });
            InfoText.Inlines.Add(new Run("\n"));

            InfoText.Inlines.Add(new Run(Lang.TitleBarContextMenu_Info_Folders) { Foreground = Brushes.White });
            InfoText.Inlines.Add(new Run($"{ViewModel.FolderCount}") { Foreground = Brushes.CornflowerBlue });
            InfoText.Inlines.Add(new Run("\n"));
            if (Instance.CheckFolderSize)
            {
                InfoText.Inlines.Add(new Run(Lang.TitleBarContextMenu_Info_FolderSize) { Foreground = Brushes.White });
                InfoText.Inlines.Add(new Run($"{ViewModel.FolderSize}") { Foreground = Brushes.CornflowerBlue });
                InfoText.Inlines.Add(new Run("\n"));
            }

            InfoText.Inlines.Add(new Run(Lang.TitleBarContextMenu_Info_LastUpdated) { Foreground = Brushes.White });
            InfoText.Inlines.Add(new Run($"{_lastUpdated.ToString("hh:mm tt")}") { Foreground = Brushes.CornflowerBlue });

            FrameInfoItem.Header = InfoText;

            CustomItemOrderMenuItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_CustomItemOrder,
                Height = 36,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Star20 }
            };

            MenuItem CustomItemOrder_Delete_MenuItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_CustomItemOrder_DeleteOrder,
                Height = 34,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Delete20 }
            };
            CustomItemOrder_Delete_MenuItem.Click += (s, args) =>
            {
                Instance.CustomOrderFiles = null;
                SortItems();
            };
            ToggleSwitch CustomItemOrder_ToggleSwitch = new ToggleSwitch
            {
                IsChecked = Instance.EnableCustomItemsOrder,
                Content = Instance.EnableCustomItemsOrder ? Lang.TitleBarContextMenu_CustomItemOrder_ToggleSwitch_Enable : Lang.TitleBarContextMenu_CustomItemOrder_ToggleSwitch_Disable,
                Height = 20,
            };
            CustomItemOrder_ToggleSwitch.Click += (s, args) =>
            {
                Instance.EnableCustomItemsOrder = !Instance.EnableCustomItemsOrder;
                CustomItemOrder_ToggleSwitch.Content = Instance.EnableCustomItemsOrder ?
                    Lang.TitleBarContextMenu_CustomItemOrder_ToggleSwitch_Enable : Lang.TitleBarContextMenu_CustomItemOrder_ToggleSwitch_Disable;
                SortItems();
            };
            CustomItemOrderMenuItem.Items.Add(CustomItemOrder_ToggleSwitch);
            CustomItemOrderMenuItem.Items.Add(CustomItemOrder_Delete_MenuItem);

            folderOrderMenuItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_Sortby_FolderOrder,
                Height = 36,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Folder20 }
            };

            folderNoneMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_FolderIder_None, Height = 34, StaysOpenOnClick = true };
            folderFirstMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_FolderIder_First, Height = 34, StaysOpenOnClick = true };
            folderLastMenuItem = new MenuItem { Header = Lang.TitleBarContextMenu_Sortby_FolderIder_Last, Height = 34, StaysOpenOnClick = true };

            folderNoneMenuItem.Click += (s, args) =>
            {
                Instance.FolderOrder = 0;
                UpdateIcons();
                SortItems();
            };
            folderFirstMenuItem.Click += (s, args) =>
            {
                Instance.FolderOrder = 1;
                UpdateIcons();
                SortItems();
            };
            folderLastMenuItem.Click += (s, args) =>
            {
                Instance.FolderOrder = 2;
                UpdateIcons();
                SortItems();
            };

            UpdateIcons();

            MenuItem openInExplorerMenuItem = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_OpenFolder,
                Icon = new SymbolIcon { Symbol = SymbolRegular.FolderOpen20 }
            };
            openInExplorerMenuItem.Visibility = (Instance.Folder == "empty" || string.IsNullOrEmpty(Instance.Folder)) ? Visibility.Collapsed : Visibility.Visible;
            openInExplorerMenuItem.Click += (_, _) => { OpenFolder(); };


            MenuItem changeItemView = new MenuItem
            {
                Header = Lang.TitleBarContextMenu_ChangeView
            };
            if (showFolder.Visibility == Visibility.Visible)
            {
                changeItemView.Header = Lang.TitleBarContextMenu_GridView;
                changeItemView.Icon = new SymbolIcon { Symbol = SymbolRegular.Grid20 };
            }
            else
            {
                changeItemView.Header = Lang.TitleBarContextMenu_DetailsView;
                changeItemView.Icon = new SymbolIcon { Symbol = SymbolRegular.AppsList20 };
            }
            changeItemView.Click += (_, _) =>
            {
                if (showFolder.Visibility == Visibility.Visible)
                {
                    changeItemView.Header = Lang.TitleBarContextMenu_GridView;
                    changeItemView.Icon = new SymbolIcon { Symbol = SymbolRegular.Grid20 };
                    showFolderInGrid.Visibility = Visibility.Visible;
                    showFolder.Visibility = Visibility.Hidden;
                    Instance.ShowInGrid = !Instance.ShowInGrid;
                }
                else
                {
                    Instance.ShowInGrid = !Instance.ShowInGrid;
                    showFolder.Visibility = Visibility.Visible;
                    showFolderInGrid.Visibility = Visibility.Hidden;
                    changeItemView.Header = Lang.TitleBarContextMenu_DetailsView;
                    changeItemView.Icon = new SymbolIcon { Symbol = SymbolRegular.AppsList20 };
                }
            };

            folderOrderMenuItem.Items.Add(folderNoneMenuItem);
            folderOrderMenuItem.Items.Add(folderFirstMenuItem);
            folderOrderMenuItem.Items.Add(folderLastMenuItem);


            sortByMenuItem.Items.Add(CustomItemOrderMenuItem);
            sortByMenuItem.Items.Add(folderOrderMenuItem);
            sortByMenuItem.Items.Add(new Separator());
            sortByMenuItem.Items.Add(nameMenuItem);
            sortByMenuItem.Items.Add(dateModifiedMenuItem);
            sortByMenuItem.Items.Add(dateCreatedMenuItem);
            sortByMenuItem.Items.Add(fileTypeMenuItem);
            sortByMenuItem.Items.Add(fileSizeMenuItem);
            sortByMenuItem.Items.Add(new Separator());
            sortByMenuItem.Items.Add(ascendingMenuItem);
            sortByMenuItem.Items.Add(descendingMenuItem);

            MenuItem renameItem = new MenuItem
            {
                Header = "Rename rack",
                Height = 34,
                Icon = new SymbolIcon(SymbolRegular.Edit20),
            };
            renameItem.Click += (_, _) =>
            {
                contextMenu.IsOpen = false;
                Dispatcher.BeginInvoke(new Action(BeginTitleRename), DispatcherPriority.Background);
            };

            // Slim, two-tier rack menu. Frequently used at the top; per-rack
            // toggles in the middle; one-shot actions next; everything advanced
            // (lock, snap, auto-route, theme, background image, refresh
            // thumbnails) lives inside Frame Settings — not exposed here so the
            // menu doesn't drown the user. Labels avoid parenthetical hints —
            // tooltips/Settings are the place for those.
            contextMenu.Items.Add(renameItem);
            contextMenu.Items.Add(sortByMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(toggleHiddenFiles);
            contextMenu.Items.Add(toggleFileExtension);
            contextMenu.Items.Add(changeItemView);
            contextMenu.Items.Add(pinToTopToggle);
            contextMenu.Items.Add(lockToggle);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(linkOnDropToggle);
            contextMenu.Items.Add(openInsideToggle);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(reloadItems);
            contextMenu.Items.Add(openInExplorerMenuItem);
            contextMenu.Items.Add(duplicateItem);
            contextMenu.Items.Add(resetPositionItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(frameSettings);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            contextMenu.IsOpen = true;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            KeepWindowBehind();
            Debug.WriteLine("Window_StateChanged hide");
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

        private int GetZIndex(IntPtr hwnd)
        {
            IntPtr h = GetTopWindow(shellView);
            int z = 0;

            while (h != IntPtr.Zero)
            {
                if (h == hwnd) return z;
                h = Interop.GetWindow(h, GW_HWNDNEXT);
                z++;
            }
            return -1;
        }
        IntPtr GetWindowWithMinZIndex(List<IntPtr> windowHandles)
        {
            IntPtr lowestWindow = IntPtr.Zero;
            int lowestZ = int.MaxValue;

            foreach (var hwnd in windowHandles)
            {
                int z = 0;
                IntPtr prev = hwnd;

                while ((prev = Interop.GetWindow(prev, GW_HWNDPREV)) != IntPtr.Zero)
                {
                    z++;
                }

                if (z >= 0 && z < lowestZ)
                {
                    lowestZ = z;
                    lowestWindow = hwnd;
                }
            }
            return lowestWindow;
        }
        Rectangle RectToRectangle(RECT r)
        {
            return new Rectangle(
                r.Left,
                r.Top,
                r.Right - r.Left,
                r.Bottom - r.Top
            );
        }
        private bool WindowIsOverlapped(IntPtr hwnd, List<IntPtr> windowHandles)
        {
            if (!GetWindowRect(hwnd, out RECT thisR))
            {
                return false;
            }
            Rectangle thisRect = RectToRectangle(thisR);
            if (Instance.AutoExpandonCursor && _isMinimized)
            {
                thisRect = new Rectangle(
                    thisRect.Left,
                    thisRect.Top,
                    thisRect.Width,
                    (int)Instance.Height
                );
            }
            int zIndex = GetZIndex(hwnd);
            foreach (var window in windowHandles)
            {
                if (window == hwnd) continue;
                if (!GetWindowRect(window, out RECT testR)) continue;
                if (GetZIndex(window) > zIndex) continue;

                Rectangle testRect = RectToRectangle(testR);
                Rectangle intersect = Rectangle.Intersect(thisRect, testRect);
                if (intersect.Width > 0 && intersect.Height > 0)
                {
                    return true;
                }
            }
            return false;
        }

        void BringFrameToFront(IntPtr hwnd, bool forceToFront)
        {
            IntPtr hwndLower = GetWindowWithMinZIndex(MainWindow._controller._subWindowsPtr);
            bool overlapped = WindowIsOverlapped(hwnd, MainWindow._controller._subWindowsPtr);

            if (forceToFront || (hwnd != hwndLower && overlapped))
            {
                hwndLower = Interop.GetWindow(hwndLower, GW_HWNDPREV);
                SendMessage(hwnd, WM_SETREDRAW, 0, IntPtr.Zero);
                Debug.WriteLine("moved to the front");
                SetWindowPos(hwnd, 0, 0, 0, 0, 0,
               SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);

                SendMessage(hwnd, WM_SETREDRAW, 1, IntPtr.Zero);
            }
        }
        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_mouseIsOver && IsCursorWithinWindowBounds())
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive)
                {
                    TitleBarIconsFadeAnimation(true);
                }
            }
            _mouseIsOver = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            BringFrameToFront(hwnd, false);
            SetForegroundWindow(hwnd);
            this.Activate();
            SetFocus(hwnd);
            this.Focus();
            _canAutoClose = true;
            AnimateWindowOpacity(1, Instance.AnimationSpeed);
            if ((Instance.AutoExpandonCursor) && _isMinimized)
            {
                Minimize_MouseLeftButtonDown(null, null);
            }
        }
        public bool IsCursorWithinWindowBounds()
        {
            Point cursor = System.Windows.Forms.Cursor.Position;
            bool cursorIsOverTheWindow = WindowFromPoint(new POINT { X = cursor.X, Y = cursor.Y }) == new WindowInteropHelper(this).Handle;

            Interop.GetWindowRect(new WindowInteropHelper(this).Handle, out RECT rect);
            Point point = System.Windows.Forms.Cursor.Position;
            var curPoint = new Point((int)point.X, (int)point.Y);
            bool cursorIsWithinWindowBounds = point.X + 1 > rect.Left && point.X - 1 < rect.Right && point.Y + 1 > rect.Top && point.Y - 1 < rect.Bottom;

            if (_isDragging && (GetAsyncKeyState(0x01) & 0x8000) == 0) // Left not down
            {
                _isDragging = false;
            }
            if (_isDragging)
            {
                if (cursorIsWithinWindowBounds) return true;
                if (!cursorIsWithinWindowBounds) return false;
            }

            if (_contextMenuIsOpen
                || contextMenu.IsOpen
                || (_isDragging && (GetAsyncKeyState(0x01) & 0x8000) != 0)) return true;
            if (!_contextMenuIsOpen)
            {
                if (cursorIsOverTheWindow)
                {
                    return true;
                }

                return false;
            }
            return false;
        }

        public void UpdateIconVisibility()
        {
            if (FileExtensionIcon != null)
            {
                FileExtensionIconGrid.Visibility = Instance.ShowFileExtensionIcon ? Visibility.Visible : Visibility.Collapsed;
            }
            if (HiddenFilesIcon != null)
            {
                HiddenFilesIconGrid.Visibility = Instance.ShowHiddenFilesIcon ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            MouseLeaveWindow();
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

        private void SymbolIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            InfoFlyout.IsOpen = true;
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

        private bool _isClosingAnimPlaying = false;
        private bool _closeRequested = false;
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Instance.isWindowClosing = true;
            // CompositionTarget.Rendering holds a strong reference to the handler, which
            // would keep this window (and its whole visual tree) alive after close. Detach.
            StopParticles();
            // Detach the static VirtualDesktop event so it can't keep this window alive
            // after close, then drop the folder watcher.
            try { VirtualDesktop.CurrentChanged -= OnVirtualDesktopChanged; } catch { }
            try { _watcherDebounce?.Stop(); } catch { }
            try { _fileWatcherService.Dispose(); } catch { }
            try { if (_physics != null) Util.RackPhysics.Unregister(_physics); } catch { }
            // "Disable Animations (Performance)" closes immediately with no shrink/fade.
            if (Instance.DisableAnimations)
            {
                return; // let the close proceed without cancelling for an animation
            }
            if (!_isClosingAnimPlaying)
            {
                e.Cancel = true;
                _isClosingAnimPlaying = true;

                // Close: a small graceful shrink + fade. Snappy (0.18s) so dismissing a
                // rack feels responsive, with matching eases so scale and opacity leave
                // together instead of one outlasting the other.
                var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0.9,
                    Duration = TimeSpan.FromSeconds(0.18),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(0.18),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                // scaleAnim drives both ScaleX and ScaleY below, so this Completed handler
                // fires once per clock (twice total) - Completed lives on the shared
                // Timeline, not the clock. Guard so the second firing can't re-enter
                // Close() while the first is still tearing the window down (WPF throws
                // InvalidOperationException from VerifyNotClosing in that case).
                scaleAnim.Completed += (s, args) =>
                {
                    if (_closeRequested) return;
                    _closeRequested = true;
                    this.Close();
                };
                RootScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
                RootScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
                this.BeginAnimation(OpacityProperty, opacityAnim);
            }
        }
    }
}