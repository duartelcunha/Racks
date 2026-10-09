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
            if (_isTopmost || _inHandleWindowMove || _physics?.Gliding == true) // physics owns the window mid-glide
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
                    _flick.Reset(this.Left, this.Top);
                    this.DragMove(); // blocks until the button is released
                    Instance.PosX = this.Left; Instance.PosY = this.Top; // saved once per drag, not on every move


                    // Flick-to-throw: if the rack was still moving when released, hand its
                    // velocity to the physics loop so it glides on with the same ice-rink
                    // friction/bounce as a push.
                    _dragMovingWinddow = false;
                    if (_physics != null && !_isLocked && !_isTopmost)
                    {
                        // Average speed over the last ~80 ms of the drag (0 if the rack was held still
                        // before release), so a parked rack isn't flung and one fast step can't spike it.
                        (_physics.Vx, _physics.Vy) = _flick.Release();
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

                _flick.Add(this.Left, this.Top); // samples for flick-to-throw on release

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
                        Util.RackPhysics.Impart(other._physics, otherRect, myRect, _flick.Current());
                }

            }
        }
    }
}
