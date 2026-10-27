using System.IO;
using Racks.Infrastructure.FileSystem;

namespace Racks.Infrastructure.Layout;

/// <summary>How an atomic write ended. <see cref="Error"/> is a sentence for the user when <see cref="Ok"/> is false.</summary>
public readonly record struct LayoutWriteResult(bool Ok, string Error)
{
    public static LayoutWriteResult Success => new(true, "");
    public static LayoutWriteResult Fail(string error) => new(false, error);
}

/// <summary>
/// Writes a whole new file in one step, so a failure never leaves a half-written file where a good one was (P9: the layout
/// export and the recovery file). The bytes go to a temp file in the SAME folder as the destination (same volume, so the swap is
/// a rename), are flushed to disk, and then one <c>File.Move(temp, destination, overwrite: true)</c> puts them in place, whether
/// the destination exists or not (<c>File.Replace</c> is NOT used: it can fail half way, with the old file gone and the new one
/// not yet in place). The temp name is short and random (<c>.racks-&lt;12 hex&gt;.tmp</c>), so a destination name of 255
/// characters still works.
/// <para>If the move fails the temp file is deleted ONLY when the destination is confirmed as it was before (same size and
/// last-write time, or absent when it was absent). If the old destination is gone or changed, the temp file is kept, because
/// it holds the only copy of the new content, and its path is in the error.</para>
/// The destination goes through <see cref="PathPolicy.ValidateFullPath"/> first; the folder must already exist (nothing is
/// created here). It never deletes or replaces anything except that destination (which the dialog already asked the user to
/// confirm) and its own temp file.
/// </summary>
public static class LayoutFileWriter
{
    public static LayoutWriteResult Write(string destination, byte[] content)
    {
        var check = PathPolicy.ValidateFullPath(destination);
        if (!check.Ok) return LayoutWriteResult.Fail(check.Reason);

        string? folder;
        string fileName;
        try
        {
            string full = Path.GetFullPath(destination);
            folder = Path.GetDirectoryName(full);
            fileName = Path.GetFileName(full);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName)) return LayoutWriteResult.Fail("The path does not name a file.");
            if (!Directory.Exists(folder)) return LayoutWriteResult.Fail("The folder does not exist.");
            destination = full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return LayoutWriteResult.Fail(ex.Message);
        }

        string temp = Path.Combine(folder, ".racks-" + Guid.NewGuid().ToString("N")[..12] + ".tmp");
        bool tempCreated = false;
        (bool Existed, long Length, DateTime LastWriteUtc) before = default;
        try
        {
            before = Fingerprint(destination);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                tempCreated = true;
                stream.Write(content, 0, content.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, destination, overwrite: true);
            return LayoutWriteResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            if (!tempCreated) return LayoutWriteResult.Fail(ex.Message);
            if (DestinationIntact(destination, before))
            {
                try { File.Delete(temp); } catch { /* best effort: the temp name is random and hidden by its dot */ }
                return LayoutWriteResult.Fail(ex.Message);
            }
            return LayoutWriteResult.Fail($"{ex.Message} The previous file at that location may have been lost; the new layout was kept at {temp}.");
        }
    }

    private static (bool Existed, long Length, DateTime LastWriteUtc) Fingerprint(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (true, info.Length, info.LastWriteTimeUtc) : (false, 0, default);
    }

    private static bool DestinationIntact(string path, (bool Existed, long Length, DateTime LastWriteUtc) before)
    {
        try
        {
            var now = Fingerprint(path);
            return now.Existed == before.Existed && now.Length == before.Length && now.LastWriteUtc == before.LastWriteUtc;
        }
        catch
        {
            return false;   // cannot tell: keep the temp file
        }
    }
}
