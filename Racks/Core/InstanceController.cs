using Racks;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;

public class InstanceController
{
    // Product name. Was "DeskFrame", now "Racks". A one-time registry migration
    // (MigrateLegacyRegistry) copies old HKCU\SOFTWARE\DeskFrame keys to the new
    // root on first launch so existing frames survive the rebrand.
    public static string appName = "Racks";
    public const string LegacyAppName = "DeskFrame";
    public bool isInitializingInstances = false;
    public List<Instance> Instances = new List<Instance>();
    public RegistryHelper reg = new RegistryHelper(appName);
    public List<RackWindow> _subWindows = new List<RackWindow>();
    public List<IntPtr> _subWindowsPtr = new List<IntPtr>();
    private bool Visible = true;

    // Sandbox path used by virtual ("shortcuts-only") frames. Created on first use.
    public static string VirtualFramesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        appName,
        "VirtualFrames");

    public static bool IsInsideVirtualFramesRoot(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(VirtualFramesRoot).TrimEnd(Path.DirectorySeparatorChar);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Rebuild the %USERPROFILE%\Racks mirror (one junction per virtual rack,
    // named after the rack's title, pointing at the sandbox). Called from
    // InitInstances, Add*, Remove*, and from Instance.TitleText setter. Safe
    // to call any time — Rebuild is idempotent and tolerant of missing state.
    public static void RefreshMirror()
    {
        try
        {
            var ctrl = MainWindow._controller;
            if (ctrl == null || ctrl.isInitializingInstances) return;
            var virtualRacks = ctrl.Instances
                .Where(i => IsInsideVirtualFramesRoot(i.Folder))
                .Select(i => (i.TitleText ?? "Rack", i.Folder))
                .ToList();
            Racks.Util.RackMirror.Rebuild(virtualRacks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshMirror failed: {ex.Message}");
        }
    }

    // Copy HKCU\SOFTWARE\DeskFrame tree to HKCU\SOFTWARE\Racks once, so users who
    // upgrade from the legacy DeskFrame build don't lose their frames.
    public static void MigrateLegacyRegistry()
    {
        try
        {
            using var newRoot = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{appName}");
            if (newRoot != null) return; // already migrated

            using var legacy = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{LegacyAppName}");
            if (legacy == null) return;

            using var dest = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\{appName}");
            CopyRegistryKey(legacy, dest);
            dest.SetValue("_MigratedFromDeskFrame", "1");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MigrateLegacyRegistry failed: {ex.Message}");
        }
    }

    private static void CopyRegistryKey(RegistryKey source, RegistryKey dest)
    {
        foreach (var name in source.GetValueNames())
        {
            object? val = source.GetValue(name);
            if (val != null) dest.SetValue(name, val, source.GetValueKind(name));
        }
        foreach (var sub in source.GetSubKeyNames())
        {
            using var s = source.OpenSubKey(sub);
            using var d = dest.CreateSubKey(sub);
            if (s != null && d != null) CopyRegistryKey(s, d);
        }
    }
    public void WriteOverInstanceToKey(Instance instance, string oldKey)
    {

        try
        {
            Debug.WriteLine($"old: {oldKey}\t{instance.Name}");

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@$"SOFTWARE\{appName}\Instances\{instance.Name}"))
            {
                key.SetValue("Name", instance.Name!);
                key.SetValue("PosX", instance.PosX!);
                key.SetValue("PosY", instance.PosY!);
                key.SetValue("Width", instance.Width!);
                key.SetValue("Height", instance.Height!);
                key.SetValue("IconSize", instance.IconSize!);
                key.SetValue("IdleOpacity", instance.IdleOpacity!);
                key.SetValue("AnimationSpeed", instance.AnimationSpeed!);
                key.SetValue("MaxGrayScaleStrength", instance.MaxGrayScaleStrength!);
                key.SetValue("GrayScaleEnabled", instance.GrayScaleEnabled!);
                key.SetValue("GrayScaleEnabled_InactiveOnly", instance.GrayScaleEnabled_InactiveOnly!);
                key.SetValue("Minimized", instance.Minimized!);
                key.SetValue("Folder", instance.Folder!);
                key.SetValue("TitleFontFamily", instance.TitleFontFamily!);
                key.SetValue("ItemFontFamily", instance.ItemFontFamily!);
                key.SetValue("ShowHiddenFiles", instance.ShowHiddenFiles!);
                key.SetValue("LastAccesedToFirstRow", instance.LastAccesedToFirstRow);
                key.SetValue("EnableCustomItemsOrder", instance.EnableCustomItemsOrder);
                key.SetValue("ShowFileExtension", instance.ShowFileExtension!);
                key.SetValue("ShowFileExtensionIcon", instance.ShowFileExtensionIcon!);
                key.SetValue("ShowHiddenFilesIcon", instance.ShowHiddenFilesIcon!);
                key.SetValue("ShowDisplayName", instance.ShowDisplayName!);
                key.SetValue("IsLocked", instance.IsLocked!);
                key.SetValue("ShowInGrid", instance.ShowInGrid!);
                key.SetValue("AutoExpandonCursor", instance.AutoExpandonCursor);
                key.SetValue("ShowShortcutArrow", instance.ShowShortcutArrow);
                key.SetValue("FolderOpenInsideFrame", instance.FolderOpenInsideFrame);
                key.SetValue("HideTitleBarIconsWhenInactive", instance.HideTitleBarIconsWhenInactive);
                key.SetValue("SnapWidthToIconWidth", instance.SnapWidthToIconWidth);
                key.SetValue("SnapWidthToIconWidth_PlusScrollbarWidth", instance.SnapWidthToIconWidth_PlusScrollbarWidth);
                key.SetValue("CheckFolderSize", instance.CheckFolderSize);
                key.SetValue("LinkOnDrop", instance.LinkOnDrop);
                key.SetValue("SnapToGrid", instance.SnapToGrid);
                key.SetValue("GridSize", instance.GridSize);
                key.SetValue("AutoRouteRegex", instance.AutoRouteRegex ?? "");
                key.SetValue("BackgroundImagePath", instance.BackgroundImagePath ?? "");
                key.SetValue("PinToTop", instance.PinToTop);
                key.SetValue("TitleBarColor", instance.TitleBarColor!);
                key.SetValue("TitleTextColor", instance.TitleTextColor!);
                key.SetValue("ActiveTitleTextColor", instance.ActiveTitleTextColor!);
                key.SetValue("TitleTextAlignment", instance.TitleTextAlignment.ToString());
                key.SetValue("TitleText", instance.TitleText != null ? instance.TitleText : instance.Name);
                key.SetValue("BorderColor", instance.BorderColor!);
                key.SetValue("BorderEnabled", instance.BorderEnabled!);
                key.SetValue("ActiveBorderEnabled", instance.ActiveBorderEnabled!);
                key.SetValue("ActiveBackgroundEnabled", instance.ActiveBackgroundEnabled!);
                key.SetValue("ActiveTitleTextEnabled", instance.ActiveTitleTextEnabled!);
                key.SetValue("FileFilterRegex", instance.FileFilterRegex!);
                key.SetValue("FileFilterHideRegex", instance.FileFilterHideRegex!);
                key.SetValue("ListViewBackgroundColor", instance.ListViewBackgroundColor!);
                key.SetValue("ActiveBackgroundColor", instance.ActiveBackgroundColor!);
                key.SetValue("ActiveBorderColor", instance.ActiveBorderColor!);
                key.SetValue("ListViewFontColor", instance.ListViewFontColor!);
                key.SetValue("ListViewFontShadowColor", instance.ListViewFontShadowColor!);
                key.SetValue("Opacity", instance.Opacity);
                key.SetValue("SortBy", instance.SortBy);
                key.SetValue("FolderOrder", instance.FolderOrder);
                if (instance.ShowOnVirtualDesktops != null && instance.ShowOnVirtualDesktops.Length > 0)
                {
                    key.SetValue("ShowOnVirtualDesktops", string.Join(",", instance.ShowOnVirtualDesktops));
                }
                if (instance.LastAccessedFiles != null && instance.LastAccessedFiles.Count > 0)
                {
                    key.SetValue("LastAccessedFiles", instance.LastAccessedFiles.ToArray(), RegistryValueKind.MultiString);
                }
                if (instance.CustomOrderFiles != null && instance.CustomOrderFiles.Count > 0)
                {
                    key.SetValue("CustomOrderFiles", instance.CustomOrderFiles.Select(t => $"{t.Item1},{t.Item2}").ToArray(), RegistryValueKind.MultiString);
                }
                key.SetValue("TitleFontSize", instance.TitleFontSize);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WriteOverInstanceToKey failed: {ex.Message}");
        }
        Registry.CurrentUser.DeleteSubKey(@$"SOFTWARE\{appName}\Instances\{oldKey}", throwOnMissingSubKey: false);
    }
    private void InitDetails()
    {
        if (reg.KeyExistsRoot("blurBackground"))
        {
            ChangeBlur(reg.ReadKeyValueRoot("blurBackground") as bool? ?? false);
        }
    }
    public void WriteInstanceToKey(Instance instance)
    {
        if (string.IsNullOrEmpty(instance.Name))
        {
            Debug.WriteLine("instance.Name is null, Instance is not written to key");
            return;
        }
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(instance.GetKeyLocation()))
            {
                if (instance.Name != null) key.SetValue("Name", instance.Name);
                key.SetValue("PosX", instance.PosX);
                key.SetValue("PosY", instance.PosY);
                key.SetValue("Width", instance.Width);
                key.SetValue("Height", instance.Height);
                key.SetValue("IconSize", instance.IconSize);
                key.SetValue("IdleOpacity", instance.IdleOpacity);
                key.SetValue("AnimationSpeed", instance.AnimationSpeed);
                key.SetValue("MaxGrayScaleStrength", instance.MaxGrayScaleStrength!);
                key.SetValue("GrayScaleEnabled", instance.GrayScaleEnabled!);
                key.SetValue("GrayScaleEnabled_InactiveOnly", instance.GrayScaleEnabled_InactiveOnly!);
                key.SetValue("Minimized", instance.Minimized);
                if (instance.Folder != null) key.SetValue("Folder", instance.Folder);
                if (instance.TitleFontFamily != null) key.SetValue("TitleFontFamily", instance.TitleFontFamily);
                if (instance.ItemFontFamily != null) key.SetValue("ItemFontFamily", instance.ItemFontFamily);
                key.SetValue("ShowHiddenFiles", instance.ShowHiddenFiles);
                key.SetValue("LastAccesedToFirstRow", instance.LastAccesedToFirstRow);
                key.SetValue("EnableCustomItemsOrder", instance.EnableCustomItemsOrder);
                key.SetValue("ShowFileExtension", instance.ShowFileExtension);
                key.SetValue("ShowFileExtensionIcon", instance.ShowFileExtensionIcon);
                key.SetValue("ShowHiddenFilesIcon", instance.ShowHiddenFilesIcon);
                key.SetValue("ShowDisplayName", instance.ShowDisplayName);
                key.SetValue("IsLocked", instance.IsLocked);
                key.SetValue("ShowInGrid", instance.ShowInGrid);
                key.SetValue("AutoExpandonCursor", instance.AutoExpandonCursor);
                key.SetValue("ShowShortcutArrow", instance.ShowShortcutArrow);
                key.SetValue("FolderOpenInsideFrame", instance.FolderOpenInsideFrame);
                key.SetValue("HideTitleBarIconsWhenInactive", instance.HideTitleBarIconsWhenInactive);
                key.SetValue("SnapWidthToIconWidth", instance.SnapWidthToIconWidth);
                key.SetValue("SnapWidthToIconWidth_PlusScrollbarWidth", instance.SnapWidthToIconWidth_PlusScrollbarWidth);
                key.SetValue("CheckFolderSize", instance.CheckFolderSize);
                key.SetValue("LinkOnDrop", instance.LinkOnDrop);
                key.SetValue("SnapToGrid", instance.SnapToGrid);
                key.SetValue("GridSize", instance.GridSize);
                key.SetValue("AutoRouteRegex", instance.AutoRouteRegex ?? "");
                key.SetValue("BackgroundImagePath", instance.BackgroundImagePath ?? "");
                key.SetValue("PinToTop", instance.PinToTop);
                key.SetValue("DropShadowEnabled", instance.DropShadowEnabled);
                key.SetValue("GradientBackgroundEnabled", instance.GradientBackgroundEnabled);
                key.SetValue("DisableAnimations", instance.DisableAnimations);
                key.SetValue("IsDesktopFilterRack", instance.IsDesktopFilterRack);
                if (instance.AssignedFiles != null && instance.AssignedFiles.Count > 0)
                {
                    key.SetValue("AssignedFiles", string.Join("|", instance.AssignedFiles));
                }
                else
                {
                    key.DeleteValue("AssignedFiles", false);
                }
                if (instance.TitleBarColor != null) key.SetValue("TitleBarColor", instance.TitleBarColor);
                if (instance.TitleTextColor != null) key.SetValue("TitleTextColor", instance.TitleTextColor);
                if (instance.ActiveTitleTextColor != null) key.SetValue("ActiveTitleTextColor", instance.ActiveTitleTextColor);
                key.SetValue("TitleTextAlignment", instance.TitleTextAlignment.ToString());
                if (instance.TitleText != null) key.SetValue("TitleText", instance.TitleText);
                if (instance.BorderColor != null) key.SetValue("BorderColor", instance.BorderColor);
                key.SetValue("BorderEnabled", instance.BorderEnabled);
                key.SetValue("ActiveBorderEnabled", instance.ActiveBorderEnabled!);
                key.SetValue("ActiveBackgroundEnabled", instance.ActiveBackgroundEnabled!);
                key.SetValue("ActiveTitleTextEnabled", instance.ActiveTitleTextEnabled!);
                if (instance.FileFilterRegex != null) key.SetValue("FileFilterRegex", instance.FileFilterRegex);
                if (instance.FileFilterHideRegex != null) key.SetValue("FileFilterHideRegex", instance.FileFilterHideRegex);
                if (instance.ListViewBackgroundColor != null) key.SetValue("ListViewBackgroundColor", instance.ListViewBackgroundColor);
                if (instance.ActiveBackgroundColor != null) key.SetValue("ActiveBackgroundColor", instance.ActiveBackgroundColor);
                if (instance.ActiveBorderColor != null) key.SetValue("ActiveBorderColor", instance.ActiveBorderColor);
                if (instance.ListViewFontColor != null) key.SetValue("ListViewFontColor", instance.ListViewFontColor);
                if (instance.ListViewFontShadowColor != null) key.SetValue("ListViewFontShadowColor", instance.ListViewFontShadowColor);
                key.SetValue("Opacity", instance.Opacity);
                key.SetValue("SortBy", instance.SortBy);
                key.SetValue("FolderOrder", instance.FolderOrder);
                if (instance.ShowOnVirtualDesktops != null) key.SetValue("ShowOnVirtualDesktops", string.Join(",", instance.ShowOnVirtualDesktops));
                if (instance.LastAccessedFiles != null && instance.LastAccessedFiles.Count > 0)
                {
                    key.SetValue("LastAccessedFiles", instance.LastAccessedFiles.ToArray(), RegistryValueKind.MultiString);
                }
                if (instance.CustomOrderFiles != null && instance.CustomOrderFiles.Count > 0)
                {
                    key.SetValue("CustomOrderFiles", instance.CustomOrderFiles.Select(t => $"{t.Item1},{t.Item2}").ToArray(), RegistryValueKind.MultiString);
                }
                key.SetValue("TitleFontSize", instance.TitleFontSize);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WriteInstanceToKey failed: {ex.Message}");
        }
    }

    public void AddInstance()
    {
        var existingEmptyInstance = Instances.FirstOrDefault(instance => instance.Name == "empty");

        if (existingEmptyInstance != null)
        {
            Instances.Remove(existingEmptyInstance);
        }

        var inst = new Instance("empty", false);
        if (Racks.Util.Interop.GetCursorPos(out Racks.Util.Interop.POINT pt))
        {
            inst.PosX = pt.X - 150;
            inst.PosY = pt.Y - 200;
            var screenX = System.Windows.SystemParameters.VirtualScreenWidth;
            var screenY = System.Windows.SystemParameters.VirtualScreenHeight;
            if (inst.PosX < 0) inst.PosX = 0;
            if (inst.PosY < 0) inst.PosY = 0;
            if (inst.PosX > screenX - 300) inst.PosX = screenX - 300;
            if (inst.PosY > screenY - 400) inst.PosY = screenY - 400;
        }
        Instances.Add(inst);

        MainWindow._controller.WriteInstanceToKey(inst);
        var subWindow = new RackWindow(inst);
        subWindow.ChangeBackgroundOpacity(inst.Opacity);
        _subWindows.Add(subWindow);
        subWindow.Show();
        // The rack appears fully placed at the cursor-centered PosX/PosY computed above.
        // The old StartPlacementMode() "follow the cursor then click to drop" flow was
        // removed: it wrote screen coords to a window that SetAsDesktopChild reparents as
        // a desktop child (whose Left/Top are parent-client coords), so it mis-positioned
        // the rack, and its per-frame CompositionTarget.Rendering loop fought the push
        // physics on the UI thread, freezing the app. Drag the title bar to reposition.
        _subWindowsPtr.Add(new WindowInteropHelper(subWindow).Handle);
        InitDetails();
        RefreshMirror();
    }

    // Create a new virtual rack pre-filled with style/behavior settings copied from
    // an existing one. Items aren't copied — only the rack's appearance and rules.
    // Position is offset 30px so the new rack doesn't completely cover the source.
    public void DuplicateInstance(Instance source)
    {
        var existingEmpty = Instances.FirstOrDefault(i => i.Name == "empty");
        if (existingEmpty != null) Instances.Remove(existingEmpty);

        string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        double sWidth = source.Width < 50 ? 300 : source.Width;
        double sHeight = source.Height < 50 ? 400 : source.Height;

        double newPosX = source.PosX;
        double newPosY = source.PosY;
        int maxAttempts = 50;
        for (int i = 0; i < maxAttempts; i++)
        {
            bool overlaps = Instances.Any(r => 
                newPosX < r.PosX + (r.Width < 50 ? 300 : r.Width) && 
                newPosX + sWidth > r.PosX && 
                newPosY < r.PosY + (r.Height < 50 ? 400 : r.Height) && 
                newPosY + sHeight > r.PosY);
            if (!overlaps) break;
            newPosX += sWidth + 20;
            if (newPosX > System.Windows.SystemParameters.PrimaryScreenWidth - sWidth)
            {
                newPosX = 100;
                newPosY += sHeight + 20;
            }
        }

        var inst = new Instance("empty", false)
        {
            Folder = desktopFolder,
            IsDesktopFilterRack = true,
            Name = "New Rack",
            TitleText = (source.TitleText ?? "Rack") + " (copy)",
            IsShortcutsOnly = true,
            ShowShortcutArrow = source.ShowShortcutArrow,
            LinkOnDrop = source.LinkOnDrop,
            SnapToGrid = source.SnapToGrid,
            GridSize = source.GridSize,
            PinToTop = source.PinToTop,
            BackgroundImagePath = source.BackgroundImagePath,
            AutoRouteRegex = "", // don't duplicate routing — would race with source
            PosX = newPosX,
            PosY = newPosY,
            Width = source.Width,
            Height = source.Height,
            Opacity = source.Opacity,
            IdleOpacity = source.IdleOpacity,
            IconSize = source.IconSize,
            TitleFontFamily = source.TitleFontFamily,
            TitleFontSize = source.TitleFontSize,
            ItemFontFamily = source.ItemFontFamily,
            TitleBarColor = source.TitleBarColor,
            TitleTextColor = source.TitleTextColor,
            BorderColor = source.BorderColor,
            BorderEnabled = source.BorderEnabled,
            ListViewBackgroundColor = source.ListViewBackgroundColor,
            ListViewFontColor = source.ListViewFontColor,
            ListViewFontShadowColor = source.ListViewFontShadowColor,
        };
        Instances.Add(inst);
        WriteInstanceToKey(inst);

        var subWindow = new RackWindow(inst);
        subWindow.ChangeBackgroundOpacity(inst.Opacity);
        _subWindows.Add(subWindow);
        subWindow.Show();
        _subWindowsPtr.Add(new WindowInteropHelper(subWindow).Handle);
        InitDetails();
        RefreshMirror();
    }

    public void AddVirtualInstance()
    {
        var existingEmpty = Instances.FirstOrDefault(i => i.Name == "empty");
        if (existingEmpty != null) Instances.Remove(existingEmpty);

        string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        // Find a safe placement that doesn't overlap existing racks
        double startX = 100;
        double startY = 100;
        double width = 300;
        double height = 400;
        if (Racks.Util.Interop.GetCursorPos(out Racks.Util.Interop.POINT pt))
        {
            startX = pt.X - (width / 2);
            startY = pt.Y - (height / 2);
            var screenX = System.Windows.SystemParameters.VirtualScreenWidth;
            var screenY = System.Windows.SystemParameters.VirtualScreenHeight;
            if (startX < 0) startX = 0;
            if (startY < 0) startY = 0;
            if (startX > screenX - width) startX = screenX - width;
            if (startY > screenY - height) startY = screenY - height;
        }

        var inst = new Instance("empty", false)
        {
            Folder = desktopFolder,
            IsDesktopFilterRack = true,
            Name = "New Rack",
            TitleText = "New rack",
            ShowShortcutArrow = false,
            LinkOnDrop = false, // We want to MOVE files to RacksWorkspace, not link them!
            PosX = startX,
            PosY = startY,
            AssignedFiles = new List<string>()
        };
        Instances.Add(inst);
        WriteInstanceToKey(inst);

        var subWindow = new RackWindow(inst);
        subWindow.ChangeBackgroundOpacity(inst.Opacity);
        _subWindows.Add(subWindow);
        subWindow.Show();
        // The rack appears fully placed at the cursor-centered PosX/PosY computed above
        // (SetAsDesktopChild converts that to the desktop-child client coords). Drag the
        // title bar to move it afterwards.
        _subWindowsPtr.Add(new WindowInteropHelper(subWindow).Handle);
        InitDetails();
        RefreshMirror();
    }
    public void RemoveInstance(Instance instance, RackWindow window)
    {
        Instances.Remove(instance);
        _subWindows.Remove(window);
        RefreshMirror();
    }
    public void ChangeBlur(bool toBlur)
    {
        foreach (RackWindow window in _subWindows)
        {
            window.BackgroundType(toBlur);
        }
    }

    public void ChangeBackgroundOpacity(int num)
    {
        foreach (RackWindow window in _subWindows)
        {
            window.ChangeBackgroundOpacity(num);
        }
    }
    public void CheckFrameWindowsLive(bool forceFullReload = false)
    {
        int closedCount = 0;
        foreach (var window in _subWindows)
        {
            if (new WindowInteropHelper(window).Handle == IntPtr.Zero) closedCount++;
        }
        if (forceFullReload || (_subWindows.Count != 0 && !isInitializingInstances))
        {
            if (forceFullReload || closedCount == _subWindows.Count)
            {
                foreach (var window in _subWindows.ToList())
                {
                    window.Close();
                }
                _subWindows.Clear();
                _subWindowsPtr.Clear();
                Instances.Clear();
                InitInstances();
            }
            if (closedCount != _subWindows.Count)
            {
                foreach (var window in _subWindows)
                {
                    window.AdjustPosition();
                }
            }
        }
    }
    public void ChangeVisibility()
    {
        if (Visible)
        {
            foreach (var window in _subWindows)
            {
                window.Hide();
            }
            Visible = false;
        }
        else
        {
            foreach (var window in _subWindows)
            {
                window.Show();
            }
            Visible = true;
        }
    }
    // One-shot migration marker: an earlier build briefly defaulted virtual
    // racks to LinkOnDrop=true, which left .lnk shortcuts in the rack while
    // the original stayed on the Desktop — users perceived this as duplication.
    // We revert those racks back to move-on-drop here. Marker so we don't fight
    // a user who deliberately re-enables link mode after this runs.
    private const string LinkOnDropRevertedKey = "VirtualRackLinkOnDropRevertedToMove";

    // One-shot: flip FolderOpenInsideFrame to false for every rack. The
    // upstream default was inside-rack navigation; users find the OS-native
    // "double-click opens Explorer" behavior more intuitive. Marker prevents
    // overriding a user-driven toggle on subsequent launches.
    private const string FolderOpenInsideFrameDefaultFlippedKey = "FolderOpenInsideFrameDefaultedToExplorer";

    public void InitInstances()
    {
        isInitializingInstances = true;
        Debug.WriteLine("Init...");
        bool needsRevertMigration = !reg.KeyExistsRoot(LinkOnDropRevertedKey);
        bool needsFolderOpenMigration = !reg.KeyExistsRoot(FolderOpenInsideFrameDefaultFlippedKey);
        try
        {
            using (RegistryKey instancesKey = Registry.CurrentUser.OpenSubKey(@$"SOFTWARE\{appName}\Instances")!)
            {
                if (instancesKey != null)
                {
                    string[] instanceNames = instancesKey.GetSubKeyNames();
                    Debug.WriteLine($"\n");

                    foreach (var item in instanceNames)
                    {
                        Debug.WriteLine($"subkeyname: {item}");
                    }

                    foreach (string instance in instanceNames)
                    {

                        Debug.WriteLine($"instanceNames: {instance}");


                        using (RegistryKey instanceKey = Registry.CurrentUser.OpenSubKey(@$"SOFTWARE\{appName}\Instances\{instance}")!)
                        {
                            Debug.WriteLine("valid");
                            if (instanceKey != null)
                            {
                                Instance temp = new Instance("", false);
                                Debug.WriteLine("valied 2");

                                foreach (var valueName in instanceKey.GetValueNames())   // Read all values under the current subkey
                                {
                                    object value = instanceKey.GetValue(valueName)!;

                                    if (value != null)
                                    {
                                        switch (valueName)
                                        {
                                            case "PosX":
                                                if (double.TryParse(value.ToString(), out double parsedPosX))
                                                {
                                                    temp.PosX = parsedPosX;
                                                    Debug.WriteLine($"PosX added: {temp.PosX}");
                                                }
                                                else
                                                {
                                                    Debug.WriteLine("Failed to parse PosX.");
                                                }
                                                break;
                                            case "PosY":
                                                if (double.TryParse(value.ToString(), out double parsedPosY))
                                                {
                                                    temp.PosY = parsedPosY;
                                                    Debug.WriteLine($"PosY added: {temp.PosY}");
                                                }
                                                else
                                                {
                                                    Debug.WriteLine("Failed to parse PosY.");
                                                }
                                                break;

                                            case "Width":
                                                if (double.TryParse(value.ToString(), out double parsedWidth))
                                                {
                                                    temp.Width = parsedWidth;
                                                    Debug.WriteLine($"Width added: {temp.Width}");
                                                }
                                                else
                                                {
                                                    Debug.WriteLine("Failed to parse Width.");
                                                }
                                                break;

                                            case "Height":
                                                if (double.TryParse(value.ToString(), out double parsedHeight))
                                                {
                                                    temp.Height = parsedHeight;
                                                }
                                                break;
                                            case "IconSize":
                                                if (int.TryParse(value.ToString(), out int parseIconSize))
                                                {
                                                    temp.IconSize = parseIconSize;
                                                }
                                                break;
                                            case "IdleOpacity":
                                                if (double.TryParse(value.ToString(), out double parsedIdleOpacity))
                                                {
                                                    temp.IdleOpacity = parsedIdleOpacity;
                                                }

                                                break;
                                            case "AnimationSpeed":
                                                if (double.TryParse(value.ToString(), out double parsedAnimationSpeed))
                                                {
                                                    temp.AnimationSpeed = parsedAnimationSpeed;
                                                }
                                                break;

                                            case "MaxGrayScaleStrength":
                                                if (double.TryParse(value.ToString(), out double parsedMaxGrayScaleStrength))
                                                {
                                                    temp.MaxGrayScaleStrength = parsedMaxGrayScaleStrength;
                                                }
                                                break;
                                            case "GrayScaleEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_GrayScaleEnabled)) temp.GrayScaleEnabled = parsed_GrayScaleEnabled;
                                                Debug.WriteLine($"GrayScaleEnabled added\t{temp.GrayScaleEnabled}");
                                                break;
                                            case "GrayScaleEnabled_InactiveOnly":
                                                if (bool.TryParse(value.ToString(), out bool parsed_GrayScaleEnabled_InactiveOnly)) temp.GrayScaleEnabled_InactiveOnly = parsed_GrayScaleEnabled_InactiveOnly;
                                                Debug.WriteLine($"GrayScaleEnabled_InactiveOnly added\t{temp.GrayScaleEnabled_InactiveOnly}");
                                                break;
                                            case "Name":
                                                temp.Name = value.ToString()!;
                                                Debug.WriteLine($"Name added\t{temp.Name}");
                                                break;
                                            case "Folder":
                                                Debug.WriteLine("+trest:  " + value.ToString());
                                                temp.Folder = value.ToString()!;
                                                Debug.WriteLine($"Folder added\t{temp.Folder}");
                                                break;
                                            case "TitleFontFamily":
                                                temp.TitleFontFamily = value.ToString()!;
                                                Debug.WriteLine($"TitleFontFamily added\t{temp.TitleFontFamily}");
                                                break;
                                            case "ItemFontFamily":
                                                temp.ItemFontFamily = value.ToString()!;
                                                Debug.WriteLine($"ItemFontFamily added\t{temp.ItemFontFamily}");
                                                break;
                                            case "Minimized":
                                                if (bool.TryParse(value.ToString(), out bool parsed_Minimized)) temp.Minimized = parsed_Minimized;
                                                Debug.WriteLine($"Minimized added\t{temp.Minimized}");
                                                break;
                                            case "ShowHiddenFiles":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowHiddenFiles)) temp.ShowHiddenFiles = parsed_ShowHiddenFiles;
                                                Debug.WriteLine($"ShowHiddenFiles added\t{temp.ShowHiddenFiles}");
                                                break;
                                            case "LastAccesedToFirstRow":
                                                if (bool.TryParse(value.ToString(), out bool parsed_LastAccesedToFirstRow)) temp.LastAccesedToFirstRow = parsed_LastAccesedToFirstRow;
                                                Debug.WriteLine($"LastAccesedToFirstRow added\t{temp.LastAccesedToFirstRow}");
                                                break;
                                            case "EnableCustomItemsOrder":
                                                if (bool.TryParse(value.ToString(), out bool parsed_EnableCustomItemsOrder)) temp.EnableCustomItemsOrder = parsed_EnableCustomItemsOrder;
                                                Debug.WriteLine($"EnableCustomItemsOrder added\t{temp.EnableCustomItemsOrder}");
                                                break;
                                            case "ShowFileExtension":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowFileExtension)) temp.ShowFileExtension = parsed_ShowFileExtension;
                                                Debug.WriteLine($"ShowFileExtension added\t{temp.ShowFileExtension}");
                                                break;
                                            case "ShowFileExtensionIcon":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowFileExtensionIcon)) temp.ShowFileExtensionIcon = parsed_ShowFileExtensionIcon;
                                                Debug.WriteLine($"ShowFileExtensionIcon added\t{temp.ShowFileExtensionIcon}");
                                                break;
                                            case "ShowHiddenFilesIcon":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowHiddenFilesIcon)) temp.ShowHiddenFilesIcon = parsed_ShowHiddenFilesIcon;
                                                Debug.WriteLine($"ShowHiddenFilesIcon added\t{temp.ShowHiddenFilesIcon}");
                                                break;
                                            case "ShowDisplayName":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowDisplayName)) temp.ShowDisplayName = parsed_ShowDisplayName;
                                                Debug.WriteLine($"ShowDisplayName added\t{temp.ShowDisplayName}");
                                                break;
                                            case "IsLocked":
                                                if (bool.TryParse(value.ToString(), out bool parsed_IsLocked)) temp.IsLocked = parsed_IsLocked;
                                                Debug.WriteLine($"IsLocked added\t{temp.IsLocked}");
                                                break;
                                            case "ShowInGrid":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowInGrid)) temp.ShowInGrid = parsed_ShowInGrid;
                                                Debug.WriteLine($"ShowInGrid added\t{temp.ShowInGrid}");
                                                break;
                                            case "AutoExpandonCursor":
                                                if (bool.TryParse(value.ToString(), out bool parsed_AutoExpandonCursor)) temp.AutoExpandonCursor = parsed_AutoExpandonCursor;
                                                Debug.WriteLine($"AutoExpandonCursor added\t{temp.AutoExpandonCursor}");
                                                break;
                                            case "ShowShortcutArrow":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ShowShortcutArrow)) temp.ShowShortcutArrow = parsed_ShowShortcutArrow;
                                                Debug.WriteLine($"ShowShortcutArrow added\t{temp.ShowShortcutArrow}");
                                                break;
                                            case "FolderOpenInsideFrame":
                                                if (bool.TryParse(value.ToString(), out bool parsed_FolderOpenInsideFrame)) temp.FolderOpenInsideFrame = parsed_FolderOpenInsideFrame;
                                                Debug.WriteLine($"FolderOpenInsideFrame added\t{temp.FolderOpenInsideFrame}");
                                                break;
                                            case "HideTitleBarIconsWhenInactive":
                                                if (bool.TryParse(value.ToString(), out bool parsed_HideTitleBarIconsWhenInactive)) temp.HideTitleBarIconsWhenInactive = parsed_HideTitleBarIconsWhenInactive;
                                                Debug.WriteLine($"HideTitleBarIconsWhenInactive added\t{temp.HideTitleBarIconsWhenInactive}");
                                                break;
                                            case "SnapWidthToIconWidth":
                                                if (bool.TryParse(value.ToString(), out bool parsed_SnapWidthToIconWidth)) temp.SnapWidthToIconWidth = parsed_SnapWidthToIconWidth;
                                                Debug.WriteLine($"SnapWidthToIconWidth added\t{temp.SnapWidthToIconWidth}");
                                                break;
                                            case "SnapWidthToIconWidth_PlusScrollbarWidth":
                                                if (bool.TryParse(value.ToString(), out bool parsed_SnapWidthToIconWidth_PlusScrollbarWidth)) temp.SnapWidthToIconWidth_PlusScrollbarWidth = parsed_SnapWidthToIconWidth_PlusScrollbarWidth;
                                                Debug.WriteLine($"SnapWidthToIconWidth_PlusScrollbarWidth added\t{temp.SnapWidthToIconWidth_PlusScrollbarWidth}");
                                                break;
                                            case "CheckFolderSize":
                                                if (bool.TryParse(value.ToString(), out bool parsed_CheckFolderSize)) temp.CheckFolderSize = parsed_CheckFolderSize;
                                                Debug.WriteLine($"CheckFolderSize added\t{temp.CheckFolderSize}");
                                                break;
                                            case "LinkOnDrop":
                                                if (bool.TryParse(value.ToString(), out bool parsed_LinkOnDrop)) temp.LinkOnDrop = parsed_LinkOnDrop;
                                                break;
                                            // Legacy: read old MoveFilesOnDrop (inverted semantic).
                                            // If user explicitly enabled it, they wanted move — that's
                                            // the new default, so we just drop the legacy value.
                                            case "MoveFilesOnDrop":
                                                break;
                                            case "SnapToGrid":
                                                if (bool.TryParse(value.ToString(), out bool parsed_SnapToGrid)) temp.SnapToGrid = parsed_SnapToGrid;
                                                break;
                                            case "GridSize":
                                                if (int.TryParse(value.ToString(), out int parsedGrid))
                                                    temp.GridSize = parsedGrid;
                                                break;
                                            case "AutoRouteRegex":
                                                temp.AutoRouteRegex = value.ToString() ?? "";
                                                break;
                                            case "BackgroundImagePath":
                                                temp.BackgroundImagePath = value.ToString() ?? "";
                                                break;
                                            case "PinToTop":
                                                if (bool.TryParse(value.ToString(), out bool parsed_PinToTop)) temp.PinToTop = parsed_PinToTop;
                                                break;
                                            case "DropShadowEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_DropShadowEnabled)) temp.DropShadowEnabled = parsed_DropShadowEnabled;
                                                break;
                                            case "GradientBackgroundEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_GradientBackgroundEnabled)) temp.GradientBackgroundEnabled = parsed_GradientBackgroundEnabled;
                                                break;
                                            case "DisableAnimations":
                                                if (bool.TryParse(value.ToString(), out bool parsed_DisableAnimations)) temp.DisableAnimations = parsed_DisableAnimations;
                                                break;
                                            case "IsDesktopFilterRack":
                                                if (bool.TryParse(value.ToString(), out bool parsed_IsDesktopFilterRack)) temp.IsDesktopFilterRack = parsed_IsDesktopFilterRack;
                                                break;
                                            case "AssignedFiles":
                                                var files = value.ToString()?.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                                                if (files != null)
                                                {
                                                    temp.AssignedFiles = new List<string>(files);
                                                }
                                                break;
                                            case "TitleBarColor":
                                                temp.TitleBarColor = value.ToString()!;
                                                Debug.WriteLine($"TitleBarColor added\t{temp.TitleBarColor}");
                                                break;
                                            case "TitleTextColor":
                                                temp.TitleTextColor = value.ToString()!;
                                                Debug.WriteLine($"TitleTextColor added\t{temp.TitleTextColor}");
                                                break;
                                            case "TitleText":
                                                temp.TitleText = value.ToString();
                                                Debug.WriteLine($"TitleText added\t{temp.TitleText}");
                                                break;
                                            case "TitleTextAlignment":
                                                if (Enum.TryParse<System.Windows.HorizontalAlignment>(value.ToString()!, out var alignment))
                                                {
                                                    temp.TitleTextAlignment = alignment;
                                                }
                                                break;
                                            case "BorderColor":
                                                temp.BorderColor = value.ToString()!;
                                                Debug.WriteLine($"BorderColor added\t{temp.BorderColor}");
                                                break;
                                            case "BorderEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_BorderEnabled)) temp.BorderEnabled = parsed_BorderEnabled;
                                                Debug.WriteLine($"BorderEnabled added\t{temp.BorderEnabled}");
                                                break;
                                            case "ActiveBorderEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ActiveBorderEnabled)) temp.ActiveBorderEnabled = parsed_ActiveBorderEnabled;
                                                Debug.WriteLine($"ActiveBorderEnabled added\t{temp.ActiveBorderEnabled}");
                                                break;
                                            case "ActiveBackgroundEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ActiveBackgroundEnabled)) temp.ActiveBackgroundEnabled = parsed_ActiveBackgroundEnabled;
                                                Debug.WriteLine($"ActiveBackgroundEnabled added\t{temp.ActiveBackgroundEnabled}");
                                                break;
                                            case "ActiveTitleTextEnabled":
                                                if (bool.TryParse(value.ToString(), out bool parsed_ActiveTitleTextEnabled)) temp.ActiveTitleTextEnabled = parsed_ActiveTitleTextEnabled;
                                                Debug.WriteLine($"ActiveTitleTextEnabled added\t{temp.ActiveTitleTextEnabled}");
                                                break;
                                            case "FileFilterRegex":
                                                temp.FileFilterRegex = value.ToString()!;
                                                Debug.WriteLine($"FileFilterRegex added\t{temp.FileFilterRegex}");
                                                break;
                                            case "FileFilterHideRegex":
                                                temp.FileFilterHideRegex = value.ToString()!;
                                                Debug.WriteLine($"FileFilterHideRegex added\t{temp.FileFilterHideRegex}");
                                                break;
                                            case "ListViewBackgroundColor":
                                                temp.ListViewBackgroundColor = value.ToString()!;
                                                Debug.WriteLine($"ListViewBackgroundColor added\t{temp.ListViewBackgroundColor}");
                                                break;
                                            case "ActiveBackgroundColor":
                                                temp.ActiveBackgroundColor = value.ToString()!;
                                                Debug.WriteLine($"ActiveBackgroundColor added\t{temp.ActiveBackgroundColor}");
                                                break;
                                            case "ActiveBorderColor":
                                                temp.ActiveBorderColor = value.ToString()!;
                                                Debug.WriteLine($"ActiveBorderColor added\t{temp.ActiveBorderColor}");
                                                break;
                                            case "ActiveTitleTextColor":
                                                temp.ActiveTitleTextColor = value.ToString()!;
                                                Debug.WriteLine($"ActiveTitleTextColor added\t{temp.ActiveTitleTextColor}");
                                                break;
                                            case "ListViewFontColor":
                                                temp.ListViewFontColor = value.ToString()!;
                                                Debug.WriteLine($"ListViewFontColor added\t{temp.ListViewFontColor}");
                                                break;
                                            case "ListViewFontShadowColor":
                                                temp.ListViewFontShadowColor = value.ToString()!;
                                                Debug.WriteLine($"ListViewFontShadowColor added\t{temp.ListViewFontShadowColor}");
                                                break;
                                            case "Opacity":
                                                if (int.TryParse(value.ToString(), out int parsedOpacity))
                                                {
                                                    temp.Opacity = parsedOpacity;
                                                    Debug.WriteLine($"Opacity added\t{temp.Opacity}");
                                                }
                                                break;
                                            case "SortBy":
                                                if (Int32.TryParse(value.ToString(), out int parsedSortBy))
                                                {
                                                    temp.SortBy = parsedSortBy;
                                                }
                                                else
                                                {
                                                    temp.SortBy = 1;
                                                }
                                                break;
                                            case "FolderOrder":
                                                if (Int32.TryParse(value.ToString(), out int parsedFolderOrder))
                                                {
                                                    temp.FolderOrder = parsedFolderOrder;
                                                }
                                                break;
                                            case "ShowOnVirtualDesktops":
                                                if (value is string stringShowOnVirtualDesktops)
                                                {
                                                    try
                                                    {
                                                        int[] parsedArray = stringShowOnVirtualDesktops
                                                            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                                            .Select(s => int.Parse(s))
                                                            .ToArray();

                                                        temp.ShowOnVirtualDesktops = parsedArray;
                                                        Debug.WriteLine($"ShowOnVirtualDesktops added\t{temp.ShowOnVirtualDesktops}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Debug.WriteLine($"ShowOnVirtualDesktops failed to parse:\t{ex.Message}");
                                                    }
                                                }
                                                break; ;
                                            case "LastAccessedFiles":
                                                if (value is string[] strArray)
                                                {
                                                    temp.LastAccessedFiles = new List<string>(strArray);
                                                }
                                                break;
                                            case "CustomOrderFiles":
                                                if (value is string[] values)
                                                {
                                                    temp.CustomOrderFiles = values
                                                        .Select(s =>
                                                        {
                                                            var parts = s.Split(',');
                                                            if (parts.Length < 2) return null;
                                                            return Tuple.Create(parts[0], parts[1]);
                                                        }).Where(t => t != null).ToList()!;
                                                }
                                                break;

                                            case "TitleFontSize":
                                                if (double.TryParse(value.ToString(), out double parsedFontSize))
                                                {
                                                    temp.TitleFontSize = parsedFontSize;
                                                    Debug.WriteLine($"TitleFontSize loaded: {temp.TitleFontSize}");
                                                }
                                                break;
                                            default:
                                                Debug.WriteLine($"Unknown value: {valueName}");
                                                break;
                                        }
                                    }

                                }
                                // Auto-detect Desktop Filter Racks: if the rack's
                                // Folder points at the user's Desktop, mark it as a
                                // Desktop Filter Rack even if the registry flag was
                                // never saved (e.g. racks created before this feature).
                                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                                if (!temp.IsDesktopFilterRack
                                    && !string.IsNullOrEmpty(temp.Folder)
                                    && string.Equals(Path.GetFullPath(temp.Folder), Path.GetFullPath(desktopPath), StringComparison.OrdinalIgnoreCase))
                                {
                                    temp.IsDesktopFilterRack = true;
                                    Debug.WriteLine($"Auto-detected Desktop Filter Rack: {temp.Name}");
                                }
                                // Revert virtual racks that picked up the bad
                                // LinkOnDrop=true default from a prior build. Runs once,
                                // gated by LinkOnDropRevertedKey. Property setter
                                // persists the change to the registry.
                                //
                                // Identify virtual racks by folder path, not by
                                // temp.IsShortcutsOnly — that field isn't persisted to
                                // registry (only set in code when a virtual rack is
                                // created; reconstructed by RackWindow's constructor
                                // from the folder path AFTER this loop runs). Checking
                                // IsShortcutsOnly here would always be false.
                                if (needsRevertMigration && temp.LinkOnDrop
                                    && IsInsideVirtualFramesRoot(temp.Folder))
                                {
                                    temp.LinkOnDrop = false;
                                }

                                // Flip the inside-rack-folder-navigation default to
                                // OFF for every rack on first launch under this build.
                                // Users who actually want inside-rack nav can re-enable
                                // it per-rack via Settings or the title-bar menu.
                                if (needsFolderOpenMigration && temp.FolderOpenInsideFrame)
                                {
                                    temp.FolderOpenInsideFrame = false;
                                }

                                if (temp.Name != "empty")
                                {
                                    if (Path.Exists(temp.Folder))
                                    {
                                        Instances.Add(temp);
                                    }
                                    else
                                    {
                                        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(temp.GetKeyLocation(), true);
                                        if (key != null)
                                        {
                                            temp.IsFolderMissing = true;
                                            Instances.Add(temp);
                                        }
                                    }
                                }

                            }
                            else
                            {
                                Debug.WriteLine("instance not valid");
                            }
                        }
                    }
                }
                // No Instances key yet: this is a first run (fresh install). Start with a
                // clean desktop - no rack is created or shown. The user opens one from the
                // tray menu ("New rack") whenever they want. (Previously a starter rack was
                // auto-created and popped onto the screen on install.)
            }
            Debug.WriteLine("Showing windows...");
            foreach (var Instance in Instances)
            {
                var subWindow = new RackWindow(Instance);
                _subWindows.Add(subWindow);
                subWindow.ChangeBackgroundOpacity(Instance.Opacity);
                subWindow.Show();
                _subWindowsPtr.Add(new WindowInteropHelper(subWindow).Handle);
                InitDetails();
            }
            foreach (var window in _subWindows)
            {
                // Position each rack against the screen edge on startup. Neighbor docking
                // (WonRight/WonLeft) was removed in favor of drag-time push physics.
                window.HandleWindowMove(true);
            }
            // No auto-created starter rack on first launch: the user should see a clean
            // desktop after install and create a rack from the tray menu when they want one.
            // Mark the LinkOnDrop revert migration as done so a later user-driven
            // LinkOnDrop=true toggle isn't undone next launch.
            if (needsRevertMigration)
            {
                try { reg.WriteToRegistryRoot(LinkOnDropRevertedKey, true); }
                catch (Exception ex) { Debug.WriteLine($"Marking LinkOnDrop revert failed: {ex.Message}"); }
            }
            if (needsFolderOpenMigration)
            {
                try { reg.WriteToRegistryRoot(FolderOpenInsideFrameDefaultFlippedKey, true); }
                catch (Exception ex) { Debug.WriteLine($"Marking FolderOpen flip failed: {ex.Message}"); }
            }
            Debug.WriteLine("Showing windows DONE");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR reading key: {ex.Message}");
        }
        isInitializingInstances = false;
        RefreshMirror();
    }
}
