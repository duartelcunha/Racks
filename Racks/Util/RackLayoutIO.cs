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

        public static int Import(string json, bool replaceExisting)
        {
            var layout = JsonSerializer.Deserialize<LayoutFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (layout?.Racks == null || layout.Racks.Count == 0) return 0;

            string root = $@"SOFTWARE\{InstanceController.appName}\Instances";
            if (replaceExisting)
            {
                Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
            }

            int written = 0;
            foreach (var rack in layout.Racks)
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
