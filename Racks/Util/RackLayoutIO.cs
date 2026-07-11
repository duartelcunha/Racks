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
            if (layout?.Racks == null || layout.Racks.Count == 0) return 0;

            // Validate every rack name BEFORE touching existing state. A rack name becomes a
            // registry subkey, so it must be a single safe segment: no separators (which would
            // write keys outside Instances\<name>) and no traversal. Reject the whole import if
            // any is bad - a partially-valid file must not wipe the user's racks.
            foreach (var rack in layout.Racks)
            {
                if (!IsValidRackName(rack.Name))
                    throw new InvalidDataException($"Layout contains an unsafe rack name: '{rack.Name}'.");
                foreach (var key in _pathValueNames)
                {
                    if (rack.Values.TryGetValue(key, out var raw))
                    {
                        string? p = ValueAsString(raw);
                        if (!IsSafeImportedPath(p))
                            throw new InvalidDataException($"Layout rack '{rack.Name}' has an unsafe {key}: '{p}'.");
                    }
                }
            }

            string root = $@"SOFTWARE\{InstanceController.appName}\Instances";

            // Snapshot current racks so a failure after the delete can be rolled back exactly.
            string backup = replaceExisting ? Export() : "";

            try
            {
                if (replaceExisting)
                    Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);

                return WriteRacks(root, layout.Racks);
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

        // A rack name is used verbatim as a registry subkey: it must be one safe segment.
        private static bool IsValidRackName(string? name)
            => !string.IsNullOrWhiteSpace(name)
               && name.IndexOf('\\') < 0
               && name.IndexOf('/') < 0
               && name != "." && name != ".."
               && name.Length <= 255;

        // Value names that hold a filesystem path and so are validated on import.
        private static readonly string[] _pathValueNames = { "Folder", "BackgroundImagePath" };

        private static string? ValueAsString(object? raw)
            => raw is JsonElement je
                ? (je.ValueKind == JsonValueKind.String ? je.GetString() : null)
                : raw as string;

        // An imported path must be empty, a known sentinel, or an absolute rooted path with no
        // ".." traversal segment - so a hostile layout can't point a rack at a relative/traversal
        // location the app then reads or mirrors. UNC/network paths are allowed (legitimate).
        private static bool IsSafeImportedPath(string? value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            if (value == "empty" || value == "Default Style") return true; // rack sentinels
            foreach (var seg in value.Split('\\', '/'))
                if (seg == "..") return false;
            try { return Path.IsPathRooted(value); }
            catch { return false; }
        }

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
