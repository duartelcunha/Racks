using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Racks.Util;

namespace Racks.Tests.Adversarial;

/// <summary>
/// Shared set-up for the adversarial filesystem suite (P11): one throwaway tree under %TEMP%, real NTFS, real
/// junctions and hard links. Nothing here ever points at the user's real Desktop, Documents or AppData; a folder
/// that must look "protected" is declared through the per-thread <c>SafeMove.UseProtectedFoldersForTest</c> seam.
/// Cleanup unlinks every junction it made first, so a failing test can at worst remove a link, never a target.
/// </summary>
public abstract class AdversarialTree : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "racks-adv-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];
    private readonly List<string> _extendedPaths = [];

    protected AdversarialTree() => Directory.CreateDirectory(Root);

    public virtual void Dispose()
    {
        foreach (var link in _links) { try { Directory.Delete(link); } catch { } }
        foreach (var p in _extendedPaths) { try { Directory.Delete(@"\\?\" + p, recursive: true); } catch { } }
        ClearAttributes(Root);
        try { SafeDelete.DeleteDirectoryRecursive(Root); } catch { }
        try { Directory.Delete(Root, recursive: true); } catch { }
    }

    // A read-only or hidden folder blocks the cleanup of its parent; walk without following links and reset.
    private static void ClearAttributes(string dir)
    {
        try
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((e.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    e.Attributes = (e.Attributes & FileAttributes.Directory) != 0 ? FileAttributes.Directory : FileAttributes.Normal;
                if ((e.Attributes & FileAttributes.Directory) != 0) ClearAttributes(e.FullName);
            }
        }
        catch { }
    }

    protected string P(params string[] parts) => Path.Combine([Root, .. parts]);

    protected string Write(string path, string content = "data")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    protected string Dir(params string[] parts)
    {
        string p = P(parts);
        Directory.CreateDirectory(p);
        return p;
    }

    protected string Link(string target, string link)
    {
        Assert.True(JunctionHelper.TryCreate(target, link), "test setup: junction " + link);
        _links.Add(link);
        return link;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);

    /// <summary>
    /// A reparse point that is NOT a junction or symbolic link, standing in for a cloud placeholder (which a test
    /// cannot create): an empty file or folder carrying a made-up Microsoft-range tag with 4 bytes of data. It reports
    /// ReparsePoint, <see cref="JunctionHelper.IsLink"/> is false, and it can be renamed and removed like any entry.
    /// </summary>
    protected string NonLinkReparsePoint(string path, bool directory)
    {
        if (directory) Directory.CreateDirectory(path); else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, ""); }
        using (var h = CreateFileW(path, 0x40000000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero))
        {
            var buffer = new byte[12];
            BitConverter.GetBytes(0x80000F11u).CopyTo(buffer, 0);
            BitConverter.GetBytes((ushort)4).CopyTo(buffer, 4);
            Assert.True(!h.IsInvalid && DeviceIoControl(h, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero),
                "test setup: a non-link reparse point could not be created here");
        }
        Assert.True(JunctionHelper.IsReparsePoint(path) && !JunctionHelper.IsLink(path));
        return path;
    }

    /// <summary>Register a path that was created in the \\?\ spelling (trailing dot or space) for cleanup.</summary>
    protected void CleanLater(string normalPath) => _extendedPaths.Add(normalPath);

    /// <summary>Every file under <paramref name="dir"/> (links not followed) as relative path to content.</summary>
    protected static Dictionary<string, string> Snapshot(string dir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string d)
        {
            foreach (var e in new DirectoryInfo(d).EnumerateFileSystemInfos())
            {
                string rel = Path.GetRelativePath(dir, e.FullName);
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { result[rel] = "<link>"; continue; }
                if ((e.Attributes & FileAttributes.Directory) != 0) { result[rel + "\\"] = ""; Walk(e.FullName); }
                else result[rel] = File.ReadAllText(e.FullName);
            }
        }
        Walk(dir);
        return result;
    }
}
