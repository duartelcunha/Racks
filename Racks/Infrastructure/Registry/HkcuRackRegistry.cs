using Microsoft.Win32;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// <see cref="IRackRegistry"/> over <c>HKCU\SOFTWARE\&lt;InstanceController.appName&gt;\Instances</c>. The path is looked up on
/// every call, so <see cref="ForApp"/> keeps following <c>InstanceController.appName</c> (tests redirect it).
/// </summary>
public sealed class HkcuRackRegistry : IRackRegistry
{
    private readonly Func<string> _instancesKey;

    public HkcuRackRegistry(Func<string> instancesKey) => _instancesKey = instancesKey;

    public static HkcuRackRegistry ForApp() => new(() => @"SOFTWARE\" + InstanceController.appName + @"\Instances");

    public string Path => @"HKCU\" + _instancesKey();

    public IReadOnlyList<string> GetRackKeyNames()
    {
        using var key = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_instancesKey());
        return key == null ? Array.Empty<string>() : key.GetSubKeyNames();
    }

    public IReadOnlyDictionary<string, RegistryValue> ReadValues(string keyName)
    {
        var values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
        using var key = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_instancesKey() + @"\" + keyName);
        if (key == null) return values;
        foreach (var name in key.GetValueNames())
        {
            object? data = key.GetValue(name);
            if (data != null) values[name] = new RegistryValue(data, key.GetValueKind(name));
        }
        return values;
    }

    public void Apply(string keyName, IReadOnlyList<RegistryWrite> writes)
    {
        using var key = global::Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_instancesKey() + @"\" + keyName);
        foreach (var write in writes)
        {
            if (write.IsRemoval) key.DeleteValue(write.Name, throwOnMissingValue: false);
            else key.SetValue(write.Name, write.Data!, write.Kind);
        }
    }

    public bool DeleteKey(string keyName)
    {
        string path = _instancesKey() + @"\" + keyName;
        using (var existing = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path))
        {
            if (existing == null) return false;
        }
        global::Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        return true;
    }
}
