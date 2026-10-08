using System.IO;

namespace Racks.Util;

/// <summary>
/// Appends to crash.log with a size cap. The log used to grow without limit, and a bug that threw
/// on every mouse move could fill the disk over weeks.
/// </summary>
public static class CrashLog
{
    public const string FileName = "crash.log";
    public const long MaxBytes = 1_000_000;

    /// <summary>
    /// Appends <paramref name="entry"/> to crash.log in <paramref name="directory"/>. When the file
    /// would pass <paramref name="maxBytes"/> it is first moved to crash.log.1 (replacing an older
    /// one), so at most about two files of that size exist. Never throws.
    /// </summary>
    public static void Append(string directory, string entry, long maxBytes = MaxBytes)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);
            long incoming = System.Text.Encoding.UTF8.GetByteCount(entry);
            if (File.Exists(path) && new FileInfo(path).Length + incoming > maxBytes)
                File.Move(path, path + ".1", overwrite: true);
            File.AppendAllText(path, entry);
        }
        catch
        {
            // Logging is best-effort; it must never mask the original exception.
        }
    }
}
