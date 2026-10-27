using Microsoft.Win32;

namespace Racks.Infrastructure.Registry;

/// <summary>A value as it sits in the registry: the data and the kind it is stored as.</summary>
public readonly record struct RegistryValue(object Data, RegistryValueKind Kind);

/// <summary>
/// The seam under <see cref="AppSettings"/> and <see cref="MigrationMarkers"/>: loose values directly under one
/// registry key. Production uses <see cref="HkcuRegistryRoot"/>; tests point it at a throwaway key
/// (<c>Software\RacksTests-&lt;guid&gt;</c>) or at a root that fails on purpose.
/// Implementations throw on a real failure (access denied, I/O). A missing key or value is not a failure.
/// </summary>
public interface IRegistryRoot
{
    /// <summary>Where this root lives, for the audit log (for example <c>HKCU\SOFTWARE\Racks</c>).</summary>
    string Path { get; }

    /// <summary>The stored value, or null when the key or the value does not exist.</summary>
    RegistryValue? Read(string name);

    /// <summary>Creates the key when needed and stores the value as <paramref name="kind"/>.</summary>
    void Write(string name, object data, RegistryValueKind kind);

    /// <summary>Removes the value. True when it existed.</summary>
    bool Delete(string name);
}
