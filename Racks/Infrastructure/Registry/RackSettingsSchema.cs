namespace Racks.Infrastructure.Registry;

/// <summary>How a stored per-rack value is read, validated and written.</summary>
public enum SettingKind
{
    /// <summary>A finite number. Written as invariant text (REG_SZ); read from invariant or current-culture text, REG_DWORD or REG_QWORD.</summary>
    Double,
    /// <summary>A 32-bit integer. Written as REG_DWORD; read from REG_DWORD, REG_QWORD or text.</summary>
    Int,
    /// <summary>"True" or "False" text (REG_SZ).</summary>
    Bool,
    /// <summary>A defined member of <see cref="SettingDescriptor.EnumType"/>, stored by name.</summary>
    Enum,
    /// <summary>Free text (titles, regular expressions).</summary>
    String,
    /// <summary>A file system path stored as text: any text without control characters (a network folder is the user's own choice).</summary>
    Path,
    /// <summary>The rack name: one safe registry segment (<see cref="RackSettingsSchema.IsValidRackName"/>).</summary>
    RackName,
    /// <summary>A font family accepted by <see cref="Racks.Util.SafeWpfValues.IsSafeFontFamily"/>.</summary>
    FontFamily,
    /// <summary>A color accepted by <see cref="Racks.Util.SafeWpfValues.IsSafeColor"/>.</summary>
    Color,
    /// <summary>A list of text lines (REG_MULTI_SZ).</summary>
    MultiString,
    /// <summary>A comma-joined list of integers (REG_SZ), read with ',' ';' and ' ' as separators.</summary>
    IntList,
    /// <summary>A '|'-joined list of file names (REG_SZ).</summary>
    PipeList,
    /// <summary>REG_MULTI_SZ lines of the form "first,second" that become pairs.</summary>
    PairList,
    /// <summary>A name that older builds wrote and this one reads and throws away.</summary>
    Legacy,
}

/// <summary>What the validator does with a number outside <see cref="SettingDescriptor.Min"/>..<see cref="SettingDescriptor.Max"/>.</summary>
public enum RangePolicy
{
    /// <summary>No range check beyond "is a finite number".</summary>
    None,
    /// <summary>The value is refused (audited) and the default applies.</summary>
    Reject,
    /// <summary>The value is moved to the nearest limit (audited). Only where the setter of <c>Instance</c> already clamps.</summary>
    Clamp,
}

/// <summary>Which section of a layout file (<c>LayoutValidator</c>) holds a value, and which extra check import applies to it.</summary>
public enum ImportSection
{
    Scalar,
    Path,
    RackName,
    FontFamily,
    Color,
    MultiString,
}

/// <summary>
/// One row of <see cref="RackSettingsSchema"/>: everything the app knows about one value stored under
/// <c>Instances\&lt;rack&gt;</c> (and, for <see cref="IsGlobalDefault"/>, under the registry root).
/// </summary>
/// <param name="Name">The registry value name, which is also the <c>Instance</c> property (or field) and the <see cref="RackSettings"/> property.</param>
/// <param name="Default">What a rack has when the value is missing or refused. Null for the four screen-dependent values.</param>
/// <param name="Min">Lower limit when <paramref name="Range"/> is not <see cref="RangePolicy.None"/>.</param>
/// <param name="Max">Upper limit when <paramref name="Range"/> is not <see cref="RangePolicy.None"/>.</param>
/// <param name="EnumType">The enum for <see cref="SettingKind.Enum"/>.</param>
/// <param name="ImportAccepted">Whether a layout file may carry the value (every name the app reads, and the legacy one).</param>
/// <param name="IsGlobalDefault">Whether a new rack takes its starting value from the registry root ("default style").</param>
/// <param name="Why">One line for a reader: where the limits come from.</param>
public sealed record SettingDescriptor(
    string Name,
    SettingKind Kind,
    object? Default,
    RangePolicy Range = RangePolicy.None,
    double Min = 0,
    double Max = 0,
    Type? EnumType = null,
    bool ImportAccepted = true,
    bool IsGlobalDefault = false,
    string Why = "")
{
    /// <summary>False only for the legacy name, which is read and discarded.</summary>
    public bool IsPersisted => Kind != SettingKind.Legacy;

    /// <summary>The layout-file section for this value (the layout import allow-list derives from this table).</summary>
    public ImportSection ImportSection => Kind switch
    {
        SettingKind.Path => ImportSection.Path,
        SettingKind.RackName => ImportSection.RackName,
        SettingKind.FontFamily => ImportSection.FontFamily,
        SettingKind.Color => ImportSection.Color,
        SettingKind.MultiString or SettingKind.PairList => ImportSection.MultiString,
        _ => ImportSection.Scalar,
    };
}

/// <summary>
/// The ONE table of the values a rack stores in the registry (INV-REG-2). The reader, the writers, the layout
/// import allow-list and the global-default reader all derive from it, so a name cannot be known to one and
/// forgotten by another.
/// <para>Ranges. A number is checked only where there is an evident limit: a setter that already clamps
/// (<c>IconSize</c> 16-256, <c>GridSize</c> 2-256: <see cref="RangePolicy.Clamp"/>) or a limit the settings UI
/// enforces (sliders and number boxes; <see cref="RangePolicy.Reject"/>, the default then applies). Positions and
/// sizes only have to be finite and not absurd (+-100000 px). Where the UI limit is narrower than what earlier
/// builds accepted (<c>TitleFontSize</c>: the box allows 8-72) the table keeps the wider, safe window. A number
/// that is not finite (NaN, Infinity, overflow) is always refused.</para>
/// </summary>
public static class RackSettingsSchema
{
    /// <summary>Longest text accepted for a free text or path value. Windows paths stop at 32767 characters.</summary>
    public const int MaxTextLength = 32767;

    public const string NameField = "Name";

    private static readonly List<SettingDescriptor> s_all = Build();
    private static readonly Dictionary<string, SettingDescriptor> s_byName =
        s_all.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every value name, in a stable order (the persisted ones and the legacy one).</summary>
    public static IReadOnlyList<SettingDescriptor> All => s_all;

    /// <summary>The 67 values that are stored and read back.</summary>
    public static IEnumerable<SettingDescriptor> Persisted => s_all.Where(d => d.IsPersisted);

    /// <summary>The 32 values a new rack starts from (registry root).</summary>
    public static IEnumerable<SettingDescriptor> GlobalDefaults => s_all.Where(d => d.IsGlobalDefault);

    /// <summary>Case-insensitive, like the registry itself.</summary>
    public static bool TryGet(string? name, out SettingDescriptor descriptor)
    {
        if (name != null && s_byName.TryGetValue(name, out var d)) { descriptor = d; return true; }
        descriptor = null!;
        return false;
    }

    /// <summary>
    /// A rack name is used verbatim as a registry subkey, so it must be one safe segment: not blank, no '\' or '/',
    /// not "." or "..", at most 255 characters. (The rule the layout import applies to imported names; it also refuses control characters.)
    /// </summary>
    public static bool IsValidRackName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.IndexOf('\\') < 0
           && name.IndexOf('/') < 0
           && name != "." && name != ".."
           && name.Length <= 255;

    private static List<SettingDescriptor> Build()
    {
        const RangePolicy R = RangePolicy.Reject;
        const RangePolicy C = RangePolicy.Clamp;
        var list = new List<SettingDescriptor>();

        void Dbl(string name, object? def, RangePolicy range, double min, double max, string why, bool global = false)
            => list.Add(new SettingDescriptor(name, SettingKind.Double, def, range, min, max, IsGlobalDefault: global, Why: why));
        void Num(string name, int def, RangePolicy range, double min, double max, string why, bool global = false)
            => list.Add(new SettingDescriptor(name, SettingKind.Int, def, range, min, max, IsGlobalDefault: global, Why: why));
        void Bool(string name, bool def, bool global = false)
            => list.Add(new SettingDescriptor(name, SettingKind.Bool, def, IsGlobalDefault: global));
        void Text(string name, SettingKind kind, object? def, bool global = false)
            => list.Add(new SettingDescriptor(name, kind, def, IsGlobalDefault: global));

        // ---- identity and place ----------------------------------------------------------------------------
        Text("Name", SettingKind.RackName, "");
        Text("Folder", SettingKind.Path, "empty");
        Dbl("PosX", null, R, -100000, 100000, "finite and on any plausible multi-monitor layout");
        Dbl("PosY", null, R, -100000, 100000, "finite and on any plausible multi-monitor layout");
        Dbl("Width", 175.0, R, 0, 100000, "finite, not negative; 0 is read as 'unset' by the code that lays racks out");
        Dbl("Height", 215.0, R, 0, 100000, "finite, not negative; 0 is read as 'unset' by the code that lays racks out");

        // ---- look: numbers ------------------------------------------------------------------------------------
        Dbl("IdleOpacity", 1.0, R, 0, 1, "window opacity; the settings slider is 0-10 divided by 10", global: true);
        Dbl("AnimationSpeed", 1.0, R, 0, 10, "the settings slider is 0-10 times 0.25 (up to 2.5); 10 leaves room", global: true);
        Dbl("MaxGrayScaleStrength", 1.0, R, 0, 1, "the settings slider is 0-10 divided by 10", global: true);
        Dbl("TitleFontSize", 13.0, R, 1, 200, "the settings box allows 8-72; earlier builds had no limit, so the window is wider", global: true);
        Num("IconSize", 32, C, 16, 256, "Instance.IconSize clamps to 16-256", global: true);
        Num("GridSize", 16, C, 2, 256, "Instance.GridSize clamps to 2-256");
        Num("Opacity", 26, R, 0, 255, "the alpha of the rack background (a byte)", global: true);
        Num("SortBy", 1, R, 1, 10, "1-10: five sort keys, ascending and descending (SortToggle)", global: true);
        Num("FolderOrder", 0, R, 0, 2, "0 none, 1 folders first, 2 folders last (FolderOrder enum)", global: true);

        // ---- switches -------------------------------------------------------------------------------------------
        Bool("GrayScaleEnabled", false, global: true);
        Bool("GrayScaleEnabled_InactiveOnly", false, global: true);
        Bool("Minimized", false);
        Bool("ShowHiddenFiles", false, global: true);
        Bool("LastAccesedToFirstRow", false);
        Bool("EnableCustomItemsOrder", true);
        Bool("ShowFileExtension", false, global: true);
        Bool("ShowFileExtensionIcon", false, global: true);
        Bool("ShowHiddenFilesIcon", false, global: true);
        Bool("ShowDisplayName", true, global: true);
        Bool("IsLocked", false);
        Bool("ShowInGrid", true);
        Bool("AutoExpandonCursor", false);
        Bool("ShowShortcutArrow", true);
        Bool("FolderOpenInsideFrame", false);
        Bool("HideTitleBarIconsWhenInactive", false);
        Bool("SnapWidthToIconWidth", false);
        Bool("SnapWidthToIconWidth_PlusScrollbarWidth", false);
        Bool("CheckFolderSize", false);
        Bool("LinkOnDrop", false);
        Bool("SnapToGrid", false);
        Bool("PinToTop", false);
        Bool("IsTransparent", false);
        Bool("DropShadowEnabled", true);
        Bool("GradientBackgroundEnabled", true);
        Bool("DisableAnimations", false);
        Bool("IsDesktopFilterRack", false);
        Bool("BorderEnabled", false, global: true);
        Bool("ActiveBorderEnabled", false, global: true);
        Bool("ActiveBackgroundEnabled", false, global: true);
        Bool("ActiveTitleTextEnabled", false, global: true);
        list.Add(new SettingDescriptor("TitleTextAlignment", SettingKind.Enum, System.Windows.HorizontalAlignment.Center,
            EnumType: typeof(System.Windows.HorizontalAlignment), IsGlobalDefault: true,
            Why: "Enum.TryParse plus Enum.IsDefined: a number that names no member is refused"));

        // ---- text -----------------------------------------------------------------------------------------------
        Text("TitleText", SettingKind.String, null);
        Text("AutoRouteRegex", SettingKind.String, "");
        Text("FileFilterRegex", SettingKind.String, "", global: true);
        Text("FileFilterHideRegex", SettingKind.String, "", global: true);
        Text("BackgroundImagePath", SettingKind.Path, "");

        // ---- fonts and colors (INV-IMPORT-3) ---------------------------------------------------------------------------
        Text("TitleFontFamily", SettingKind.FontFamily, "Segoe UI", global: true);
        Text("ItemFontFamily", SettingKind.FontFamily, "Segoe UI", global: true);
        Text("TitleBarColor", SettingKind.Color, "#0C000000", global: true);
        Text("TitleTextColor", SettingKind.Color, "#FFFFFF", global: true);
        Text("BorderColor", SettingKind.Color, "#FFFFFF");
        Text("ListViewBackgroundColor", SettingKind.Color, "#0C000000", global: true);
        Text("ActiveBackgroundColor", SettingKind.Color, "#0C000000", global: true);
        Text("ActiveBorderColor", SettingKind.Color, "#FFFFFF", global: true);
        Text("ActiveTitleTextColor", SettingKind.Color, "#FFFFFF", global: true);
        Text("ListViewFontColor", SettingKind.Color, "#FFFFFF", global: true);
        Text("ListViewFontShadowColor", SettingKind.Color, "#000000", global: true);

        // ---- lists --------------------------------------------------------------------------------------------------------
        Text("AssignedFiles", SettingKind.PipeList, null);
        Text("ShowOnVirtualDesktops", SettingKind.IntList, null);
        Text("LastAccessedFiles", SettingKind.MultiString, null);
        Text("CustomOrderFiles", SettingKind.PairList, null);

        // ---- legacy -----------------------------------------------------------------------------------------------------------
        // The old inverted drop semantic. Read and discarded: "move" is the default now.
        list.Add(new SettingDescriptor("MoveFilesOnDrop", SettingKind.Legacy, null));

        return list;
    }
}
