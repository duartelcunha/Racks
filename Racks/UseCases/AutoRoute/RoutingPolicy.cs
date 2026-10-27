using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Util;

namespace Racks.UseCases.AutoRoute;

/// <summary>Verdict of <see cref="RoutingPolicy.Evaluate"/>.</summary>
public enum EligibilityKind
{
    /// <summary>Safe to move now.</summary>
    Eligible,
    /// <summary>Never route this entry (see the reason).</summary>
    Skip,
    /// <summary>A file that is still open elsewhere or still being produced: try again later, within a bound.</summary>
    InUse,
    /// <summary>Nothing exists at the path any more (a temporary name that was already renamed or deleted). Not worth a record.</summary>
    Gone,
}

public readonly record struct Eligibility(EligibilityKind Kind, string Reason)
{
    public static Eligibility Eligible { get; } = new(EligibilityKind.Eligible, "");
    public static Eligibility Skip(string reason) => new(EligibilityKind.Skip, reason);
    public static Eligibility InUse(string reason) => new(EligibilityKind.InUse, reason);
    public static Eligibility Gone { get; } = new(EligibilityKind.Gone, "it no longer exists");
}

/// <summary>
/// Is this Desktop entry something auto-route may move (INV-ROUTE-1)? Decides only about the entry, never about
/// which rack gets it. Pure apart from a few small probes of the disk, which tests replace: file attributes,
/// "is it a junction or symlink", "does a partial-download sibling exist" and "is the file still open elsewhere".
///
/// Not eligible: <c>desktop.ini</c>; hidden or system entries; names of unfinished files (browser and
/// download-manager temporaries, Office <c>~$</c> lock files; the later rename to the final name is routed
/// instead); junctions and symlinks, and a reparse point whose kind can't be read (fail closed); names that
/// are not a plain leaf name; online-only cloud files (see below). A file whose sibling <c>name.part</c> or
/// <c>name.crdownload</c> exists, or that is still open elsewhere, is <see cref="EligibilityKind.InUse"/>.
///
/// Cloud files: a OneDrive or other cloud-sync file on a redirected Desktop is a reparse point that is not a link.
/// One that is only online (flagged offline, recall-on-open or recall-on-data-access) is skipped: moving it out of
/// the sync folder would download it or delete it on the user's other devices. One that is fully on this PC (no
/// such flag) is moved like any file; it is never opened for the in-use probe.
/// </summary>
public sealed class RoutingPolicy
{
    /// <summary>Suffixes of files that are still being produced. Compared without regard to case.</summary>
    internal static readonly string[] PartialExtensions =
        [".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload"];

    // FILE_ATTRIBUTE_RECALL_ON_OPEN (0x00040000) and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (0x00400000): the
    // content is not on this PC and opening or reading it starts a download.
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes OnlineOnly = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    /// <summary>Attributes of a path, or null when nothing exists there (or it can't be read).</summary>
    public Func<string, FileAttributes?> GetAttributes { get; init; } = DefaultGetAttributes;

    /// <summary>True for a junction or symbolic link, false for another reparse point, null when it can't be told.</summary>
    public Func<string, bool?> IsLink { get; init; } = JunctionHelper.TryIsLink;

    /// <summary>True when a file exists at that path (used for the <c>name.part</c> / <c>name.crdownload</c> siblings).</summary>
    public Func<string, bool> Exists { get; init; } = File.Exists;

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

        // First, does it still exist: browsers and editors create and rename temporary names within moments.
        FileAttributes? attrs = GetAttributes(path!);
        if (attrs is null) return Eligibility.Gone;
        var a = attrs.Value;

        string? badName = CheckName(name);
        if (badName != null) return Eligibility.Skip(badName);

        if ((a & FileAttributes.Hidden) != 0) return Eligibility.Skip("it is hidden");
        if ((a & FileAttributes.System) != 0) return Eligibility.Skip("it is a system item");

        bool reparse = (a & FileAttributes.ReparsePoint) != 0;
        if (reparse)
        {
            bool? link = IsLink(path!);
            if (link is null) return Eligibility.Skip("it is a reparse point whose kind could not be read");
            if (link.Value) return Eligibility.Skip("it is a junction or symbolic link");
        }

        // A folder is moved as a whole; there is no meaningful open-file probe for it.
        if ((a & FileAttributes.Directory) != 0) return Eligibility.Eligible;

        if ((a & OnlineOnly) != 0)
            return Eligibility.Skip("it is an online-only cloud file (moving it out of the sync folder could download it or delete it on your other devices)");

        // Firefox and others write "name.part" next to a finished-looking name; the partner file means "not done".
        foreach (var ext in PartialExtensions)
            if (Exists(path + ext))
                return Eligibility.InUse($"a partner file \"{name}{ext}\" is still next to it");

        // A file that is fully on this PC but synced (reparse point, no recall flag) is moved without being opened.
        if (reparse) return Eligibility.Eligible;

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
