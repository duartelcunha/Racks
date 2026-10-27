using Microsoft.Win32;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// <see cref="IRegistryRoot"/> over a key under HKEY_CURRENT_USER. The key path is looked up on every call, so
/// <see cref="ForApp"/> keeps following <c>InstanceController.appName</c> (the one place that names the root;
/// tests redirect it).
/// </summary>
public sealed class HkcuRegistryRoot : IRegistryRoot
{
    public const string AutoRunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly Func<string> _subKey;

    public HkcuRegistryRoot(string subKey) : this(() => subKey) { }

    public HkcuRegistryRoot(Func<string> subKey) => _subKey = subKey;

    /// <summary><c>HKCU\SOFTWARE\&lt;InstanceController.appName&gt;</c>: where the app-wide values live.</summary>
    public static HkcuRegistryRoot ForApp() => new(() => @"SOFTWARE\" + InstanceController.appName);

    /// <summary>The Windows "Run at sign-in" key of the current user.</summary>
    public static HkcuRegistryRoot ForAutoRun() => new(AutoRunSubKey);

    public string Path => @"HKCU\" + _subKey();

    public RegistryValue? Read(string name)
    {
        using var key = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_subKey());
        if (key == null) return null;
        object? data = key.GetValue(name);
        return data == null ? null : new RegistryValue(data, key.GetValueKind(name));
    }

    public void Write(string name, object data, RegistryValueKind kind)
    {
        using var key = global::Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_subKey());
        key.SetValue(name, data, kind);
    }

    public bool Delete(string name)
    {
        using var key = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_subKey(), writable: true);
        if (key == null || key.GetValue(name) == null) return false;
        key.DeleteValue(name, throwOnMissingValue: false);
        return true;
    }
}
