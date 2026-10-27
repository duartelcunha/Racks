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
/// export). The bytes go to a temp file in the SAME folder as the destination (same volume, so the swap is a rename), are
/// flushed to disk, and only then replace the destination (<c>File.Replace</c> when it exists, <c>File.Move</c> when it does not).
/// If anything fails the temp file is deleted and the destination is exactly as it was. The destination goes through
/// <see cref="PathPolicy.ValidateFullPath"/> first; the folder must already exist (nothing is created).
/// <para>This writes a file the user just named in a dialog, with content Racks produced. It never deletes or replaces anything
/// except that destination (which the dialog already asked the user to confirm) and its own temp file.</para>
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

        string temp = Path.Combine(folder, "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content, 0, content.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
            return LayoutWriteResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            try { File.Delete(temp); } catch { /* best effort: the temp name is unique, so nothing else can collide with a leftover */ }
            return LayoutWriteResult.Fail(ex.Message);
        }
    }
}
