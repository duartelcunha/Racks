using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Racks.Infrastructure.Update;

/// <summary>
/// Removes the download folders that earlier updates left behind. The installer runs from its download folder, so that
/// folder cannot be deleted while it runs; it is removed later instead. Only our own "Racks-update-&lt;32 lowercase hex&gt;"
/// folders directly in the temp root, never a reparse point, never one with sub-folders, and only when it holds plain files.
/// A folder still in use stays for the next time: a locked file cannot be deleted, and a folder younger than
/// <see cref="DefaultMinAge"/> is skipped, so the empty folder of a download that is just starting (created before its file)
/// is never removed from under it.
/// </summary>
internal static class UpdateCleanupService
{
    public static readonly TimeSpan DefaultMinAge = TimeSpan.FromMinutes(10);

    private static readonly Regex UpdateDirName = new(
        @"\ARacks-update-[0-9a-f]{32}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool IsUpdateDirName(string name) => UpdateDirName.IsMatch(name);

    /// <returns>How many folders were removed.</returns>
    internal static int CleanupOldUpdateDirs(string? tempRoot = null, TimeSpan? minAge = null, DateTime? nowUtc = null)
    {
        int removed = 0;
        var age = minAge ?? DefaultMinAge;
        var now = nowUtc ?? DateTime.UtcNow;
        try
        {
            var root = new DirectoryInfo(tempRoot ?? Path.GetTempPath());
            foreach (var d in root.EnumerateDirectories("Racks-update-*"))
            {
                if (!UpdateDirName.IsMatch(d.Name)) continue;
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                // Possibly a download in progress (this process or another instance): leave it alone.
                var touched = d.CreationTimeUtc > d.LastWriteTimeUtc ? d.CreationTimeUtc : d.LastWriteTimeUtc;
                if (now - touched < age) continue;
                if (d.EnumerateDirectories().Any()) continue;
                if (d.EnumerateFiles().Any(f => (f.Attributes & FileAttributes.ReparsePoint) != 0)) continue;
                TryDeleteDir(d.FullName);
                if (!Directory.Exists(d.FullName)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Debug.WriteLine($"Update folder cleanup: {ex.Message}");
        }
        return removed;
    }

    /// <summary>Our own download folder holds one file and no subfolders: delete that, then the empty folder. Never throws.</summary>
    internal static void TryDeleteDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
            Directory.Delete(dir);
        }
        catch { }
    }
}
