using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Util;

namespace Racks.UseCases.Layout;

/// <summary>
/// An untrusted <see cref="LayoutDocument"/> to a typed list of <see cref="ValidatedRack"/>, or one reason the whole file is
/// refused (INV-IMPORT-1, -3, -5). A partially valid file is never half applied. Nothing here touches the registry.
/// <list type="bullet">
/// <item><b>Version</b>: missing reads as the current one, older is read, a newer one is refused.</item>
/// <item><b>Names</b>: only the value names the app persists, case-insensitive, written in their canonical spelling; two
/// spellings of one name are refused; unknown names are dropped (the app never reads them and old backups carry legacy ones).</item>
/// <item><b>Racks</b>: the name is one safe registry segment; two racks may not differ only in case (registry keys are not case
/// sensitive); a <c>Name</c> value, when present, must equal the rack name (case aside).</item>
/// <item><b>Values</b>: JSON scalars of the right kind, then every value goes through <see cref="RackSettingsValidator"/>, the
/// same strict parser the loader uses. A number out of range, NaN, Infinity or a switch that is not True/False refuses the file
/// with a message naming the rack and the value; it is never clamped silently. Paths, fonts and colors also pass
/// <see cref="LayoutSecurityPolicy"/> first (its messages come first). A list item the loader would drop is dropped.</item>
/// </list>
/// Never throws.
/// </summary>
public static class LayoutValidator
{
    private sealed class Refused(string message) : Exception(message);

    public static LayoutValidationResult Validate(LayoutDocument document)
    {
        try
        {
            string? version = LayoutSecurityPolicy.CheckVersion(document.Version);
            if (version != null) return LayoutValidationResult.Fail(version);

            var result = new List<ValidatedRack>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rack in document.Racks ?? [])
            {
                if (rack == null) throw new Refused("Layout contains an empty rack entry.");
                // A rack name becomes a registry subkey, so it must be a single safe segment.
                if (!LayoutSecurityPolicy.IsSafeRackName(rack.Name))
                    throw new Refused($"Layout contains an unsafe rack name: '{rack.Name}'.");
                if (!names.Add(rack.Name!))
                    throw new Refused($"Layout contains the rack '{rack.Name}' more than once (rack names are not case sensitive).");
                result.Add(ValidateRack(rack));
            }
            return new LayoutValidationResult(result, null);
        }
        catch (Refused ex)
        {
            return LayoutValidationResult.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            // Something in a value could not even be read (for example a lone surrogate in an escape): refuse, do not guess.
            return LayoutValidationResult.Fail("The layout file contains a value that could not be read.");
        }
    }

    private static ValidatedRack ValidateRack(LayoutDocumentRack rack)
    {
        string name = rack.Name!;
        var settings = new RackSettings();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<SettingDescriptor>();

        foreach (var kv in rack.Values ?? new Dictionary<string, object?>())
        {
            if (!LayoutSecurityPolicy.TryGetAllowed(kv.Key, out var d)) continue;   // unknown: dropped
            if (!seen.Add(d.Name))
                throw new Refused($"Layout rack '{name}' sets '{d.Name}' more than once.");
            if (d.ImportSection == ImportSection.MultiString)
                throw new Refused($"Layout rack '{name}' has '{d.Name}' in the wrong section.");

            // JSON null is skipped, as before. Anything else must be a JSON scalar.
            if (kv.Value is null) continue;
            if (kv.Value is not JsonElement e)
                throw new Refused($"Layout rack '{name}' has an invalid {d.Name}.");
            if (e.ValueKind == JsonValueKind.Null) continue;
            // An empty font or color is what a cleared settings box exports: the rack keeps its default, as it would on load.
            if (d.ImportSection is ImportSection.FontFamily or ImportSection.Color
                && e.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(e.GetString())) continue;
            bool ok = d.ImportSection == ImportSection.Scalar
                ? e.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                : e.ValueKind == JsonValueKind.String;
            if (!ok)
                throw new Refused($"Layout rack '{name}' has an invalid {d.Name}.");

            string? text = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? problem = LayoutSecurityPolicy.CheckText(name, d, text);
            if (problem != null) throw new Refused(problem);

            Apply(settings, d, ToRaw(d, e), name, text ?? e.GetRawText(), fields);
        }

        foreach (var kv in rack.MultiStringValues ?? new Dictionary<string, string[]?>())
        {
            if (!LayoutSecurityPolicy.TryGetAllowed(kv.Key, out var d)) continue;   // unknown: dropped
            if (d.ImportSection != ImportSection.MultiString)
                throw new Refused($"Layout rack '{name}' has '{d.Name}' in the wrong section.");
            if (!seen.Add(d.Name))
                throw new Refused($"Layout rack '{name}' sets '{d.Name}' more than once.");
            if (kv.Value == null || kv.Value.Any(x => x == null))
                throw new Refused($"Layout rack '{name}' has an invalid {d.Name}.");
            string? problem = LayoutSecurityPolicy.CheckLines(name, d, kv.Value);
            if (problem != null) throw new Refused(problem);

            Apply(settings, d, new RegistryValue(kv.Value, RegistryValueKind.MultiString), name, string.Join(" | ", kv.Value), fields);
        }

        // The key is the rack's identity: a Name value that says otherwise would load as a different rack than the one checked.
        if (settings.Name != null && !string.Equals(settings.Name, name, StringComparison.OrdinalIgnoreCase))
            throw new Refused($"Layout rack '{name}' has a Name value ('{LayoutSecurityPolicy.Shorten(settings.Name)}') that does not match the rack name.");

        var writes = new List<RegistryWrite>();
        foreach (var d in fields)
        {
            if (!RackSettingsWriter.TryFormat(d, settings.Get(d.Name), bulk: false, out var write, out string? writeProblem))
                throw new Refused(LayoutSecurityPolicy.Invalid(name, d.Name, writeProblem ?? "it cannot be stored"));
            if (write != null) writes.Add(write.Value);
        }
        return new ValidatedRack(name, settings, writes);
    }

    // Runs one value through the loader's own parser. A refusal names the rack, the value and the reason.
    private static void Apply(RackSettings settings, SettingDescriptor d, RegistryValue raw, string rack, string shown, List<SettingDescriptor> fields)
    {
        var parsed = RackSettingsValidator.Parse(d, raw);
        switch (parsed.Status)
        {
            case ParseStatus.Absent:
                return;   // the legacy name, which the app reads and throws away
            case ParseStatus.Rejected:
                throw new Refused(Describe(rack, d, parsed.Reason, shown));
            case ParseStatus.Changed when parsed.Normalizes:
                // A number the loader would move to its limit: the file is wrong, so say so instead of changing it.
                throw new Refused(Describe(rack, d, parsed.Reason, shown));
                // Changed otherwise: list items the loader would drop. The kept items are what the rack will have anyway.
        }
        settings.Set(d.Name, parsed.Value);
        fields.Add(d);
    }

    private static string Describe(string rack, SettingDescriptor d, string reason, string shown) =>
        LayoutSecurityPolicy.Invalid(rack, d.Name, $"{reason} ('{LayoutSecurityPolicy.Shorten(shown)}')");

    // The JSON scalar in the forms the loader reads: text for most values, a number for an integer value.
    private static RegistryValue ToRaw(SettingDescriptor d, JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => new RegistryValue(e.GetString()!, RegistryValueKind.String),
        JsonValueKind.Number when d.Kind == SettingKind.Int && e.TryGetInt64(out long whole) => new RegistryValue(whole, RegistryValueKind.QWord),
        JsonValueKind.Number => new RegistryValue(e.GetRawText(), RegistryValueKind.String),
        JsonValueKind.True => new RegistryValue("True", RegistryValueKind.String),
        _ => new RegistryValue("False", RegistryValueKind.String),
    };
}
