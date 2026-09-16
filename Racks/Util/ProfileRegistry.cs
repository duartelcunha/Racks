using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Core;

namespace Racks.Util;

// Keep the existing registry contract in production. Isolated profiles use a
// versioned JSON document, including original registry value types and arrays.
public static class ProfileRegistry
{
    public static ProfileRegistryKey CurrentUser => NativeProfile.IsIsolated ? new("") : new(Registry.CurrentUser, false);
    public static ProfileRegistryKey ClassesRoot => new(Registry.ClassesRoot, false);
}

public sealed class ProfileRegistryKey : IDisposable
{
    public sealed class StoredValue
    {
        public RegistryValueKind Kind { get; set; }
        public JsonElement Value { get; set; }
    }
    public sealed class Document
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Dictionary<string, StoredValue>> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly object Gate = new();
    private static Document? document;
    private static string? loadedPath;
    private readonly RegistryKey? native;
    private readonly bool ownsNative;
    private readonly string path = "";
    internal ProfileRegistryKey(RegistryKey key, bool owns = true) { native = key; ownsNative = owns; }
    internal ProfileRegistryKey(string keyPath) { path = keyPath; }
    public string Name => native?.Name ?? @"HKEY_CURRENT_USER\" + path;
    private static Document Data
    {
        get
        {
            var file = Path.Combine(NativeProfile.Root!, "native-settings.json");
            if (loadedPath != file)
            {
                var next = File.Exists(file) ? JsonStore.Read<Document>(file) : new();
                if (next.Version != 1 || next.Keys == null) throw new InvalidDataException("Unsupported native profile settings.");
                next.Keys = next.Keys.ToDictionary(x => x.Key, x => new Dictionary<string, StoredValue>(x.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                document = next; loadedPath = file;
            }
            return document!;
        }
    }
    private static void Save() => JsonStore.Write(loadedPath!, document!);
    private string Child(string child) => (path.Length == 0 ? child : path + "\\" + child).Trim('\\');
    private static void CheckWritable(string key)
    {
        if (!key.Equals(@"SOFTWARE\Racks", StringComparison.OrdinalIgnoreCase) && !key.StartsWith(@"SOFTWARE\Racks\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An isolated profile cannot change Windows settings.");
    }
    public ProfileRegistryKey CreateSubKey(string subkey)
    {
        if (native != null) return new(native.CreateSubKey(subkey));
        var child = Child(subkey); CheckWritable(child);
        lock (Gate)
        {
            var parts = child.Split('\\');
            for (int i = 1; i <= parts.Length; i++) Data.Keys.TryAdd(string.Join('\\', parts.Take(i)), new(StringComparer.OrdinalIgnoreCase));
            Save(); return new(child);
        }
    }
    public ProfileRegistryKey? OpenSubKey(string name, bool writable = false)
    {
        if (native != null) { var key = native.OpenSubKey(name, writable); return key == null ? null : new(key); }
        var child = Child(name);
        if (writable) CheckWritable(child);
        lock (Gate) return Data.Keys.ContainsKey(child) ? new(child) : null;
    }
    public string[] GetSubKeyNames()
    {
        if (native != null) return native.GetSubKeyNames();
        lock (Gate) return Data.Keys.Keys.Where(x => x.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[(path.Length + 1)..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string[] GetValueNames() { if (native != null) return native.GetValueNames(); lock (Gate) return Data.Keys[path].Keys.ToArray(); }
    public RegistryValueKind GetValueKind(string name) { if (native != null) return native.GetValueKind(name); lock (Gate) return Data.Keys[path][name].Kind; }
    public object? GetValue(string name, object? defaultValue = null)
    {
        if (native != null) return native.GetValue(name, defaultValue);
        lock (Gate)
        {
            if (!Data.Keys[path].TryGetValue(name, out var value)) return defaultValue;
            return value.Kind switch
            {
                RegistryValueKind.MultiString => value.Value.Deserialize<string[]>(),
                RegistryValueKind.Binary => value.Value.Deserialize<byte[]>(),
                RegistryValueKind.DWord => value.Value.GetInt32(),
                RegistryValueKind.QWord => value.Value.GetInt64(),
                _ => value.Value.GetString()
            };
        }
    }
    public void SetValue(string name, object value) => SetValue(name, value, value switch { int => RegistryValueKind.DWord, long => RegistryValueKind.QWord, byte[] => RegistryValueKind.Binary, string[] => RegistryValueKind.MultiString, _ => RegistryValueKind.String });
    public void SetValue(string name, object value, RegistryValueKind valueKind)
    {
        if (native != null) { native.SetValue(name, value, valueKind); return; }
        CheckWritable(path);
        lock (Gate)
        {
            if (valueKind is RegistryValueKind.String or RegistryValueKind.ExpandString) value = Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? "";
            Data.Keys[path][name] = new() { Kind = valueKind, Value = JsonSerializer.SerializeToElement(value) }; Save();
        }
    }
    public void DeleteValue(string name, bool throwOnMissingValue = true)
    {
        if (native != null) { native.DeleteValue(name, throwOnMissingValue); return; }
        CheckWritable(path);
        lock (Gate) { if (!Data.Keys[path].Remove(name) && throwOnMissingValue) throw new ArgumentException("Missing value."); Save(); }
    }
    public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = true)
    {
        if (native != null) { native.DeleteSubKeyTree(subkey, throwOnMissingSubKey); return; }
        var child = Child(subkey); CheckWritable(child);
        lock (Gate)
        {
            if (!Data.Keys.ContainsKey(child) && throwOnMissingSubKey) throw new ArgumentException("Missing key.");
            foreach (var key in Data.Keys.Keys.Where(x => x.Equals(child, StringComparison.OrdinalIgnoreCase) || x.StartsWith(child + "\\", StringComparison.OrdinalIgnoreCase)).ToArray()) Data.Keys.Remove(key);
            Save();
        }
    }
    public void DeleteSubKey(string subkey, bool throwOnMissingSubKey = true)
    {
        if (native != null) { native.DeleteSubKey(subkey, throwOnMissingSubKey); return; }
        using var key = OpenSubKey(subkey);
        if (key?.GetSubKeyNames().Length > 0) throw new InvalidOperationException("Key has children.");
        DeleteSubKeyTree(subkey, throwOnMissingSubKey);
    }
    public void Close() => Dispose();
    public void Dispose() { if (ownsNative) native?.Dispose(); }
}
