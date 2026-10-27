using Racks.Infrastructure.Registry;
using Racks.Util;

namespace Racks.UseCases.Layout;

/// <summary>
/// The security decisions about an imported layout (INV-IMPORT-1, -3, -5): which value names exist, which paths, fonts,
/// colors and names may be imported, and how big a value or a list may be. Pure functions over text; the shape of the
/// file is <see cref="LayoutParser"/>'s and the meaning of the numbers is <see cref="LayoutValidator"/>'s.
/// <para>A path in an imported file must be empty, a known sentinel, or a fully qualified LOCAL drive path
/// (<c>C:\...</c> or <c>C:/...</c>) with no <c>..</c>, no alternate data stream and no wildcard. Anything that starts with
/// two separators is refused: UNC (<c>\\host\share</c>, <c>//host/share</c>, <c>\\host@SSL\...</c>) and the DOS device forms
/// (<c>\\?\</c>, <c>\\.\</c>, <c>\\?\UNC\</c>). A hostile layout could otherwise point a rack (and, through auto-routing,
/// new Desktop files) at a remote share, and merely touching that path leaks the user's NTLM hash. A user can still pick a
/// network folder in the app; only an IMPORTED file is held to this rule.</para>
/// </summary>
public static class LayoutSecurityPolicy
{
    /// <summary>The value names a layout may carry (every name the app reads, and the discarded legacy one), canonical spelling.</summary>
    public static IEnumerable<string> AllowedValueNames =>
        RackSettingsSchema.All.Where(d => d.ImportAccepted).Select(d => d.Name);

    /// <summary>Case-insensitive, like the registry: "folder" is "Folder". Unknown names are not allowed (they are dropped).</summary>
    public static bool TryGetAllowed(string? name, out SettingDescriptor descriptor)
    {
        if (RackSettingsSchema.TryGet(name, out descriptor) && descriptor.ImportAccepted) return true;
        descriptor = null!;
        return false;
    }

    /// <summary>The refusal for a format version this build cannot read, or null. Missing means the current one; older is read.</summary>
    public static string? CheckVersion(int version)
    {
        if (version > LayoutLimits.CurrentVersion)
            return $"The layout file was made by a newer version of Racks (file format {version}; this version reads up to {LayoutLimits.CurrentVersion}). Update Racks to import it.";
        if (version < 1)
            return $"The layout file has an invalid format version ({version}).";
        return null;
    }

    /// <summary>One safe registry key segment (the schema rule) with no control character: the name becomes a key.</summary>
    public static bool IsSafeRackName(string? name) =>
        RackSettingsSchema.IsValidRackName(name) && !name!.Any(c => c < 0x20);

    public static bool IsSafeImportedPath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (value == "empty" || value == "Default Style") return true;   // rack sentinels
        return SafeWpfValues.IsLocalDrivePath(value);
    }

    /// <summary>Two leading separators: UNC or a DOS device path. Reported with its own message so the user knows what to fix.</summary>
    public static bool IsNetworkOrDevicePath(string? value)
        => value != null && value.Length >= 2 && IsSeparator(value[0]) && IsSeparator(value[1]);

    public static string NetworkPathMessage(string rack, string valueName, string path)
        => valueName == "Folder"
            ? $"Layout rack '{rack}' has a network folder ('{Shorten(path)}'). Imported layouts can only use local folders. Remove that rack from the file or pick the folder again after importing."
            : $"Layout rack '{rack}' has a {valueName} on a network folder ('{Shorten(path)}'). Imported layouts can only use local files. Remove that value from the file or pick the file again after importing.";

    /// <summary>For a text value: the refusal message, or null when the policy accepts it.</summary>
    public static string? CheckText(string rack, SettingDescriptor d, string? s)
    {
        if (d.ImportSection == ImportSection.Path && IsNetworkOrDevicePath(s))
            return NetworkPathMessage(rack, d.Name, s!);
        if ((d.ImportSection == ImportSection.Path && !IsSafeImportedPath(s))
            || (d.ImportSection == ImportSection.RackName && !IsSafeRackName(s))
            || (d.ImportSection == ImportSection.FontFamily && !SafeWpfValues.IsSafeFontFamily(s))
            || (d.ImportSection == ImportSection.Color && !SafeWpfValues.IsSafeColor(s)))
            return $"Layout rack '{rack}' has an unsafe {d.Name}: '{Shorten(s)}'.";

        if (s == null) return null;
        // A registry text value ends at the first NUL: what was stored would not be what the file said.
        if (s.Contains('\0')) return Invalid(rack, d.Name, "it contains a NUL character");
        if (IsRegexField(d.Name) && s.Length > LayoutLimits.MaxRegexLength)
            return Invalid(rack, d.Name, $"a regular expression can have at most {LayoutLimits.MaxRegexLength} characters");

        return null;
    }

    /// <summary>For a list value (REG_MULTI_SZ lines): the refusal message (a NUL), or null. The size caps are <see cref="ListProblem"/>.</summary>
    public static string? CheckLines(string rack, SettingDescriptor d, string[] lines)
        => lines.Any(l => l.Contains('\0')) ? Invalid(rack, d.Name, "an item contains a NUL character") : null;

    /// <summary>
    /// The items of a list the registry keeps as one text (the pipe list and the integer list), or null for any other kind.
    /// </summary>
    public static IReadOnlyList<string>? SplitListText(SettingDescriptor d, string s) => d.Kind switch
    {
        SettingKind.PipeList => s.Split('|', StringSplitOptions.RemoveEmptyEntries),
        SettingKind.IntList => s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries),
        _ => null,
    };

    public static string? Shorten(string? s) => s != null && s.Length > 120 ? s.Substring(0, 120) + "..." : s;

    public static string Invalid(string rack, string field, string why) => $"Layout rack '{rack}' has an invalid {field}: {why}.";

    /// <summary>
    /// Why a list is over its caps (count, item length, total), or null. Not a refusal: the validator drops that one value, as the
    /// loader would not use such a list either, and imports the rest of the file.
    /// </summary>
    public static string? ListProblem(IReadOnlyList<string> items)
    {
        if (items.Count > LayoutLimits.MaxListItems)
            return $"it has {items.Count} items; the most Racks reads is {LayoutLimits.MaxListItems}";
        long total = 0;
        foreach (var item in items)
        {
            if (item.Length > LayoutLimits.MaxListItemLength)
                return $"an item is longer than {LayoutLimits.MaxListItemLength} characters";
            total += item.Length;
        }
        return total > LayoutLimits.MaxListTotalChars
            ? $"the items together are longer than {LayoutLimits.MaxListTotalChars} characters"
            : null;
    }

    private static bool IsRegexField(string name) =>
        name is "AutoRouteRegex" or "FileFilterRegex" or "FileFilterHideRegex";

    private static bool IsSeparator(char c) => c == '\\' || c == '/';
}
