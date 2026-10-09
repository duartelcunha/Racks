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

        private ShellMenuHandlers? _menuHandlers;
        bool _dragMovingWinddow = false;

#pragma warning disable CS0649
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
        private readonly Util.FlickTracker _flick = new(); // release speed for flick-to-throw

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

        // One timer for the window's lifetime. MouseLeaveWindow used to create a new WinForms timer
        // (never disposed) on every mouse-leave, and each tick of it could create another
        // DispatcherTimer, so a rack that was entered and left often kept piling up 1 ms timers.
        private System.Windows.Forms.Timer? _mouseLeaveTimer;
        private DispatcherTimer? _deselectTimer;
        private bool _mouseLeaveAnimateActiveColor = true;

        private ShellViewWaiter? _shellViewWaiter;

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

        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListViewItem, object> _wiredContainers = new();

        public RackWindow(Instance instance)
        {
            InitializeComponent();
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
                IsAnchored = () => _isLocked || _isTopmost || _dragMovingWinddow || Instance.isWindowClosing,
                OnSettled = () => { Instance.PosX = this.Left; Instance.PosY = this.Top; HandleWindowMove(false); }
            };
            Util.RackPhysics.Register(_physics);

            _grayscaleEffect = (GrayscaleEffect)FindResource("ImageGrayscaleEffect");
            _grayscaleEffect.Strength = Instance.GrayScaleEnabled ? Instance.MaxGrayScaleStrength : 0;

            this.Width = instance.Width;
            this.Opacity = Instance.IdleOpacity;
            _currentFolderPath = instance.Folder;
            _isLocked = instance.IsLocked;
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

        private bool _fileWatcherWired;
        // Coalesces a burst of file-system events (a copy/save can fire many in a row) into
        // a single LoadFiles ~250ms after the last one, on the UI thread. Async (InvokeAsync)
        // so the watcher's threadpool callback never blocks, and debounced so we never stack
        // full folder rescans - the CPU-storm class of bug from earlier builds.
        private DispatcherTimer? _watcherDebounce;

        //public void KeepWindowBehind()

        // Shown when a user tries to delete an item from inside a protected rack. Keeps it
        // short and points them at the escape hatch (drag it out, or remove the whole rack).
        private bool _deleteBlockedNoticeOpen;

        // Path of the item the shell menu was opened on, so "Open in File Explorer" reveals it.
        private string? _lastRightClickedPath;
        // A rack is a "safe space" - its items can't be deleted from the shell menu - when
        // it physically owns the files: a sandboxed virtual rack (shortcuts in AppData) or a
        // desktop-filter rack (files parked in RacksWorkspace). Folder-backed racks that point
        // at a real user folder are NOT protected: that's the user's own folder and blocking
        // delete there would be surprising. Removing the whole rack still returns everything.
        private bool RackProtectsFromDelete =>
            Instance.IsDesktopFilterRack
            || (Instance.IsShortcutsOnly && InstanceController.IsInsideVirtualFramesRoot(Instance.Folder));

        // Creates the Ctrl+drop "reference" (hardlink, junction or .lnk) and returns the path it made.
        string CreateShortcut(string filePath, string shortcutFolder = null, string destFileName = null) =>
            ShortcutFactory.Create(filePath, !string.IsNullOrEmpty(shortcutFolder) ? shortcutFolder : Path.GetDirectoryName(filePath), destFileName);

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateFileExtensionIcon();
            UpdateHiddenFilesIcon();
            UpdateIconVisibility();
            AnimateChevron(_isMinimized, true, 0.01); // When 0 docked window won't open
            KeepWindowBehind();
            RegistryHelper rgh = new RegistryHelper(InstanceController.appName);

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

        private void Window_StateChanged(object sender, EventArgs e)
        {
            KeepWindowBehind();
            Debug.WriteLine("Window_StateChanged hide");
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
            try { _deselectTimer?.Stop(); } catch { }
            try { _shellViewWaiter?.Dispose(); } catch { }
            try { _mouseLeaveTimer?.Stop(); _mouseLeaveTimer?.Dispose(); _mouseLeaveTimer = null; } catch { }
            try { _menuHandlers?.Clear(); } catch { }
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
