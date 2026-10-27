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

        return MoveChecked(ctx, src, dest, i.SameVolumeRenameOnly);
    }

    // Preflight with SafeMove's own rules (so the reasons users see are unchanged), then the move, then
    // a look at the disk. After a clean preflight, a SafeMove "Rejected" is an I/O failure, hence Failed.
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

        var moved = SafeMove.TryMove(src, dest, out reason, allowCrossVolumeCopy: !sameVolumeOnly);
        return moved switch
        {
            SafeMove.Result.Moved => ConfirmMoved(ctx, src, dest, reason),
            SafeMove.Result.Skipped => Make(ctx, OperationStatus.Skipped, reason, src, dest),
            _ => Make(ctx, OperationStatus.Failed, reason, src, dest),
        };
    }

    private static FileOperationResult ConfirmMoved(OperationContext ctx, string src, string dest, string moveReason)
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

    private static FileOperationResult DoRename(OperationContext ctx, FileOperationIntent i)
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
        if (SafeMove.IsProtectedPath(current, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{Path.GetFileName(current)}\" is a {kind} — refusing to rename.", existing, target);

        if (string.Equals(current, target, StringComparison.Ordinal))
            return Make(ctx, OperationStatus.Skipped, "The item already has that name.", existing, target);
        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            return RenameCaseOnly(ctx, existing, current, dir, leaf, target);

        if (Exists(target))
            return Make(ctx, OperationStatus.Skipped, $"\"{leaf}\" already exists.", existing, target);

        return MoveChecked(ctx, current, target, sameVolumeOnly: true);
    }

    // "photo.jpg" -> "Photo.jpg": the destination "exists" (it is the same item), so a plain move is
    // refused, and Directory.Move throws when the paths differ only by case. Go through a temporary
    // name in the same folder; if the second step fails, put the first one back.
    private static FileOperationResult RenameCaseOnly(OperationContext ctx, string existing, string current, string dir, string leaf, string target)
    {
        string temp = Path.Combine(dir, ".racks-rename-" + ctx.Id);

        if (SafeMove.TryMove(current, temp, out string reason, allowCrossVolumeCopy: false) != SafeMove.Result.Moved)
            return Make(ctx, OperationStatus.Failed, reason, existing, target);

        if (SafeMove.TryMove(temp, target, out reason, allowCrossVolumeCopy: false) != SafeMove.Result.Moved)
        {
            if (SafeMove.TryMove(temp, current, out _, allowCrossVolumeCopy: false) == SafeMove.Result.Moved)
                return Make(ctx, OperationStatus.Failed, reason, existing, target);
            return Make(ctx, OperationStatus.Failed,
                $"{reason} The item was left under the temporary name \"{Path.GetFileName(temp)}\".", existing, temp);
        }

        // Exists() is case-insensitive; ask the folder for the spelling it actually stores.
        bool spelled = Directory.EnumerateFileSystemEntries(dir, leaf)
            .Any(e => string.Equals(Path.GetFileName(e), leaf, StringComparison.Ordinal));
        return spelled
            ? Make(ctx, OperationStatus.Succeeded, "", existing, target)
            : Make(ctx, OperationStatus.Failed, "The rename could not be confirmed.", existing, target);
    }

    // ---- Delete ----------------------------------------------------------------------------

    private static FileOperationResult DoRecycle(OperationContext ctx, FileOperationIntent i)
    {
        string path = i.Source;
        var rejected = CheckDeletable(ctx, path, requireDirectory: false);
        if (rejected != null) return rejected;

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
        if (SafeMove.IsProtectedPath(path, out string kind))
            return Make(ctx, OperationStatus.Rejected, $"\"{name}\" is a {kind} — refusing to delete.", path, null);
        return null;
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
