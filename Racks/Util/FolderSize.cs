using System.IO;

namespace Racks.Util;

/// <summary>Sizes for the rack's size column.</summary>
public static class FolderSize
{
    /// <summary>
    /// Total size of the files under <paramref name="directory"/>. Reparse points (symlinks, junctions)
    /// are not followed: a link back to an ancestor would recurse forever, and disk-usage tools measure
    /// a folder's own contents the same way. Unreadable parts count as zero.
    /// </summary>
    public static long Compute(DirectoryInfo directory, CancellationToken token = default)
    {
        long size = 0;
        try
        {
            foreach (var file in directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                size += file.Length;
            }

            var subDirs = directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint));

            Parallel.ForEach(subDirs, subDir =>
            {
                token.ThrowIfCancellationRequested();
                Interlocked.Add(ref size, Compute(subDir, token));
            });
        }
        catch
        {
        }
        return size;
    }

    /// <summary>"1 234 KB": kilobytes rounded, a space between thousands, the same in every language.</summary>
    public static string Kilobytes(long bytes)
        => (bytes / 1024.0).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ") + " KB";
}
