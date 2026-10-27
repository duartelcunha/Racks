using Microsoft.Win32;

namespace Racks.Infrastructure.Registry;

/// <summary>One change to a rack key: set a value, or (when <see cref="Data"/> is null) remove it.</summary>
public readonly record struct RegistryWrite(string Name, object? Data, RegistryValueKind Kind)
{
    public static RegistryWrite Set(string name, object data, RegistryValueKind kind) => new(name, data, kind);
    public static RegistryWrite Remove(string name) => new(name, null, RegistryValueKind.None);
    public bool IsRemoval => Data == null;
}

/// <summary>
/// The seam under <see cref="RackSettingsRepository"/>: the per-rack keys <c>Instances\&lt;name&gt;</c>. Production uses
/// <see cref="HkcuRackRegistry"/>; tests use a throwaway root (<c>InstanceController.appName</c>) or a fake that fails on
/// purpose. Implementations throw on a real failure (access denied, I/O); a missing key is not a failure.
/// </summary>
public interface IRackRegistry
{
    /// <summary>Where the rack keys live, for the audit log (for example <c>HKCU\SOFTWARE\Racks\Instances</c>).</summary>
    string Path { get; }

    /// <summary>The names of the rack keys; empty when there is no Instances key (a fresh install).</summary>
    IReadOnlyList<string> GetRackKeyNames();

    /// <summary>Every value of one rack key, by name (case-insensitive); empty when the key does not exist.</summary>
    IReadOnlyDictionary<string, RegistryValue> ReadValues(string keyName);

    /// <summary>Creates the key when needed and applies the changes in order.</summary>
    void Apply(string keyName, IReadOnlyList<RegistryWrite> writes);

    /// <summary>Removes the key with everything under it. True when it existed.</summary>
    bool DeleteKey(string keyName);
}
