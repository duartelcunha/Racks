using System.Globalization;
using System.IO;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IRackRegistry"/> (keys not case sensitive, like the registry) that keeps every kind and can be told
/// to fail on purpose: <see cref="FailWhen"/> is asked before each call with the operation ("list", "read", "apply", "delete"),
/// the key and the 1-based number of that operation so far; returning true throws an <see cref="IOException"/>. Nothing here
/// touches the real registry.
/// </summary>
public sealed class ScriptedRackRegistry : IRackRegistry
{
    private readonly Dictionary<string, Dictionary<string, RegistryValue>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new();

    public Func<string, string, int, bool>? FailWhen { get; set; }

    /// <summary>When set, a write of this value name is accepted but silently not stored (a registry that lies).</summary>
    public string? DropValueOnApply { get; set; }

    public string Path => @"HKCU\SOFTWARE\RacksTests-scripted\Instances";

    /// <summary>How many times each operation was called.</summary>
    public int Calls(string operation) => _counts.TryGetValue(operation, out int n) ? n : 0;

    public int TotalCalls => _counts.Values.Sum();

    private void Enter(string operation, string key)
    {
        int n = Calls(operation) + 1;
        _counts[operation] = n;
        if (FailWhen != null && FailWhen(operation, key, n)) throw new IOException($"scripted {operation} failure for '{key}' (call {n})");
    }

    /// <summary>Creates an empty key without counting as a call (test setup).</summary>
    public void SeedKey(string key)
    {
        if (!_keys.ContainsKey(key)) _keys[key] = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Puts a value in without counting as a call or failing (test setup).</summary>
    public void Seed(string key, string name, object data, RegistryValueKind kind)
    {
        if (!_keys.TryGetValue(key, out var values)) _keys[key] = values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
        values[name] = new RegistryValue(Copy(data), kind);
    }

    public IReadOnlyList<string> GetRackKeyNames() { Enter("list", ""); return _keys.Keys.ToList(); }

    public IReadOnlyDictionary<string, RegistryValue> ReadValues(string keyName)
    {
        Enter("read", keyName);
        return _keys.TryGetValue(keyName, out var values)
            ? values.ToDictionary(v => v.Key, v => new RegistryValue(Copy(v.Value.Data), v.Value.Kind), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
    }

    public void Apply(string keyName, IReadOnlyList<RegistryWrite> writes)
    {
        Enter("apply", keyName);
        if (!_keys.TryGetValue(keyName, out var values)) _keys[keyName] = values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var write in writes)
        {
            if (write.IsRemoval) values.Remove(write.Name);
            else if (!string.Equals(write.Name, DropValueOnApply, StringComparison.OrdinalIgnoreCase))
                values[write.Name] = new RegistryValue(Copy(write.Data!), write.Kind);
        }
    }

    public bool DeleteKey(string keyName)
    {
        Enter("delete", keyName);
        return _keys.Remove(keyName);
    }

    /// <summary>Every key and value with its kind, sorted, so two states can be compared as text.</summary>
    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var key in _keys.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('[').Append(key).Append("]\n");
            foreach (var (name, v) in _keys[key].OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                sb.Append(name).Append('=').Append(v.Kind).Append(':').Append(Text(v.Data)).Append('\n');
        }
        return sb.ToString();
    }

    public IReadOnlyList<string> KeyNames => _keys.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public RegistryValue? Get(string key, string name) =>
        _keys.TryGetValue(key, out var values) && values.TryGetValue(name, out var v) ? v : null;

    private static string Text(object data) => data switch
    {
        string[] lines => "[" + string.Join("|", lines) + "]",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(data, CultureInfo.InvariantCulture) ?? "",
    };

    private static object Copy(object data) => data switch
    {
        string[] lines => lines.ToArray(),
        byte[] bytes => bytes.ToArray(),
        _ => data,
    };
}
