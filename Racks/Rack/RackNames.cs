namespace Racks.Rack;

/// <summary>
/// A rack's name is also its registry key, so two racks with one name overwrite each other. Magic Organize
/// names racks after file groups ("Images"), and a second run used to reuse the first run's names.
/// </summary>
public static class RackNames
{
    /// <summary>"Images" if free, else "Images 2", "Images 3"... Compared without regard to case, like the registry.</summary>
    public static string Unique(string wanted, IEnumerable<string?> existing)
    {
        var taken = new HashSet<string>(existing.Where(n => !string.IsNullOrEmpty(n))!, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted)) return wanted;
        for (int n = 2; ; n++)
        {
            string candidate = $"{wanted} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
