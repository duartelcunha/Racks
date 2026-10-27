using System.Reflection;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// The typed form of the values a rack stores (<see cref="RackSettingsSchema"/>): what the validator produced from
/// the registry and what the mapper puts on an <c>Instance</c>. A property is null when the registry had no usable
/// value for it (missing, or refused and audited): the rack then keeps its default. Every property is named like its
/// descriptor, an <c>Instance</c> member and a registry value; the schema test proves it. Lists are never shared:
/// the mapper copies them on the way in and out.
/// </summary>
public sealed class RackSettings
{
    // identity and place
    public string? Name { get; set; }
    public string? Folder { get; set; }
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    // look: numbers
    public double? IdleOpacity { get; set; }
    public double? AnimationSpeed { get; set; }
    public double? MaxGrayScaleStrength { get; set; }
    public double? TitleFontSize { get; set; }
    public int? IconSize { get; set; }
    public int? GridSize { get; set; }
    public int? Opacity { get; set; }
    public int? SortBy { get; set; }
    public int? FolderOrder { get; set; }

    // switches
    public bool? GrayScaleEnabled { get; set; }
    public bool? GrayScaleEnabled_InactiveOnly { get; set; }
    public bool? Minimized { get; set; }
    public bool? ShowHiddenFiles { get; set; }
    public bool? LastAccesedToFirstRow { get; set; }
    public bool? EnableCustomItemsOrder { get; set; }
    public bool? ShowFileExtension { get; set; }
    public bool? ShowFileExtensionIcon { get; set; }
    public bool? ShowHiddenFilesIcon { get; set; }
    public bool? ShowDisplayName { get; set; }
    public bool? IsLocked { get; set; }
    public bool? ShowInGrid { get; set; }
    public bool? AutoExpandonCursor { get; set; }
    public bool? ShowShortcutArrow { get; set; }
    public bool? FolderOpenInsideFrame { get; set; }
    public bool? HideTitleBarIconsWhenInactive { get; set; }
    public bool? SnapWidthToIconWidth { get; set; }
    public bool? SnapWidthToIconWidth_PlusScrollbarWidth { get; set; }
    public bool? CheckFolderSize { get; set; }
    public bool? LinkOnDrop { get; set; }
    public bool? SnapToGrid { get; set; }
    public bool? PinToTop { get; set; }
    public bool? IsTransparent { get; set; }
    public bool? DropShadowEnabled { get; set; }
    public bool? GradientBackgroundEnabled { get; set; }
    public bool? DisableAnimations { get; set; }
    public bool? IsDesktopFilterRack { get; set; }
    public bool? BorderEnabled { get; set; }
    public bool? ActiveBorderEnabled { get; set; }
    public bool? ActiveBackgroundEnabled { get; set; }
    public bool? ActiveTitleTextEnabled { get; set; }
    public System.Windows.HorizontalAlignment? TitleTextAlignment { get; set; }

    // text
    public string? TitleText { get; set; }
    public string? AutoRouteRegex { get; set; }
    public string? FileFilterRegex { get; set; }
    public string? FileFilterHideRegex { get; set; }
    public string? BackgroundImagePath { get; set; }

    // fonts and colors
    public string? TitleFontFamily { get; set; }
    public string? ItemFontFamily { get; set; }
    public string? TitleBarColor { get; set; }
    public string? TitleTextColor { get; set; }
    public string? BorderColor { get; set; }
    public string? ListViewBackgroundColor { get; set; }
    public string? ActiveBackgroundColor { get; set; }
    public string? ActiveBorderColor { get; set; }
    public string? ActiveTitleTextColor { get; set; }
    public string? ListViewFontColor { get; set; }
    public string? ListViewFontShadowColor { get; set; }

    // lists
    public List<string>? AssignedFiles { get; set; }
    public int[]? ShowOnVirtualDesktops { get; set; }
    public List<string>? LastAccessedFiles { get; set; }
    public List<Tuple<string, string>>? CustomOrderFiles { get; set; }

    private static readonly Dictionary<string, PropertyInfo> s_properties = typeof(RackSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The value for a descriptor name, boxed (null when unset).</summary>
    public object? Get(string name) => s_properties[name].GetValue(this);

    /// <summary>Sets the value for a descriptor name; <paramref name="value"/> must be of the property's type (or null).</summary>
    public void Set(string name, object? value) => s_properties[name].SetValue(this, value);

    /// <summary>True when a property of that name exists (the schema test checks every descriptor against this).</summary>
    public static bool HasProperty(string name) => s_properties.ContainsKey(name);

    /// <summary>The CLR type a descriptor's property holds (without the Nullable wrapper), for the schema test.</summary>
    public static Type? PropertyType(string name) =>
        s_properties.TryGetValue(name, out var p) ? Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType : null;
}
