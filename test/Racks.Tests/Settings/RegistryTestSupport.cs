using Microsoft.Win32;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Settings;

/// <summary>
/// A throwaway registry area for one test class: <c>Software\RacksTests-&lt;guid&gt;</c> for the app-wide values and a
/// sibling <c>-run</c> key standing in for the Windows Run key, both deleted on Dispose. Nothing here touches
/// HKCU\SOFTWARE\Racks or the real Run key.
/// </summary>
public sealed class ThrowawayRegistry : IDisposable
{
    private readonly string _name = "RacksTests-" + Guid.NewGuid().ToString("N");

    /// <summary>The name to use as <c>RegistryHelper(regKeyName)</c> (the old helper reads <c>SOFTWARE\&lt;name&gt;</c>).</summary>
    public string RootName => _name;
    public string RunName => _name + "-run";

    public HkcuRegistryRoot Root { get; }
    public HkcuRegistryRoot Run { get; }

    public ThrowawayRegistry()
    {
        Root = new HkcuRegistryRoot(@"SOFTWARE\" + RootName);
        Run = new HkcuRegistryRoot(@"SOFTWARE\" + RunName);
    }

    public void Dispose()
    {
        foreach (var name in new[] { RootName, RunName })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\" + name, throwOnMissingSubKey: false); } catch { }
        }
    }

    /// <summary>The value exactly as it sits in the registry, read without going through the code under test.</summary>
    public static (object? Data, RegistryValueKind? Kind) Raw(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        object? data = key?.GetValue(name);
        return data == null ? (null, null) : (data, key!.GetValueKind(name));
    }

    public (object? Data, RegistryValueKind? Kind) RawRoot(string name) => Raw(@"SOFTWARE\" + RootName, name);
    public (object? Data, RegistryValueKind? Kind) RawRun(string name) => Raw(@"SOFTWARE\" + RunName, name);

    /// <summary>Stores a value of any kind directly, to set up what an old or damaged install could contain.</summary>
    public void Seed(string name, object data, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\" + RootName);
        key.SetValue(name, data, kind);
    }
}

/// <summary>A registry root that fails the way a locked-down or broken registry does.</summary>
public sealed class FailingRegistryRoot(bool failReads = true, bool failWrites = true) : IRegistryRoot
{
    public string Path => @"HKCU\SOFTWARE\RacksTests-failing";

    public RegistryValue? Read(string name) =>
        failReads ? throw new UnauthorizedAccessException("registry read denied") : null;

    public void Write(string name, object data, RegistryValueKind kind)
    {
        if (failWrites) throw new UnauthorizedAccessException("registry write denied");
    }

    public bool Delete(string name) =>
        failWrites ? throw new UnauthorizedAccessException("registry delete denied") : false;
}
