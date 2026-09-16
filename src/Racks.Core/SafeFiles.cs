using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;

namespace Racks.Core;

public static class SafeFiles
{
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public static bool IsLeafName(string? name) => !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\') &&
        name == name.TrimEnd(' ', '.') && !Path.IsPathRooted(name);

    public static bool IsWithin(string path, string root)
    {
        var p = CanonicalPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var r = CanonicalPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return p.Equals(r, PathComparison) || p.StartsWith(r + Path.DirectorySeparatorChar, PathComparison);
    }

    public static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return full;
        // Resolve short names and substituted drives before applying safety boundaries.
        // Missing destinations inherit the identity of their nearest existing parent.
        if (!Exists(full))
        {
            var parent = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(parent) ? full : Path.Combine(CanonicalPath(parent), Path.GetFileName(full));
        }
        using var handle = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot verify this location. Use Explorer for this operation.");
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Cannot verify this location. Use Explorer for this operation.");
        var result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + result[8..];
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    public static void RejectLinkAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("This location contains a symbolic link, junction, or cloud placeholder. Use Explorer/Finder for this operation.");
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
    }

    public static void ValidateMove(string source, string destination, IEnumerable<string>? protectedPaths = null)
    {
        if (!Path.IsPathFullyQualified(source) || !Path.IsPathFullyQualified(destination))
            throw new IOException("File locations must be absolute.");
        if (!Exists(source)) throw new FileNotFoundException("The source is unavailable.", source);
        RejectLinkAncestors(source);
        RejectLinkAncestors(Path.GetDirectoryName(destination)!);
        var full = CanonicalPath(source).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Equals(Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
            throw new IOException("A drive root cannot be moved.");
        var protectedLocations = Enum.GetValues<Environment.SpecialFolder>()
            .Select(Environment.GetFolderPath).Where(x => !string.IsNullOrEmpty(x))
            .Concat(protectedPaths ?? Array.Empty<string>())
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        if (protectedLocations.Any(p => IsWithin(p, full)))
            throw new IOException("A system or application folder cannot be moved.");
        if (IsWithin(destination, source)) throw new IOException("A folder cannot be moved into itself.");
        if (Exists(destination)) throw new IOException("The destination already exists.");
    }

    public static string UniqueDestination(string folder, string name)
    {
        if (!IsLeafName(name)) throw new IOException("Use a plain filename without path separators.");
        var destination = Path.Combine(folder, name);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var i = 2; Exists(destination); i++) destination = Path.Combine(folder, $"{stem} ({i}){extension}");
        return destination;
    }

    public static void MoveNoReplace(string source, string destination)
    {
        // Never implement a move as copy + delete. The OS either renames the item,
        // or the source stays intact. Cross-volume moves are deliberately delegated
        // to Explorer/Finder until a verified native transfer implementation exists.
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(source, destination, 0))
                throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message + " Use Explorer for transfers between volumes.");
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (renamex_np(source, destination, 4 /* RENAME_EXCL */) != 0)
                throw new IOException(new Win32Exception(Marshal.GetLastPInvokeError()).Message + " Use Finder for transfers between volumes.");
        }
        else
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination, false);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileEx(string source, string destination, uint flags);
    [DllImport("libc", SetLastError = true)] private static extern int renamex_np(string source, string destination, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
