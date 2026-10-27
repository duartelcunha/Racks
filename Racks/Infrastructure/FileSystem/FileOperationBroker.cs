using System.IO;
using Racks.Infrastructure.Logging;
using Racks.Util;

namespace Racks.Infrastructure.FileSystem;

/// <summary>
/// The one gateway for destructive file operations on user data (INV-FS-1). Every call is
/// validate -> preflight -> execute -> confirm on disk -> ONE audit record -> result, and never throws for
/// an expected failure: a bad name, a protected folder, a name collision or a locked file is a status.
///
/// It is a facade: the work is done by the primitives <see cref="SafeMove"/> (canonicalization, protected
/// roots, cross-volume copy-then-delete) and <see cref="SafeDelete"/> (reparse-point-aware tree delete,
/// Recycle Bin). Those are internal; nothing else in the app may call them or File/Directory mutators
/// (see FileMutationGuardTests for what is enforced and what is not).
/// </summary>
public sealed class FileOperationBroker
{
    /// <summary>The broker the app uses. Writes to <see cref="AuditLog.Current"/>.</summary>
    public static FileOperationBroker Default { get; } = new(AuditLog.Forwarder);

    private readonly IAuditSink _audit;

    public FileOperationBroker(IAuditSink audit)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>Moves a file or folder. Never overwrites; falls back to copy+delete across volumes.</summary>
    public FileOperationResult Move(string source, string destination, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.Move, source, destination, reason));

    /// <summary>Like <see cref="Move"/> but refuses anything that would need a copy (another volume). For the uninstall path.</summary>
    public FileOperationResult MoveSameVolume(string source, string destination, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.Move, source, destination, reason, SameVolumeRenameOnly: true));

    /// <summary>Renames an item inside its own folder. <paramref name="newLeafName"/> is a name, never a path.</summary>
    public FileOperationResult Rename(string existingPath, string newLeafName, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.Rename, existingPath, newLeafName, reason, SameVolumeRenameOnly: true));

    public FileOperationResult DeleteToRecycleBin(string path, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.DeleteToRecycleBin, path, null, reason));

    /// <summary>Deletes a folder and everything in it without following junctions or symlinks.</summary>
    public FileOperationResult DeleteTree(string path, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.DeleteTree, path, null, reason));

    /// <summary>Creates one folder. Its name must be a valid leaf name and its parent must already exist.</summary>
    public FileOperationResult CreateDirectory(string path, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.CreateDirectory, path, null, reason));

    /// <summary>
    /// Removes ONE empty folder (never recursive). Refuses a protected folder, a folder with anything in it
    /// (including hidden files), a file and a junction or symlink (use <see cref="Unlink"/> for those).
    /// </summary>
    public FileOperationResult DeleteEmptyDirectory(string path, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.DeleteEmptyDirectory, path, null, reason));

    /// <summary>
    /// Removes a NAME, never data: a junction or symbolic link (the target is left alone), or one extra hard link
    /// name of a file whose data is still reachable under another name (link count above 1). Anything else, a real
    /// folder, a file with a single name, a cloud placeholder, is refused.
    /// </summary>
    public FileOperationResult Unlink(string path, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.Unlink, path, null, reason));

    /// <summary>
    /// Deletes a shortcut file: extension <c>.lnk</c> or <c>.url</c>, a plain file (not a folder or link) of at most
    /// 1 MiB, sitting directly inside <paramref name="containingFolder"/>, which must not be a protected folder.
    /// Those are the only things checked; there is no proof that Racks created the file, so callers must pass the
    /// folder of a rack sandbox. Deliberately narrow: it cannot delete any other kind of file, a file elsewhere,
    /// a folder or a link.
    /// </summary>
    public FileOperationResult DeleteRacksShortcut(string path, string containingFolder, string reason)
        => Execute(new FileOperationIntent(FileOperationKind.DeleteRacksShortcut, path, containingFolder, reason));

    public FileOperationResult Execute(FileOperationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        OperationContext ctx;
        FileOperationResult result;
        if (string.IsNullOrWhiteSpace(intent.Reason))
        {
            ctx = OperationContext.Create("(no reason given)");
            result = Make(ctx, OperationStatus.Rejected, "An operation needs a reason.", intent.Source, intent.Destination);
        }
        else
        {
            ctx = OperationContext.Create(intent.Reason);
            try
            {
                result = intent.Kind switch
                {
                    FileOperationKind.Move => DoMove(ctx, intent),
                    FileOperationKind.Rename => DoRename(ctx, intent),
                    FileOperationKind.DeleteToRecycleBin => DoRecycle(ctx, intent),
                    FileOperationKind.DeleteTree => DoDeleteTree(ctx, intent),
                    FileOperationKind.CreateDirectory => DoCreateDirectory(ctx, intent),
                    FileOperationKind.DeleteEmptyDirectory => DoDeleteEmptyDirectory(ctx, intent),
                    FileOperationKind.Unlink => DoUnlink(ctx, intent),
                    FileOperationKind.DeleteRacksShortcut => DoDeleteRacksShortcut(ctx, intent),
                    _ => Make(ctx, OperationStatus.Rejected, $"Unknown operation {intent.Kind}.", intent.Source, intent.Destination),
                };
            }
            catch (Exception ex)
            {
                result = Make(ctx, OperationStatus.Failed, $"Unexpected error: {ex.Message}", intent.Source, intent.Destination);
            }
        }

        WriteAudit(ctx, intent.Kind, result);
        return result;
    }

    // ---- Move ------------------------------------------------------------------------------

    private static FileOperationResult DoMove(OperationContext ctx, FileOperationIntent i)
    {
        string src = i.Source, dest = i.Destination ?? "";
        if (!i.NeverOverwrite) return Make(ctx, OperationStatus.Rejected, "Overwriting existing items is not supported.", src, dest);

        var vs = PathPolicy.ValidateFullPath(src);
        if (!vs.Ok) return Make(ctx, OperationStatus.Rejected, "Source: " + vs.Reason, src, dest);
        var vd = PathPolicy.ValidateFullPath(dest);
        if (!vd.Ok) return Make(ctx, OperationStatus.Rejected, "Destination: " + vd.Reason, src, dest);

        // The destination's own name is judged like a typed one (INV-FS-2), before anything is touched: Windows
        // writes a file moved to "dest\NUL" into the NUL device and it is gone. Only the final segment is checked;
        // the folders above it are whatever the caller chose.
        var vl = PathPolicy.ValidateLeafName(Path.GetFileName(Path.TrimEndingDirectorySeparator(dest)));
        if (!vl.Ok) return Make(ctx, OperationStatus.Rejected, "Destination name: " + vl.Reason, src, dest);

        return MoveChecked(ctx, src, dest, i.SameVolumeRenameOnly);
    }

    // Preflight with SafeMove's own rules, once (so the reasons users see are unchanged and the ~50 CreateFile calls
    // of the canonical-path checks are not repeated), then the move, then a look at the disk. After a clean
    // preflight, a SafeMove "Rejected" is an I/O failure, hence Failed.
    private static FileOperationResult MoveChecked(OperationContext ctx, string src, string dest, bool sameVolumeOnly)
    {
        var verdict = SafeMove.Check(src, dest, out string reason);
        if (verdict != null)
        {
            var status = verdict == SafeMove.Result.Skipped ? OperationStatus.Skipped : OperationStatus.Rejected;
            return Make(ctx, status, reason, src, dest);
        }

        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dest));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return Make(ctx, OperationStatus.Rejected, "The destination folder doesn't exist.", src, dest);

        if (sameVolumeOnly && !SameVolume(src, parent))
            return Make(ctx, OperationStatus.Rejected, "The destination is on another volume, and this operation may not copy.", src, dest);

        var moved = SafeMove.MoveAfterCheck(src, dest, out reason, allowCrossVolumeCopy: !sameVolumeOnly);
        return moved switch
        {
            SafeMove.Result.Moved => ConfirmMoved(ctx, src, dest, reason),
            SafeMove.Result.Skipped => Make(ctx, OperationStatus.Skipped, reason, src, dest),
            _ => Make(ctx, OperationStatus.Failed, reason, src, dest),
        };
    }

    internal static FileOperationResult ConfirmMoved(OperationContext ctx, string src, string dest, string moveReason)
    {
        if (!Exists(dest))
            return Make(ctx, OperationStatus.Failed, "The move finished but the destination can't be found.", src, dest);
        if (Exists(src))
        {
            string left = string.IsNullOrEmpty(moveReason) ? "The original is still in place." : moveReason;
            return Make(ctx, OperationStatus.PartiallyCompleted, left, src, dest);
        }
        return Make(ctx, OperationStatus.Succeeded, "", src, dest);
    }

    private static bool SameVolume(string a, string b)
    {
        string ra = Path.GetPathRoot(SafeMove.Canonicalize(a)) ?? "";
        string rb = Path.GetPathRoot(SafeMove.Canonicalize(b)) ?? "";
        return ra.Length > 0 && string.Equals(ra.TrimEnd('\\'), rb.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Rename ----------------------------------------------------------------------------

    /// <summary>Test seam: called with (source, destination) before every low-level rename step; may throw to simulate a failure.</summary>
    internal Action<string, string>? RenameStepHook { get; set; }

    private FileOperationResult DoRename(OperationContext ctx, FileOperationIntent i)
    {
        string existing = i.Source, leaf = i.Destination ?? "";

        // The new name is checked first and on its own: nothing typed by a user may ever reach Path.Combine
        // unless it is a plain name (INV-FS-2).
        var vl = PathPolicy.ValidateLeafName(leaf);
        if (!vl.Ok) return Make(ctx, OperationStatus.Rejected, vl.Reason, existing, leaf);
        var vp = PathPolicy.ValidateFullPath(existing);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, existing, leaf);

        string current = Path.TrimEndingDirectorySeparator(existing);
        string? dir = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(dir))
            return Make(ctx, OperationStatus.Rejected, "A drive or a share root can't be renamed.", existing, leaf);

        string target = Path.Combine(dir, leaf);
        if (!string.Equals(Path.GetDirectoryName(target), dir, StringComparison.OrdinalIgnoreCase))
            return Make(ctx, OperationStatus.Rejected, "The new name would move the item to another folder.", existing, target);

        if (!Exists(current))
            return Make(ctx, OperationStatus.Rejected, $"\"{Path.GetFileName(current)}\" no longer exists.", existing, target);

        // A rename touches only the item itself and keeps it in its own folder, so it needs no volume check, and the
        // protected-folder check looks at the item itself, not at what a junction or symlink points to: a
        // Ctrl+dropped reference to Documents or to a folder on another drive is a link inside a rack and renames
        // fine, while the real Documents folder is still refused.
        if (SafeMove.IsProtectedOrAncestorLink(current, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{Path.GetFileName(current)}\" is a {kind} — refusing to rename.", existing, target);

        if (string.Equals(current, target, StringComparison.Ordinal))
            return Make(ctx, OperationStatus.Skipped, "The item already has that name.", existing, target);

        bool caseOnly = string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Exists(target))
            return Make(ctx, OperationStatus.Skipped, $"\"{leaf}\" already exists.", existing, target);

        string? failure = MoveItem(current, target);
        if (failure != null && caseOnly)
            return RenameViaTemporaryName(ctx, existing, current, dir, target, failure);
        if (failure != null)
            return Make(ctx, OperationStatus.Failed, $"Failed to rename \"{Path.GetFileName(current)}\": {failure}", existing, target);

        return ConfirmRenamed(ctx, existing, dir, leaf, target, caseOnly);
    }

    // One rename step: File.Move / Directory.Move on the item itself (a junction or symlink is renamed as
    // the link). NTFS renames case-only changes directly. Returns null on success, else the reason.
    private string? MoveItem(string from, string to)
    {
        try
        {
            RenameStepHook?.Invoke(from, to);
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static FileOperationResult ConfirmRenamed(OperationContext ctx, string existing, string dir, string leaf, string target, bool caseOnly)
    {
        // Exists() is case-insensitive; for a case change ask the folder for the spelling it stores.
        bool ok = Exists(target) && (!caseOnly ||
            Directory.EnumerateFileSystemEntries(dir, leaf).Any(e => string.Equals(Path.GetFileName(e), leaf, StringComparison.Ordinal)));
        if (ok && (caseOnly || !Exists(existing))) return Make(ctx, OperationStatus.Succeeded, "", existing, target);
        return Make(ctx, OperationStatus.Failed, "The rename could not be confirmed.", existing, target);
    }

    // Fallback for a case-only rename the direct move refused: two moves through a temporary name in the same
    // folder. The temporary name keeps the item's extension (so it stays recognisable to the shell and to a
    // rack that scans the folder in between). If the second step fails, put the first one back.
    private FileOperationResult RenameViaTemporaryName(OperationContext ctx, string existing, string current, string dir, string target, string directFailure)
    {
        string leaf = Path.GetFileName(target);
        string temp = Path.Combine(dir, ".racks-rename-" + ctx.Id + (Directory.Exists(current) ? "" : Path.GetExtension(current)));

        string? first = MoveItem(current, temp);
        if (first != null)
            return Make(ctx, OperationStatus.Failed, $"Failed to rename \"{Path.GetFileName(current)}\": {first}", existing, target);

        string? second = MoveItem(temp, target);
        if (second != null)
        {
            if (MoveItem(temp, current) == null)
                return Make(ctx, OperationStatus.Failed, $"Failed to rename \"{Path.GetFileName(current)}\" to \"{leaf}\": {second} The original name was restored.", existing, target);
            return Make(ctx, OperationStatus.Failed,
                $"Failed to rename to \"{leaf}\": {second} The item could not be put back and is now named \"{Path.GetFileName(temp)}\".", existing, temp);
        }

        return ConfirmRenamed(ctx, existing, dir, leaf, target, caseOnly: true);
    }

    // ---- Delete ----------------------------------------------------------------------------

    private static FileOperationResult DoRecycle(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var rejected = CheckDeletable(ctx, path, requireDirectory: false);
        if (rejected != null) return rejected;

        // Windows deletes PERMANENTLY and silently when it cannot recycle (network share, removable drive...).
        // Where we can know that beforehand, refuse and leave the item alone. Residual risk (bin disabled or
        // too small for the item) is documented on SafeDelete.ToRecycleBin and in INV-FS-1.
        if (!SafeDelete.CanRecycle(path))
            return Make(ctx, OperationStatus.Failed,
                "This item is on a network or removable drive and can't be sent to the Recycle Bin safely, so it was left in place.", path, null);

        bool sent = SafeDelete.ToRecycleBin(path);
        if (!Exists(path)) return Make(ctx, OperationStatus.Succeeded, "", path, null);
        return Make(ctx, OperationStatus.Failed,
            sent ? "The item is still there after being sent to the Recycle Bin." : "Couldn't send the item to the Recycle Bin.", path, null);
    }

    private static FileOperationResult DoDeleteTree(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var rejected = CheckDeletable(ctx, path, requireDirectory: true);
        if (rejected != null) return rejected;

        return SafeDelete.TryDeleteTree(path)
            ? Make(ctx, OperationStatus.Succeeded, "", path, null)
            : Make(ctx, OperationStatus.PartiallyCompleted,
                $"Couldn't fully delete \"{Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}\" (a file may be in use).", path, null);
    }

    private static FileOperationResult? CheckDeletable(OperationContext ctx, string path, bool requireDirectory)
    {
        var vp = PathPolicy.ValidateFullPath(path);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, path, null);

        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (requireDirectory && File.Exists(path))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a file, not a folder.", path, null);
        if (!Exists(path))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" no longer exists.", path, null);
        if (SafeMove.IsProtectedOrAncestorPath(path, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a {kind} — refusing to delete.", path, null);
        return null;
    }

    // ---- DeleteEmptyDirectory / Unlink / DeleteRacksShortcut --------------------------------

    private static FileOperationResult DoDeleteEmptyDirectory(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var vp = PathPolicy.ValidateFullPath(path);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, path, null);

        string trimmed = Path.TrimEndingDirectorySeparator(path);
        string name = Path.GetFileName(trimmed);
        if (!TryGetAttributes(trimmed, out FileAttributes attrs))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" no longer exists.", path, null);
        if ((attrs & FileAttributes.Directory) == 0)
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a file, not a folder.", path, null);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a link, not a real folder. Use Unlink for links.", path, null);
        if (SafeMove.IsProtectedPath(trimmed, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a {kind} — refusing to delete.", path, null);
        if (Directory.EnumerateFileSystemEntries(trimmed).Any())
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" isn't empty, so it was left in place.", path, null);

        // Non-recursive: if something appeared since the check above, the delete itself fails and nothing is lost.
        try { Directory.Delete(trimmed, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Make(ctx, OperationStatus.Failed, $"Couldn't delete the empty folder \"{name}\": {ex.Message}", path, null);
        }
        return Directory.Exists(trimmed)
            ? Make(ctx, OperationStatus.Failed, $"\"{name}\" is still there after the delete.", path, null)
            : Make(ctx, OperationStatus.Succeeded, "", path, null);
    }

    private static FileOperationResult DoUnlink(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var vp = PathPolicy.ValidateFullPath(path);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, path, null);

        string trimmed = Path.TrimEndingDirectorySeparator(path);
        string name = Path.GetFileName(trimmed);
        if (!TryGetAttributes(trimmed, out FileAttributes attrs))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" no longer exists.", path, null);

        // The item itself is judged, never what it points to: a link to Documents is a link and may be unlinked,
        // the real Documents folder (or a redirect that IS the Desktop) may not.
        if (SafeMove.IsProtectedLink(trimmed, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a {kind} — refusing to unlink.", path, null);

        bool isDirectory = (attrs & FileAttributes.Directory) != 0;
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            if (!JunctionHelper.IsLink(trimmed))
                return Make(ctx, OperationStatus.Rejected,
                    $"\"{name}\" is not a junction or a symbolic link (it can be a cloud placeholder that holds the file), so it was left in place.", path, null);
            try
            {
                if (isDirectory) Directory.Delete(trimmed, recursive: false);
                else File.Delete(trimmed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Make(ctx, OperationStatus.Failed, $"Couldn't remove the link \"{name}\": {ex.Message}", path, null);
            }
        }
        else
        {
            if (isDirectory)
                return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a real folder, not a link.", path, null);
            int names = HardlinkHelper.GetLinkCount(trimmed);
            if (names <= 1)
                return Make(ctx, OperationStatus.Rejected,
                    $"\"{name}\" is the only name of this file's data, so deleting it would lose the file. It was left in place.", path, null);
            try { File.Delete(trimmed); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Make(ctx, OperationStatus.Failed, $"Couldn't remove \"{name}\": {ex.Message}", path, null);
            }
        }

        return TryGetAttributes(trimmed, out _)
            ? Make(ctx, OperationStatus.Failed, $"\"{name}\" is still there after the unlink.", path, null)
            : Make(ctx, OperationStatus.Succeeded, "", path, null);
    }

    private const long MaxShortcutBytes = 1024 * 1024;

    private static FileOperationResult DoDeleteRacksShortcut(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source, folder = i.Destination ?? "";
        var vp = PathPolicy.ValidateFullPath(path);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, path, null);
        var vf = PathPolicy.ValidateFullPath(folder);
        if (!vf.Ok) return Make(ctx, OperationStatus.Rejected, "Folder: " + vf.Reason, path, null);

        string trimmed = Path.TrimEndingDirectorySeparator(path);
        string name = Path.GetFileName(trimmed);
        string ext = Path.GetExtension(name);
        if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is not a shortcut (.lnk or .url), so it was left in place.", path, null);

        if (!TryGetAttributes(trimmed, out FileAttributes attrs))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" no longer exists.", path, null);
        if ((attrs & FileAttributes.Directory) != 0)
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a folder, not a shortcut file.", path, null);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a link, not a shortcut file. Use Unlink for links.", path, null);

        // It must sit directly in the folder the caller named, and that folder must not be a protected one
        // (this cannot be used to clean out the Desktop or Documents).
        string expected = SafeMove.Canonicalize(Path.TrimEndingDirectorySeparator(folder));
        string actual = SafeMove.Canonicalize(Path.GetDirectoryName(trimmed) ?? "");
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is not in the expected folder, so it was left in place.", path, null);
        if (SafeMove.IsProtectedPath(folder, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"The folder is a {kind} — refusing to delete shortcuts there.", path, null);

        if (new FileInfo(trimmed).Length > MaxShortcutBytes)
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is too big to be a shortcut, so it was left in place.", path, null);

        try { File.Delete(trimmed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Make(ctx, OperationStatus.Failed, $"Couldn't delete the shortcut \"{name}\": {ex.Message}", path, null);
        }
        return File.Exists(trimmed)
            ? Make(ctx, OperationStatus.Failed, $"\"{name}\" is still there after the delete.", path, null)
            : Make(ctx, OperationStatus.Succeeded, "", path, null);
    }

    // ---- CreateDirectory -------------------------------------------------------------------

    private static FileOperationResult DoCreateDirectory(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var vp = PathPolicy.ValidateFullPath(path);
        if (!vp.Ok) return Make(ctx, OperationStatus.Rejected, vp.Reason, path, null);

        string trimmed = Path.TrimEndingDirectorySeparator(path);
        string leaf = Path.GetFileName(trimmed);
        var vl = PathPolicy.ValidateLeafName(leaf);
        if (!vl.Ok) return Make(ctx, OperationStatus.Rejected, vl.Reason, path, null);

        string? parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return Make(ctx, OperationStatus.Rejected, "The parent folder doesn't exist.", path, null);

        if (Directory.Exists(trimmed)) return Make(ctx, OperationStatus.Skipped, $"\"{leaf}\" already exists.", path, null);
        if (File.Exists(trimmed)) return Make(ctx, OperationStatus.Rejected, $"A file named \"{leaf}\" already exists.", path, null);

        Directory.CreateDirectory(trimmed);
        return Directory.Exists(trimmed)
            ? Make(ctx, OperationStatus.Succeeded, "", path, null)
            : Make(ctx, OperationStatus.Failed, "The folder was not created.", path, null);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    // Attributes of the item ITSELF (a link is not followed), so a dangling junction still "exists".
    private static bool TryGetAttributes(string path, out FileAttributes attrs)
    {
        try
        {
            attrs = File.GetAttributes(path);
            return true;
        }
        catch
        {
            attrs = 0;
            return false;
        }
    }

    private static FileOperationResult Make(OperationContext ctx, OperationStatus status, string reason, string? source, string? destination)
        => new(status, reason, source ?? "", destination, ctx.Id);

    private void WriteAudit(OperationContext ctx, FileOperationKind kind, FileOperationResult result)
    {
        try
        {
            _audit.Write(new AuditEvent(ctx.Id, ctx.TimestampUtc, kind.ToString(), result.Source, result.Destination,
                ctx.Reason, result.Status.ToString(), result.Reason));
        }
        catch
        {
            // A broken audit sink must never change the outcome of the operation it describes.
        }
    }
}
