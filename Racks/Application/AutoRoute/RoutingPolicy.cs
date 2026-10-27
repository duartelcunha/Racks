using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Util;

namespace Racks.Application.AutoRoute;

/// <summary>Verdict of <see cref="RoutingPolicy.Evaluate"/>.</summary>
public enum EligibilityKind
{
    /// <summary>Safe to move now.</summary>
    Eligible,
    /// <summary>Never route this entry (see the reason).</summary>
    Skip,
    /// <summary>A file that is still open elsewhere (being written or scanned): try again later, within a bound.</summary>
    InUse,
}

public readonly record struct Eligibility(EligibilityKind Kind, string Reason)
{
    public static Eligibility Eligible { get; } = new(EligibilityKind.Eligible, "");
    public static Eligibility Skip(string reason) => new(EligibilityKind.Skip, reason);
    public static Eligibility InUse(string reason) => new(EligibilityKind.InUse, reason);
}

/// <summary>
/// Is this Desktop entry something auto-route may move (INV-ROUTE-1)? Decides only about the entry, never about
/// which rack gets it. Pure apart from three small probes of the disk, which tests replace: file attributes,
/// "is it a junction or symlink" and "is the file still open elsewhere".
///
/// Not eligible: <c>desktop.ini</c>; hidden or system entries; names of unfinished files (browser and
/// download-manager temporaries, Office <c>~$</c> lock files; the later rename to the final name is routed
/// instead); junctions and symlinks (a name pointing somewhere else, moving it would not move data); names that
/// are not a plain leaf name; entries that no longer exist. A file that is still open is <see cref="EligibilityKind.InUse"/>.
///
/// Deliberately eligible: other reparse points. OneDrive and other cloud-sync placeholders on a redirected
/// Desktop are reparse points that carry real user files; they are moved like any file, and never opened for the
/// in-use probe (opening one could start a download).
/// </summary>
public sealed class RoutingPolicy
{
    /// <summary>Suffixes of files that are still being produced. Compared without regard to case.</summary>
    internal static readonly string[] PartialExtensions =
        [".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload"];

    /// <summary>Attributes of a path, or null when nothing exists there (or it can't be read).</summary>
    public Func<string, FileAttributes?> GetAttributes { get; init; } = DefaultGetAttributes;

    /// <summary>True for a junction or symbolic link (NOT for other reparse points).</summary>
    public Func<string, bool> IsLink { get; init; } = JunctionHelper.IsLink;

    /// <summary>True when the file cannot be opened exclusively right now.</summary>
    public Func<string, bool> IsInUse { get; init; } = DefaultIsInUse;

    public static RoutingPolicy Default { get; } = new();

    /// <summary>Checks the name alone (no disk access). Null = fine.</summary>
    public static string? CheckName(string? name)
    {
        var leaf = PathPolicy.ValidateLeafName(name);
        if (!leaf.Ok) return "not a plain file name: " + leaf.Reason;
        if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
            return "desktop.ini is Windows' own folder settings file";
        if (name!.StartsWith("~$", StringComparison.Ordinal))
            return "an Office lock file (~$), not a document";
        foreach (var ext in PartialExtensions)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return $"an unfinished download or temporary file ({ext})";
        return null;
    }

    public Eligibility Evaluate(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path ?? "");
        string name = Path.GetFileName(trimmed);

        string? badName = CheckName(name);
        if (badName != null) return Eligibility.Skip(badName);

        FileAttributes? attrs = GetAttributes(path!);
        if (attrs is null) return Eligibility.Skip("it no longer exists");
        var a = attrs.Value;

        if ((a & FileAttributes.Hidden) != 0) return Eligibility.Skip("it is hidden");
        if ((a & FileAttributes.System) != 0) return Eligibility.Skip("it is a system item");

        bool reparse = (a & FileAttributes.ReparsePoint) != 0;
        if (reparse && IsLink(path!)) return Eligibility.Skip("it is a junction or symbolic link");

        // A folder is moved as a whole; there is no meaningful open-file probe for it.
        if ((a & FileAttributes.Directory) != 0) return Eligibility.Eligible;

        // Cloud placeholders (reparse point, or flagged offline / recall) are moved without being opened.
        const FileAttributes cloud = FileAttributes.Offline | FileAttributes.ReparsePoint
            | (FileAttributes)0x00040000 /* RECALL_ON_DATA_ACCESS */ | (FileAttributes)0x00400000 /* RECALL_ON_OPEN */;
        if ((a & cloud) != 0) return Eligibility.Eligible;

        return IsInUse(path!) ? Eligibility.InUse("it is still open in another program") : Eligibility.Eligible;
    }

    private static FileAttributes? DefaultGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // Read access with sharing denied: fails while any other handle (a writer, a scanner, a copy in progress) is open.
    private static bool DefaultIsInUse(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        // No read permission says nothing about whether a rename works; let the broker decide.
        catch (UnauthorizedAccessException) { return false; }
    }
}
