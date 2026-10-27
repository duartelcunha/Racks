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
                        SetShellMenuHandlers(() =>
                        {
                            _contextMenuIsOpen = false;
                        }, null);
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
                else if (!_isMinimized && this.ActualHeight != CollapsedHeight && _canAnimate)
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

        private void SetAsDesktopChild()
        {
            // Explorer briefly has no SHELLDLL_DefView while it restarts. Never sleep here (that
            // froze every rack and the tray for up to 10 s on the UI thread): look once and, if it
            // is missing, keep polling in the background and attach when it appears.
            if (shellView == IntPtr.Zero) shellView = ShellViewWaiter.FindDesktopView();
            if (shellView == IntPtr.Zero)
            {
                (_shellViewWaiter ??= new ShellViewWaiter(ShellViewWaiter.FindDesktopView, new Racks.Core.Abstractions.DispatcherDelayScheduler(),
                    found: h => { shellView = h; SetAsDesktopChild(); },
                    gaveUp: () => Debug.WriteLine("SHELLDLL_DefView not found; the rack stays a normal window.")))
                    .Start();
                return;
            }

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
                this.Height = CollapsedHeight;
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

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            _previousHeight = Instance.Height;
            KeepWindowBehind();
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
    }
}
