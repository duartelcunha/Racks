namespace Racks.UseCases.Layout;

/// <summary>
/// Every limit an imported layout file is held to (INV-IMPORT-5). A layout comes from outside (a backup, a file somebody
/// shared), so size and shape are bounded BEFORE anything is parsed into objects or written anywhere. The numbers are
/// generous for real use (a typical rack is about 3 KB of JSON, a user has a few dozen racks) and small enough that a
/// hostile file cannot make the app allocate or loop without end. A file that breaks one is refused whole.
/// </summary>
public static class LayoutLimits
{
    /// <summary>The layout format this build writes and reads. A newer file is refused; an older one is read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Largest file read, in bytes (2 MiB, about 700 racks of typical size). Checked on the file before it is parsed.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Deepest nesting of the JSON. A real file nests five levels (file, Racks, a rack, Values, a list); a token deeper than
    /// this refuses the file before it is turned into objects.
    /// </summary>
    public const int MaxJsonDepth = 8;

    /// <summary>Most racks in one file.</summary>
    public const int MaxRacks = 500;

    /// <summary>Most entries in one rack's Values or MultiStringValues object (the app knows 68 value names; the rest is noise).</summary>
    public const int MaxEntriesPerSection = 256;

    /// <summary>Most items in one list value (AssignedFiles, LastAccessedFiles, CustomOrderFiles, ShowOnVirtualDesktops).</summary>
    public const int MaxListItems = 10_000;

    /// <summary>Longest single list item, in characters.</summary>
    public const int MaxListItemLength = 1024;

    /// <summary>Most characters in all the items of one list value together.</summary>
    public const int MaxListTotalChars = 262_144;

    /// <summary>
    /// Longest regular expression text. Regexes are not trusted or compiled at import (they are compiled by SafeRegex, with
    /// a timeout, where they are used); only their size is bounded.
    /// </summary>
    public const int MaxRegexLength = 1024;
}
