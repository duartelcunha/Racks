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
        private void SaveInstance() => MainWindow._controller.WriteInstanceToKey(Instance);

        private void FileWrapPanel_GeneratorStatusChanged(object sender, EventArgs e)
        {
            if (_tilePanelWired) return;

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
                    // Only for a sandbox folder: a duplicated Desktop rack is shortcuts-only too, but its folder is the real Desktop.
                    if (effect == DragDropEffects.Move)
                    {
                        Racks.Rack.OrphanShortcutCleanup.RemoveForRack(Instance.IsShortcutsOnly, Instance.Folder, fileItem.Name);
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
                bool movedAway = Rack.DesktopRackClaims.IsGone(workspaceFullPath); // Explorer MOVED it: only our claim is left
                bool newOnDesktop = !movedAway && !desktopHadFileBefore && DesktopHasFile(fileName);

                if (newOnDesktop)
                {
                    // Explorer copied the item to the desktop (if it had MOVED it, the sandbox
                    // original would already be gone). Remove the sandbox original so it isn't a
                    // duplicate - but ONLY once the desktop copy is confirmed COMPLETE, and only
                    // to the Recycle Bin, never a permanent delete. Explorer copies large folders
                    // asynchronously, so a name-existence heuristic alone could delete the source
                    // mid-copy and lose data - the app's core promise is that a rack never loses a
                    // file. If we can't confirm the copy finished, we keep the original (a brief
                    // duplicate is fine; data loss is not).
                    if (!CopyLooksComplete(workspaceFullPath, desktopTarget))
                    {
                        Debug.WriteLine("Drag-out: desktop copy not confirmed complete; keeping sandbox original.");
                        return; // keep the rack's claim and the original - no data loss
                    }
                    if (!Infrastructure.FileSystem.FileOperationBroker.Default.DeleteToRecycleBin(workspaceFullPath, "drag-out: recycle workspace original").Succeeded)
                        Debug.WriteLine("Drag-out: could not recycle sandbox original; leaving it in place.");
                }
                else if (!movedAway)
                {
                    // Nothing new landed on the desktop. If the drop was ON the desktop area
                    // (not into another app), physically move the file out of the sandbox to
                    // the desktop ourselves; otherwise leave it in the rack untouched.
                    if (!DroppedOnDesktop(dropPt)) return;
                    if (File.Exists(desktopTarget) || Directory.Exists(desktopTarget)) return; // name clash: bail safely

                    if (!Infrastructure.FileSystem.FileOperationBroker.Default.Move(workspaceFullPath, desktopTarget, "drag-out: workspace to desktop").Completed)
                        return;
                }

                // Drop the rack's claim so the item stops showing in the rack.
                if (Instance.AssignedFiles != null && Instance.AssignedFiles.Remove(fileName))
                {
                    SaveInstance();
                }

                LoadFiles(_currentFolderPath);
            }
            catch (Exception ex) { Debug.WriteLine($"HandleDesktopRackDragOut failed: {ex.Message}"); }
        }

        // Confirm the desktop copy of `source` is a COMPLETE copy before we remove the sandbox
        // original. Explorer copies asynchronously, so the destination can exist while still being
        // written. For a file we compare size; for a folder we compare recursive file count and
        // total byte size. Any mismatch or error => not complete (caller keeps the original).
        private static bool CopyLooksComplete(string source, string dest)
        {
            try
            {
                if (File.Exists(source))
                {
                    if (!File.Exists(dest)) return false;
                    return new FileInfo(source).Length == new FileInfo(dest).Length;
                }
                if (Directory.Exists(source))
                {
                    if (!Directory.Exists(dest)) return false;
                    var (sc, ss) = CountAndSize(source);
                    var (dc, ds) = CountAndSize(dest);
                    return sc == dc && ss == ds;
                }
                return false;
            }
            catch { return false; }
        }

        private static (long count, long size) CountAndSize(string dir)
        {
            long count = 0, size = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                count++;
                try { size += new FileInfo(f).Length; } catch { }
            }
            return (count, size);
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
            Instance.EnableCustomItemsOrder = true;
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
                SaveInstance();
            }
            catch (Exception ex) { Debug.WriteLine($"Persist custom order failed: {ex.Message}"); }
        }

        private void ReassignDesktopFileToThisRack(string fullPath)
        {
            // This rack takes the name; every other rack that claimed it loses it.
            foreach (var lost in Rack.DesktopRackClaims.Claim(MainWindow._controller.Instances, Instance, Path.GetFileName(fullPath)))
                MainWindow._controller.WriteInstanceToKey(lost);

            SaveInstance();

            // Force refresh of all Desktop racks
            foreach (var window in MainWindow._controller._subWindows)
            {
                if (window.Instance.IsDesktopFilterRack)
                {
                    window.Dispatcher.Invoke(() => window.LoadFiles(window.Instance.Folder));
                }
            }

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
                // An in-rack reorder drag is in progress; its drop is not a file drop.
                if (_canChangeItemPosition) return;
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
                bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                bool wantsLinkInsteadOfMove = (Instance.LinkOnDrop || ctrlDown) && !shiftDown;

                // Collect source-parent directories so we can ping the shell once at the
                // end and force the Desktop view (and any other open Explorer windows
                // pointing at the same folder) to redraw without F5.
                var sourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dropMessages = new List<string>();
                int targetSlotIndex = -1;
                try
                {
                    var panel = FindParentOrChild<AnimatedTilePanel>(FileWrapPanel);
                    if (panel != null && panel.ActualWidth > 0 && string.IsNullOrEmpty(_dropIntoFolderPath))
                    {
                        System.Windows.Point pt = e.GetPosition(panel);
                        double itemW = panel.ItemWidth;
                        if (itemW > 0 && panel.SlotHeight > 0)
                        {
                            int cols = panel.Columns > 0 ? panel.Columns : Math.Max(1, (int)(panel.ActualWidth / itemW));
                            int col = Math.Max(0, Math.Min(cols - 1, (int)(pt.X / itemW)));
                            int row = Math.Max(0, (int)(pt.Y / panel.SlotHeight));
                            targetSlotIndex = Math.Min(FileItems.Count, row * cols + col);
                        }
                    }
                }
                catch { }
                var droppedPaths = new List<string>();

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
                            SaveInstance();
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
                            if (wantsLinkInsteadOfMove)
                            {
                                string shortcut = CreateShortcut(file, destinationDir, Path.GetFileName(destinationPath));
                                ReassignDesktopFileToThisRack(shortcut);
                                droppedPaths.Add(shortcut);
                            }
                            else
                            {
                                var moveResult = Infrastructure.FileSystem.FileOperationBroker.Default.Move(file, destinationPath, "drop into desktop rack");
                                if (moveResult.Completed)
                                {
                                    Util.Interop.NotifyShellMove(file, destinationPath, isDirectory: srcIsDir);
                                    ReassignDesktopFileToThisRack(destinationPath);
                                    droppedPaths.Add(destinationPath);
                                }
                                else
                                {
                                    Debug.WriteLine($"File move {moveResult.Status}: {moveResult.Reason}");
                                    if (!string.IsNullOrEmpty(moveResult.Reason)) dropMessages.Add(moveResult.Reason);
                                }
                            }
                            continue;
                        }
                        else
                        {
                            if (wantsLinkInsteadOfMove)
                            {
                                string shortcut = CreateShortcut(file, _currentFolderPath);
                                droppedPaths.Add(shortcut);
                            }
                            else
                            {
                                var moveResult = Infrastructure.FileSystem.FileOperationBroker.Default.Move(file, destinationPath, "drop into rack");
                                if (moveResult.Completed)
                                {
                                    Util.Interop.NotifyShellMove(file, destinationPath, isDirectory: srcIsDir);
                                    droppedPaths.Add(destinationPath);
                                }
                                else
                                {
                                    Debug.WriteLine($"File move {moveResult.Status}: {moveResult.Reason}");
                                    if (!string.IsNullOrEmpty(moveResult.Reason)) dropMessages.Add(moveResult.Reason);
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
                if (targetSlotIndex >= 0 && droppedPaths.Count > 0 && string.IsNullOrEmpty(_dropIntoFolderPath))
                {
                    InsertDroppedFilesIntoCustomOrder(droppedPaths, targetSlotIndex);
                    LoadFiles(_currentFolderPath);
                }

                foreach (var parent in sourceParents)
                {
                    Util.Interop.NotifyShellUpdateDir(parent);
                }

                // Surface any guard-rail messages from the file broker in a single dialog
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
            SaveInstance();

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
                    var moveResult = Infrastructure.FileSystem.FileOperationBroker.Default.Move(firstDroppedFile, dest, "drop into new rack");
                    string moveReason = moveResult.Reason;
                    if (moveResult.Completed)
                    {
                        Util.Interop.NotifyShellMove(firstDroppedFile, dest, isDirectory: srcIsDir);
                        Util.Interop.NotifyShellUpdateDir(Path.GetDirectoryName(firstDroppedFile)!);
                    }
                    else
                    {
                        // The broker blocked the move (special folder, collision, etc.).
                        // Fall back to a shortcut so the user's gesture still produced
                        // *something* useful in the new rack, and surface the reason.
                        Debug.WriteLine($"Bootstrap move {moveResult.Status}: {moveReason}");
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

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            if (_isMinimized) AnimateWindowHeight(CollapsedHeight, Instance.AnimationSpeed);
            if (!IsCursorWithinWindowBounds() && !_isDragging)
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive) TitleBarIconsFadeAnimation(true);
            }
            AnimateWindowOpacity(Instance.IdleOpacity, Instance.AnimationSpeed);
            _dragdropIntoFolder = false;
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
            bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            e.Effects = (Instance.LinkOnDrop || ctrlDown) ? DragDropEffects.Link : DragDropEffects.Move;
            e.Handled = true;
            try
            {
                var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this));
                if (hit?.VisualHit != null && FindParentOrChild<Border>(hit.VisualHit) is Border b && b.DataContext is FileItem fi && fi.IsFolder)
                {
                    _dropIntoFolderPath = fi.FullPath; _dragdropIntoFolder = true; return;
                }
            }
            catch { }
            _dropIntoFolderPath = null; _dragdropIntoFolder = false;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                e.Effects = (Instance.LinkOnDrop || ctrlDown) ? DragDropEffects.Link : DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                e.Effects = (Instance.LinkOnDrop || ctrlDown) ? DragDropEffects.Link : DragDropEffects.Move;
            }
            if (!_mouseIsOver && IsCursorWithinWindowBounds())
            {
                AnimateActiveColor(Instance.AnimationSpeed);
                if (Instance.HideTitleBarIconsWhenInactive) TitleBarIconsFadeAnimation(true);
            }
            AnimateWindowHeight(Instance.Height, Instance.AnimationSpeed);
            AnimateWindowOpacity(1, Instance.AnimationSpeed);
            _dragdropIntoFolder = true;
            if (Instance.Folder == "empty") StartParticles();
        }

        private void InsertDroppedFilesIntoCustomOrder(IEnumerable<string> droppedPaths, int targetIndex)
        {
            try
            {
                Instance.EnableCustomItemsOrder = true;
                var current = new List<Tuple<string, string>>(Instance.CustomOrderFiles ?? Enumerable.Empty<Tuple<string, string>>());
                if (current.Count == 0 && FileItems.Count > 0)
                {
                    for (int i = 0; i < FileItems.Count; i++)
                        if (FileItems[i]?.FullPath != null)
                            current.Add(new Tuple<string, string>(Instance.IsDesktopFilterRack ? FileItems[i].Name : GetFileId(FileItems[i].FullPath).ToString(), i.ToString()));
                }
                int insertPos = Math.Clamp(targetIndex, 0, current.Count);
                foreach (var path in droppedPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    string id = Instance.IsDesktopFilterRack ? Path.GetFileName(path) : GetFileId(path).ToString();
                    current.RemoveAll(t => t.Item1 == id);
                    current.Insert(insertPos, new Tuple<string, string>(id, insertPos.ToString()));
                    insertPos++;
                }
                for (int i = 0; i < current.Count; i++) current[i] = new Tuple<string, string>(current[i].Item1, i.ToString());
                Instance.CustomOrderFiles = current;
                SaveInstance();
            }
            catch (Exception ex) { Debug.WriteLine($"Insert custom order failed: {ex.Message}"); }
        }
    }
}
