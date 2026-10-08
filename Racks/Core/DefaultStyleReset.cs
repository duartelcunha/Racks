using System.Reflection;
using Microsoft.Win32;

namespace Racks.Core;

/// <summary>
/// "Reset default style": removes the saved global default look for new racks and nothing else.
/// The registry root also holds app switches (physics, hide icons, start on login...) and one-time
/// markers (first run shown, migrations done). Deleting those re-ran first-run steps and migrations,
/// so only values that are rack style properties are removed.
/// </summary>
public static class DefaultStyleReset
{
    private static readonly HashSet<string> StyleValueNames = typeof(Instance)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite)
        .Select(p => p.Name)
        .Where(n => !Instance.NotGlobalProperties.Contains(n))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsStyleValue(string valueName) => StyleValueNames.Contains(valueName);

    /// <summary>Deletes the style defaults under <paramref name="root"/> (opened writable). Returns how many were removed.</summary>
    public static int Run(RegistryKey root)
    {
        int removed = 0;
        foreach (var name in root.GetValueNames())
        {
            if (!IsStyleValue(name)) continue;
            try
            {
                root.DeleteValue(name, throwOnMissingValue: false);
                removed++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DefaultStyleReset: could not delete '{name}': {ex.Message}");
            }
        }
        return removed;
    }
}
