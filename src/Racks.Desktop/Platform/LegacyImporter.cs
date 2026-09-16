using System.Globalization;
using Microsoft.Win32;
using Racks.Core;

namespace Racks.Desktop.Platform;

public static class LegacyImporter
{
    public static bool Import(AppPaths paths, AppSettings settings)
    {
        if (!OperatingSystem.IsWindows() || paths.IsIsolated || settings.LegacyImported) return false;
        using var root = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Racks\Instances")
            ?? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\DeskFrame\Instances");
        settings.LegacyImported = true;
        if (root == null) return false;
        var snapshot = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key == null) continue;
            snapshot[name] = key.GetValueNames().ToDictionary(x => x, key.GetValue);
        }
        JsonStore.Write(Path.Combine(paths.Data, "legacy-registry-backup.json"), snapshot);
        ImportSnapshot(paths, settings, snapshot);
        return settings.Racks.Count > 0;
    }

    public static void ImportSnapshot(AppPaths paths, AppSettings settings, Dictionary<string, Dictionary<string, object?>> snapshot)
    {
        foreach (var (name, values) in snapshot)
        {
            string S(string k, string fallback = "") => values.TryGetValue(k, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback : fallback;
            bool B(string k) => bool.TryParse(S(k), out var v) && v;
            double D(string k, double fallback) => (double.TryParse(S(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
                double.TryParse(S(k), NumberStyles.Float, CultureInfo.CurrentCulture, out v)) && double.IsFinite(v) ? v : fallback;
            var folder = S("Folder");
            if (name is "empty" or "Default Style" || string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) continue;
            var filtered = B("IsDesktopFilterRack") || Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).Equals(paths.Desktop.TrimEnd(Path.DirectorySeparatorChar), SafeFiles.PathComparison);
            var owned = filtered || SafeFiles.IsWithin(folder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Racks", "VirtualFrames"));
            var rack = new RackDefinition
            {
                Title = S("TitleText", name), Kind = owned ? RackKind.Owned : RackKind.Folder,
                Folder = filtered ? paths.Workspace : folder,
                IncludedNames = filtered ? S("AssignedFiles").Split('|', StringSplitOptions.RemoveEmptyEntries).Where(SafeFiles.IsLeafName).ToList() : null,
                X = D("PosX", 80), Y = D("PosY", 80), Width = Math.Clamp(D("Width", 320), 240, 10000), Height = Math.Clamp(D("Height", 360), 120, 10000),
                Locked = B("IsLocked"), Collapsed = B("Minimized"), ListView = !B("ShowInGrid"), Snap = B("SnapToGrid"),
                Accent = S("TitleBarColor", "#58C4AD"), Background = S("ListViewBackgroundColor", "#18252B"), Foreground = S("ListViewFontColor", "#F1F5F5"),
                FontFamily = S("ItemFontFamily", "Segoe UI"), FontSize = Math.Clamp(D("ItemFontSize", 13), 8, 48),
                IconSize = (int)Math.Clamp(D("IconSize", 32), 16, 128), Opacity = Math.Clamp(D("Opacity", 94) / 100, .2, 1),
                BackgroundImage = S("BackgroundImagePath"), LegacyAppearance = values.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? "")
            };
            if (string.IsNullOrWhiteSpace(rack.Title)) rack.Title = name;
            settings.Racks.Add(rack);
            if (!string.IsNullOrWhiteSpace(S("AutoRouteRegex"))) settings.Rules.Add(new RoutingRule
            {
                Name = rack.Title, Source = paths.Desktop, DestinationRackId = rack.Id, Regex = S("AutoRouteRegex"), Enabled = false,
                LastError = "Imported paused. Preview this rule before enabling it."
            });
        }
    }
}
