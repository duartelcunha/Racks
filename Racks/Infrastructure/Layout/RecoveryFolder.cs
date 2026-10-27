using System.IO;

namespace Racks.Infrastructure.Layout;

/// <summary>
/// Where a copy of the user's racks goes when a failed layout import could not put them back (P9 review): a folder of
/// <c>layout-&lt;timestamp&gt;.json</c> files, the newest <see cref="KeepNewest"/> kept. Each file is written with
/// <see cref="LayoutFileWriter"/> (atomic), so it is whole or absent. Racks-owned state under <c>%AppData%\Racks</c>; the only
/// files ever deleted here are older recovery files of this same folder and name pattern.
/// </summary>
public static class RecoveryFolder
{
    public const int KeepNewest = 5;
    public const string FilePattern = "layout-*.json";

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Racks", "recovery");

    /// <summary>Writes <paramref name="content"/> as a new recovery file. Never throws; <c>Path</c> is the file, or <c>Error</c> says why not.</summary>
    public static (string? Path, string? Error) Save(string directory, byte[] content, DateTime nowUtc, int keep = KeepNewest)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"layout-{nowUtc:yyyyMMdd-HHmmss-fff}.json");
            var written = LayoutFileWriter.Write(path, content);
            if (!written.Ok) return (null, written.Error);
            Prune(directory, keep);
            return (path, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    // The names sort by time, so everything after the newest `keep` is older.
    private static void Prune(string directory, int keep)
    {
        try
        {
            var old = Directory.EnumerateFiles(directory, FilePattern).OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(keep);
            foreach (var file in old)
            {
                try { File.Delete(file); } catch { /* an old recovery file that stays is harmless */ }
            }
        }
        catch
        {
            // pruning is housekeeping; the new file is already safe
        }
    }
}
