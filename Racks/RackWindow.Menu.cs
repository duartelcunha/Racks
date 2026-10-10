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
                AnimateWindowHeight(CollapsedHeight, Instance.AnimationSpeed);
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

            MenuItem themeMenu = Util.ThemeMenu.Build(ApplyTheme);

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
                    // A sandboxed rack holds the real files that were dropped into it (dropping moves).
                    body = itemCount > 0
                        ? $"Remove this rack? {itemCount} item(s) will be moved back to your Desktop."
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
                            // AssignedFiles is persisted in HKCU; treat each entry as a plain leaf
                            // name by contract. Skip anything with a separator or ".." so a
                            // hand-edited/imported value can't Path.Combine its way out of the
                            // workspace and move an arbitrary file to the desktop.
                            if (string.IsNullOrEmpty(fileName) || System.IO.Path.GetFileName(fileName) != fileName)
                                continue;
                            string wpPath = System.IO.Path.Combine(DesktopIconManager.RacksWorkspacePath, fileName);
                            string destPath = System.IO.Path.Combine(desktopPath, fileName);
                            if (System.IO.File.Exists(wpPath) || System.IO.Directory.Exists(wpPath))
                            {
                                if (Util.SafeMove.TryMove(wpPath, destPath, out _) == Util.SafeMove.Result.Moved)
                                    returnedToDesktop.Add(destPath);
                            }
                        }
                    }
                    else if (isSandboxed && System.IO.Directory.Exists(Instance.Folder))
                    {
                        // This used to delete the sandbox with everything in it, permanently. It holds
                        // the user's moved files, so give them back; keep the rack if anything is left.
                        var report = Util.UninstallCleanup.ReturnFolderToDesktop(Instance.Folder, deskPath);
                        returnedToDesktop.AddRange(report.ReturnedPaths);
                        if (report.Kept > 0 || report.KeptFolders.Count > 0)
                        {
                            if (returnedToDesktop.Count > 0)
                                Util.DesktopIconPositioner.ArrangeInGrid(returnedToDesktop);
                            Racks.Views.RacksMessageBox.Show(
                                $"{report.Kept} item(s) could not be moved back to your Desktop (for example a file is open). " +
                                "The rack was kept so nothing is lost. Close the file and remove the rack again.",
                                Lang.TitleBarContextMenu_RemoveMessageBox_Title);
                            LoadFiles(_currentFolderPath);
                            return;
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
                    // The sandbox folder was emptied and removed above (ReturnFolderToDesktop). Nothing
                    // is ever deleted recursively here any more: see INV-REMOVE-1.
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
                Instance.SortBy = Racks.Rack.Sorting.SortToggle.Next(Instance.SortBy, 1);
                UpdateIcons();
                SortItems();
            };
            dateModifiedMenuItem.Click += async (s, args) =>
            {
                Instance.SortBy = Racks.Rack.Sorting.SortToggle.Next(Instance.SortBy, 3);
                UpdateIcons();
                SortItems();
            };

            dateCreatedMenuItem.Click += async (s, args) =>
            {
                Instance.SortBy = Racks.Rack.Sorting.SortToggle.Next(Instance.SortBy, 5);
                UpdateIcons();
                SortItems();
            };
            fileTypeMenuItem.Click += async (s, args) =>
            {
                Instance.SortBy = Racks.Rack.Sorting.SortToggle.Next(Instance.SortBy, 7);
                UpdateIcons();
                SortItems();
            };
            fileSizeMenuItem.Click += (s, args) =>
            {
                Instance.SortBy = Racks.Rack.Sorting.SortToggle.Next(Instance.SortBy, 9);
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
            contextMenu.Items.Add(themeMenu);
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
    }
}
