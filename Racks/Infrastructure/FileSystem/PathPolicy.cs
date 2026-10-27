using System.IO;
using Racks.Util;

namespace Racks.Infrastructure.FileSystem;

/// <summary>
/// What a name and a path must look like before the app lets them near a destructive operation.
/// A name the user types is a LEAF name: it can never carry a directory part, so a rename can never move
/// an item somewhere else (INV-FS-2). Pure functions, no I/O.
/// </summary>
public static class PathPolicy
{
    /// <summary>Longest file or folder name NTFS accepts, in UTF-16 units.</summary>
    public const int MaxLeafLength = 255;

    // Device names Windows resolves to hardware whatever the folder or extension ("con.txt" is CON).
    // The superscript forms are reserved on current Windows too.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
    };

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
    private const char Backslash = '\\';

    /// <summary>Is <paramref name="name"/> acceptable as the name of ONE file or folder?</summary>
    public static (bool Ok, string Reason) ValidateLeafName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return (false, "The name is empty.");
        if (name.Length > MaxLeafLength) return (false, $"The name is longer than {MaxLeafLength} characters.");

        foreach (char c in name)
            if (char.IsControl(c)) return (false, "The name contains a control character.");

        if (name.IndexOfAny([Backslash, '/']) >= 0)
            return (false, "A name can't contain a slash. It must be a name, not a path.");
        if (name is "." or "..")
            return (false, "\"" + name + "\" is not a valid name.");
        if (name.Contains(':'))
            return (false, "A name can't contain \":\" (drive letters and data streams are not names).");

        int bad = name.IndexOfAny(InvalidNameChars);
        if (bad >= 0) return (false, $"A name can't contain \"{name[bad]}\".");

        if (name[^1] is '.' or ' ')
            return (false, "A name can't end with a dot or a space.");

        // The device name is what comes before the first dot, ignoring spaces before that dot.
        int dot = name.IndexOf('.');
        string stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');
        if (ReservedNames.Contains(stem))
            return (false, $"\"{stem}\" is a reserved Windows device name.");

        return (true, "");
    }

    /// <summary>
    /// Is <paramref name="path"/> a fully qualified path a destructive operation may touch?
    /// UNC paths ARE allowed here, unlike imported layouts (INV-IMPORT-1): a folder rack can legitimately
    /// point at a network share the user chose, and the broker only needs to refuse shapes that are not
    /// ordinary paths: relative or drive-relative, device namespaces, alternate data streams, wildcards,
    /// control characters and <c>..</c> segments.
    /// </summary>
    public static (bool Ok, string Reason) ValidateFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, "The path is empty.");

        foreach (char c in path)
            if (char.IsControl(c)) return (false, "The path contains a control character.");

        string p = path.Replace('/', Backslash);

        // Device namespace: \\.\PhysicalDrive0, \\.\C:, \\?\GLOBALROOT\..., \\?\Volume{...}\...
        // Only the extended-length spelling of an ordinary path (\\?\C:\x, \\?\UNC\host\share\x) is unwrapped.
        if (p.StartsWith(@"\\.\", StringComparison.Ordinal))
            return (false, "Device paths are not allowed.");
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            string rest = p[4..];
            if (rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + rest[4..];
            else if (rest.Length >= 3 && char.IsAsciiLetter(rest[0]) && rest[1] == ':' && rest[2] == Backslash) p = rest;
            else return (false, "Device paths are not allowed.");
        }

        if (p.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0)
            return (false, "The path contains a wildcard or an invalid character.");
        foreach (var segment in p.Split(Backslash))
            if (segment == "..") return (false, "The path contains \"..\".");

        if (SafeWpfValues.IsLocalDrivePath(p)) return (true, "");

        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\host\share[\...]: both parts present, no ':' anywhere (ports, streams, drive letters).
            if (p.Contains(':')) return (false, "The path contains \":\".");
            var parts = p[2..].Split(Backslash, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return (false, "The network path needs a host and a share.");
            return (true, "");
        }

        if (p.IndexOf(':', Math.Min(2, p.Length)) >= 0)
            return (false, "The path contains \":\" (data streams are not allowed).");
        return (false, "The path must be a full path (drive letter or network share).");
    }

    /// <summary>
    /// Turns arbitrary text (a cluster name from Magic Organize) into a name that passes
    /// <see cref="ValidateLeafName"/>: invalid characters become "_", trailing dots and spaces go,
    /// a reserved name gets a "_" prefix, and anything still invalid (or empty) becomes <paramref name="fallback"/>.
    /// </summary>
    public static string SanitizeLeafName(string? raw, string fallback)
    {
        string name = string.Join("_", (raw ?? "").Split(InvalidNameChars)).Trim().TrimEnd('.', ' ');
        if (name.Length == 0) return fallback;
        if (name.Length > MaxLeafLength) name = name[..MaxLeafLength].TrimEnd('.', ' ');

        if (!ValidateLeafName(name).Ok && ValidateLeafName("_" + name).Ok) name = "_" + name;
        return ValidateLeafName(name).Ok ? name : fallback;
    }
}
