using Racks;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;

public class InstanceController
{
    // Product name; also the HKCU\SOFTWARE registry root.
    public static string appName = "Racks";
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

    public static bool IsInsideVirtualFramesRoot(string path) => IsInsideRoot(path, VirtualFramesRoot);

    internal static bool IsInsideRoot(string path, string rootPath)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
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

    // Persist `instance` under its current name and drop the key it used to live under.
    // Used when a rack is renamed (the registry key IS the rack name).
    // Delegates to WriteInstanceToKey so there is exactly one list of persisted values; this method
    // used to keep its own copy that had drifted (it skipped DropShadowEnabled,
    // GradientBackgroundEnabled, DisableAnimations, IsDesktopFilterRack and AssignedFiles).
    public void WriteOverInstanceToKey(Instance instance, string oldKey)
    {
        string? newName = instance.Name;
        bool isEmptyPlaceholder = newName == "empty";

        // An un-initialized "empty" rack is never persisted (InitInstances skips it too).
        if (!isEmptyPlaceholder) WriteInstanceToKey(instance);

        // Only delete the old key when the name really changed. Deleting it when the name is
        // unchanged removed the key that was just written, so the rack vanished on restart.
        if (isEmptyPlaceholder || !string.Equals(oldKey, newName, StringComparison.OrdinalIgnoreCase))
        {
            // Never throws; a failed or refused delete is audited by the store.
            Instance.Store.Delete(oldKey);
        }
    }
    private void InitDetails()
    {
        if (Racks.Infrastructure.Registry.AppSettings.Default.BlurBackground is bool blur)
        {
            ChangeBlur(blur);
        }
    }
    public void WriteInstanceToKey(Instance instance)
    {
        if (string.IsNullOrEmpty(instance.Name))
        {
            Debug.WriteLine("instance.Name is null, Instance is not written to key");
            return;
        }
        // One list of persisted values (RackSettingsSchema) and one writer; a failure is audited, never thrown.
        Instance.Store.Save(instance);
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

        WriteInstanceToKey(inst);
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
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            _subWindowsPtr.Remove(handle);
        }
        catch { }
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
    private const string LinkOnDropRevertedKey = Racks.Infrastructure.Registry.MigrationMarkers.VirtualRackLinkOnDropRevertedToMove;

    // One-shot: flip FolderOpenInsideFrame to false for every rack. The
    // upstream default was inside-rack navigation; users find the OS-native
    // "double-click opens Explorer" behavior more intuitive. Marker prevents
    // overriding a user-driven toggle on subsequent launches.
    private const string FolderOpenInsideFrameDefaultFlippedKey = Racks.Infrastructure.Registry.MigrationMarkers.FolderOpenInsideFrameDefaultedToExplorer;

    public void InitInstances()
    {
        isInitializingInstances = true;
        Debug.WriteLine("Init...");
        var markers = Racks.Infrastructure.Registry.MigrationMarkers.Default;
        bool needsRevertMigration = !markers.IsSet(LinkOnDropRevertedKey);
        bool needsFolderOpenMigration = !markers.IsSet(FolderOpenInsideFrameDefaultFlippedKey);
        try
        {
            LoadInstancesFromRegistry(needsRevertMigration, needsFolderOpenMigration);
            Debug.WriteLine("Showing windows...");
            foreach (var Instance in Instances)
            {
                var subWindow = new RackWindow(Instance);
                _subWindows.Add(subWindow);
                subWindow.ChangeBackgroundOpacity(Instance.Opacity);
                subWindow.Show();
                _subWindowsPtr.Add(new WindowInteropHelper(subWindow).Handle);
            }
            InitDetails();
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
                markers.Set(LinkOnDropRevertedKey);   // a failed write is audited by MigrationMarkers
            }
            if (needsFolderOpenMigration)
            {
                markers.Set(FolderOpenInsideFrameDefaultFlippedKey);
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

    // The load half of InitInstances: reads the racks through the settings repository (validated, one try per rack, no
    // writes while reading), then runs the same post pass as before. Separate from InitInstances so a test can run the
    // real reader without creating windows.
    internal void LoadInstancesFromRegistry(bool needsRevertMigration, bool needsFolderOpenMigration)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var loaded = Instance.Store.LoadAll();
        foreach (var rack in loaded.Racks)
        {
            try
            {
                // "empty" is the placeholder of a rack that was never configured; it is never loaded (its key is left alone).
                if (rack.Settings.Name == "empty") continue;
                Instance temp = Racks.Infrastructure.Registry.RackSettingsMapper.ToInstance(rack.Settings);

                // Benign findings only (a clamped number, no Name value): save the rack once in normal form, audited.
                if (rack.NeedsNormalization) Instance.Store.Normalize(temp);

                // A hand-edited rack whose Name differs from its key (beyond case) has no key under its Name. A write
                // there would create a skeleton key without a Name, which loads as a "folder missing" ghost next time, so
                // the one-shot migrations below change such a rack in memory only (it is audited as a Name mismatch).
                bool nameMatchesKey = string.Equals(rack.KeyName, temp.Name, StringComparison.OrdinalIgnoreCase);
                using var quiet = nameMatchesKey ? null : temp.SuspendPersistence();

                // Auto-detect Desktop Filter Racks: if the rack's
                // Folder points at the user's Desktop, mark it as a
                // Desktop Filter Rack even if the registry flag was
                // never saved (e.g. racks created before this feature).
                if (!temp.IsDesktopFilterRack && PointsAt(temp.Folder, desktopPath))
                {
                    temp.IsDesktopFilterRack = true;
                    Debug.WriteLine($"Auto-detected Desktop Filter Rack: {temp.Name}");
                }
                // Revert virtual racks that picked up the bad
                // LinkOnDrop=true default from a prior build. Runs once,
                // gated by LinkOnDropRevertedKey. The property setter
                // persists the change to the registry (one of the two
                // writes a load still makes, on purpose).
                //
                // Identify virtual racks by folder path, not by
                // temp.IsShortcutsOnly: that field isn't persisted to
                // registry (only set in code when a virtual rack is
                // created; reconstructed by RackWindow's constructor
                // from the folder path AFTER this loop runs).
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

                // A rack whose folder is gone is kept and flagged, so the user can point it somewhere else.
                if (!Path.Exists(temp.Folder)) temp.IsFolderMissing = true;
                Instances.Add(temp);
            }
            catch (Exception ex)
            {
                // One rack failing here never stops the others. Its registry data is untouched.
                Debug.WriteLine($"Could not load rack '{rack.KeyName}': {ex.Message}");
                Racks.Infrastructure.Logging.AuditLog.Current.Write(new Racks.Infrastructure.Logging.AuditEvent(
                    Guid.NewGuid().ToString("N"), DateTime.UtcNow, "SettingFallback",
                    $@"HKCU\SOFTWARE\{appName}\Instances\{rack.KeyName}", null,
                    $"load failed: {ex.GetType().Name}: {ex.Message}", "Failed",
                    $"rack={rack.KeyName}; field=(rack); raw=; fallback=rack not loaded; its registry data is left untouched"));
            }
        }
    }

    private static bool PointsAt(string? folder, string target)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        try { return string.Equals(Path.GetFullPath(folder), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
