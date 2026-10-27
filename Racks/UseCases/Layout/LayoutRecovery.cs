using System.Text;
using System.Text.Json;
using Racks.Infrastructure.Layout;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// The user's only copy of their racks when a failed import could not put them back is the in-memory snapshot. This turns it into
/// a layout file (the same JSON shape as an export, so importing it brings the racks back) and saves it under
/// <see cref="RecoveryFolder"/>. What a layout can carry is what comes back: binary values and values with a name Racks does
/// not know are not in a layout file.
/// </summary>
public static class LayoutRecovery
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static byte[] Build(IReadOnlyDictionary<string, IReadOnlyDictionary<string, RegistryValue>> snapshot)
    {
        var document = new LayoutDocument { Version = LayoutLimits.CurrentVersion, ExportedBy = "Racks (recovery)", ExportedAt = DateTime.UtcNow, Racks = new() };
        foreach (var (key, values) in snapshot.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rack = new LayoutDocumentRack { Name = key, Values = new(), MultiStringValues = new() };
            foreach (var (name, value) in values)
            {
                if (name.Length == 0) continue;
                switch (value.Data)
                {
                    case string[] lines: rack.MultiStringValues[name] = lines; break;
                    case string or int or long: rack.Values[name] = value.Data; break;
                }
            }
            // A Name value that is not the key would make the file refuse itself: the loader uses the Name value, so the file does too.
            var stored = rack.Values.FirstOrDefault(v => string.Equals(v.Key, RackSettingsSchema.NameField, StringComparison.OrdinalIgnoreCase));
            if (stored.Value is string text && !string.Equals(text, key, StringComparison.OrdinalIgnoreCase))
            {
                if (LayoutSecurityPolicy.IsSafeRackName(text)) rack.Name = text;
                else rack.Values.Remove(stored.Key);
            }
            document.Racks.Add(rack);
        }
        return new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document, Json));
    }

    /// <summary>Saves the snapshot as a new recovery file in <paramref name="directory"/>. Never throws.</summary>
    public static (string? Path, string? Error) Save(string directory, IReadOnlyDictionary<string, IReadOnlyDictionary<string, RegistryValue>> snapshot)
    {
        try
        {
            return RecoveryFolder.Save(directory, Build(snapshot), DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
