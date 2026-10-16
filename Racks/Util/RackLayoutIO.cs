using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace Racks.Util
{
    // Plain-text portable backup/restore of the user's rack layout.
    // Mirrors the fields persisted to HKCU\SOFTWARE\Racks\Instances\* but lives as a
    // single JSON file so layouts can be backed up, version-controlled, or migrated
    // to another machine without touching the registry directly.
    public static class RackLayoutIO
    {
        public const int CurrentVersion = 1;

        public sealed class RackDto
        {
            public string Name { get; set; } = "";
            public Dictionary<string, object?> Values { get; set; } = new();
            public Dictionary<string, string[]> MultiStringValues { get; set; } = new();
        }

        public sealed class LayoutFile
        {
            public int Version { get; set; } = CurrentVersion;
            public string ExportedBy { get; set; } = "Racks";
            public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
            public List<RackDto> Racks { get; set; } = new();
        }

        public static string Export()
        {
            var layout = new LayoutFile();
            string root = $@"SOFTWARE\{InstanceController.appName}\Instances";
            using var instancesKey = Registry.CurrentUser.OpenSubKey(root);
            if (instancesKey == null) return Serialize(layout);

            foreach (var rackName in instancesKey.GetSubKeyNames())
            {
                using var rk = instancesKey.OpenSubKey(rackName);
                if (rk == null) continue;
                var dto = new RackDto { Name = rackName };
                foreach (var v in rk.GetValueNames())
                {
                    var kind = rk.GetValueKind(v);
                    if (kind == RegistryValueKind.MultiString)
                        dto.MultiStringValues[v] = (string[])rk.GetValue(v)!;
                    else
                        dto.Values[v] = rk.GetValue(v);
                }
                layout.Racks.Add(dto);
            }
            return Serialize(layout);
        }

        // Restore a layout from JSON. Atomic-ish: nothing existing is touched until the file is
        // parsed AND every rack name is validated, and if a mid-write failure occurs after the
        // destructive delete, the previous racks are rolled back from a snapshot. A malformed or
        // hostile file therefore can never leave the user with no racks. Throws InvalidDataException
        // on bad input; the caller shows the reason.
        public static int Import(string json, bool replaceExisting)
        {
            // Everything is parsed, validated and normalized BEFORE existing state is touched.
            var racks = ParseAndValidate(json);
            if (racks.Count == 0) return 0;

            string root = $@"SOFTWARE\{InstanceController.appName}\Instances";

            // Snapshot current racks so a failure after the delete can be rolled back exactly.
            string backup = replaceExisting ? Export() : "";

            try
            {
                if (replaceExisting)
                    Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);

                return WriteRacks(root, racks);
            }
            catch
            {
                // Roll back to the snapshot so a failed import never leaves the user empty.
                if (replaceExisting)
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
                        var prev = JsonSerializer.Deserialize<LayoutFile>(backup,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (prev?.Racks != null) WriteRacks(root, prev.Racks);
                    }
                    catch { /* best-effort restore; original error still propagates */ }
                }
                throw;
            }
        }

        // Parse a layout file and return its racks validated and normalized, or throw
        // InvalidDataException. Pure (no registry access) so it can be tested directly.
        //
        // INV-IMPORT-1: only value names the app itself persists are kept, matched
        // case-insensitively (registry value names are case-insensitive, so "folder" would
        // otherwise slip past a case-sensitive check and still overwrite "Folder"), written back
        // under their canonical spelling, and type-checked. Two spellings of one name are
        // rejected. Unknown names are dropped: the app never reads them, and old backups may
        // carry legacy names. A partially-valid file is rejected whole, never half-applied.
        internal static List<RackDto> ParseAndValidate(string json)
        {
            LayoutFile? layout;
            try
            {
                layout = JsonSerializer.Deserialize<LayoutFile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                throw new InvalidDataException("The layout file is not valid JSON.");
            }
            var result = new List<RackDto>();
            if (layout?.Racks == null) return result;

            foreach (var rack in layout.Racks)
            {
                if (rack == null) throw new InvalidDataException("Layout contains an empty rack entry.");
                // A rack name becomes a registry subkey, so it must be a single safe segment.
                if (!IsValidRackName(rack.Name))
                    throw new InvalidDataException($"Layout contains an unsafe rack name: '{rack.Name}'.");

                var clean = new RackDto { Name = rack.Name };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in rack.Values ?? new Dictionary<string, object?>())
                {
                    if (!_allowedValues.TryGetValue(kv.Key ?? "", out var spec)) continue; // unknown: dropped
                    if (!seen.Add(spec.Name))
                        throw new InvalidDataException($"Layout rack '{rack.Name}' sets '{spec.Name}' more than once.");
                    if (spec.Kind == ValueKind.MultiString)
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has '{spec.Name}' in the wrong section.");

                    // JSON null is skipped, as before. Anything else must be a JSON scalar; paths
                    // and the rack name must be strings and pass their own checks.
                    if (kv.Value is null) continue;
                    if (kv.Value is not JsonElement e)
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has an invalid {spec.Name}.");
                    if (e.ValueKind == JsonValueKind.Null) continue;
                    bool ok = spec.Kind == ValueKind.Scalar
                        ? e.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                        : e.ValueKind == JsonValueKind.String;
                    if (!ok)
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has an invalid {spec.Name}.");
                    string? s = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    if ((spec.Kind == ValueKind.Path && !IsSafeImportedPath(s))
                        || (spec.Kind == ValueKind.RackName && !IsValidRackName(s)))
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has an unsafe {spec.Name}: '{s}'.");

                    clean.Values[spec.Name] = e;
                }

                foreach (var kv in rack.MultiStringValues ?? new Dictionary<string, string[]>())
                {
                    if (!_allowedValues.TryGetValue(kv.Key ?? "", out var spec)) continue; // unknown: dropped
                    if (spec.Kind != ValueKind.MultiString)
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has '{spec.Name}' in the wrong section.");
                    if (!seen.Add(spec.Name))
                        throw new InvalidDataException($"Layout rack '{rack.Name}' sets '{spec.Name}' more than once.");
                    if (kv.Value == null || kv.Value.Any(x => x == null))
                        throw new InvalidDataException($"Layout rack '{rack.Name}' has an invalid {spec.Name}.");
                    clean.MultiStringValues[spec.Name] = kv.Value;
                }

                result.Add(clean);
            }
            return result;
        }

        // A rack name is used verbatim as a registry subkey: it must be one safe segment.
        private static bool IsValidRackName(string? name)
            => !string.IsNullOrWhiteSpace(name)
               && name.IndexOf('\\') < 0
               && name.IndexOf('/') < 0
               && name != "." && name != ".."
               && name.Length <= 255;

        private enum ValueKind { Scalar, Path, RackName, MultiString }

        private readonly record struct ValueSpec(string Name, ValueKind Kind);

        // Every per-rack value the app reads back (InstanceController.InitInstances) and nothing
        // else. Keys are case-insensitive; ValueSpec.Name is the canonical spelling written.
        private static readonly Dictionary<string, ValueSpec> _allowedValues = BuildAllowList();

        private static Dictionary<string, ValueSpec> BuildAllowList()
        {
            var map = new Dictionary<string, ValueSpec>(StringComparer.OrdinalIgnoreCase);
            void Add(ValueKind kind, params string[] names)
            {
                foreach (var n in names) map.Add(n, new ValueSpec(n, kind));
            }
            Add(ValueKind.Path, "Folder", "BackgroundImagePath");
            Add(ValueKind.RackName, "Name");
            Add(ValueKind.MultiString, "LastAccessedFiles", "CustomOrderFiles");
            Add(ValueKind.Scalar,
                "PosX", "PosY", "Width", "Height", "IconSize", "IdleOpacity", "AnimationSpeed",
                "MaxGrayScaleStrength", "GrayScaleEnabled", "GrayScaleEnabled_InactiveOnly",
                "TitleFontFamily", "ItemFontFamily", "Minimized", "ShowHiddenFiles",
                "LastAccesedToFirstRow", "EnableCustomItemsOrder", "ShowFileExtension",
                "ShowFileExtensionIcon", "ShowHiddenFilesIcon", "ShowDisplayName", "IsLocked",
                "ShowInGrid", "AutoExpandonCursor", "ShowShortcutArrow", "FolderOpenInsideFrame",
                "HideTitleBarIconsWhenInactive", "SnapWidthToIconWidth",
                "SnapWidthToIconWidth_PlusScrollbarWidth", "CheckFolderSize", "LinkOnDrop",
                "MoveFilesOnDrop", "SnapToGrid", "GridSize", "AutoRouteRegex", "PinToTop",
                "IsTransparent", "DropShadowEnabled", "GradientBackgroundEnabled", "DisableAnimations",
                "IsDesktopFilterRack", "AssignedFiles", "TitleBarColor", "TitleTextColor", "TitleText",
                "TitleTextAlignment", "BorderColor", "BorderEnabled", "ActiveBorderEnabled",
                "ActiveBackgroundEnabled", "ActiveTitleTextEnabled", "FileFilterRegex",
                "FileFilterHideRegex", "ListViewBackgroundColor", "ActiveBackgroundColor",
                "ActiveBorderColor", "ActiveTitleTextColor", "ListViewFontColor",
                "ListViewFontShadowColor", "Opacity", "SortBy", "FolderOrder", "ShowOnVirtualDesktops",
                "TitleFontSize");
            return map;
        }

        // An imported path must be empty, a known sentinel, or a fully-qualified LOCAL drive path
        // ("C:\..." or "C:/...") with no ".." segment. Anything that starts with two separators
        // is refused: UNC ("\\host\share", "//host/share", "\\host@SSL\..."), and the DOS device
        // forms "\\?\", "\\.\", "\\?\UNC\". A hostile layout could otherwise point a rack (and,
        // through auto-routing, new Desktop files) at a remote share, and merely touching that
        // path leaks the user's NTLM hash. Users can still pick a network folder themselves in
        // the app; only an IMPORTED file is held to this rule (DesktopRouter does not re-check).
        internal static bool IsSafeImportedPath(string? value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            if (value == "empty" || value == "Default Style") return true; // rack sentinels
            if (value.Length < 3) return false;
            if (IsSeparator(value[0])) return false;                  // UNC, device paths, "\rooted"
            if (!char.IsAsciiLetter(value[0]) || value[1] != ':' || !IsSeparator(value[2]))
                return false;                                         // "C:x" (drive-relative), "relative"
            if (value.IndexOf(':', 2) >= 0) return false;             // alternate data streams
            foreach (char ch in value)
                if (ch < 0x20 || ch is '"' or '<' or '>' or '|' or '*' or '?') return false;
            foreach (var seg in value.Split('\\', '/'))
                if (seg == "..") return false;
            try { return Path.IsPathFullyQualified(value); }
            catch { return false; }
        }

        private static bool IsSeparator(char c) => c == '\\' || c == '/';

        private static int WriteRacks(string root, List<RackDto> racks)
        {
            int written = 0;
            foreach (var rack in racks)
            {
                if (string.IsNullOrEmpty(rack.Name)) continue;
                using var rk = Registry.CurrentUser.CreateSubKey($@"{root}\{rack.Name}");
                foreach (var kv in rack.Values)
                {
                    object? value = kv.Value;
                    // JSON deserializes numbers as JsonElement; unwrap to plain types.
                    if (value is JsonElement je) value = Unwrap(je);
                    if (value != null) rk.SetValue(kv.Key, value);
                }
                foreach (var kv in rack.MultiStringValues)
                {
                    rk.SetValue(kv.Key, kv.Value, RegistryValueKind.MultiString);
                }
                written++;
            }
            return written;
        }

        public static bool PromptExport()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"racks-layout-{DateTime.Now:yyyyMMdd-HHmm}.json",
                Filter = "Racks layout (*.json)|*.json|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return false;
            File.WriteAllText(dlg.FileName, Export());
            return true;
        }

        public static int PromptImport(bool replaceExisting)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Racks layout (*.json)|*.json|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return 0;
            return Import(File.ReadAllText(dlg.FileName), replaceExisting);
        }

        private static string Serialize(LayoutFile layout)
            => JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });

        private static object? Unwrap(JsonElement je) => je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt64(out long l)
                ? (object)l : (je.TryGetDouble(out double d) ? d : je.GetRawText()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => je.EnumerateArray().Select(Unwrap).ToArray(),
            _ => je.GetRawText(),
        };
    }
}
