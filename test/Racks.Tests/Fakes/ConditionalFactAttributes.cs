using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Racks.Tests.Fakes;

// xunit 2.9.3 has no Assert.Skip. A test that needs something the machine may not have (a second drive, the right
// to create symlinks, 8.3 names) is skipped with a clear message instead of returning early and showing green
// without having tested anything. Same idea as ReleaseGateFactAttribute. Probed once, when the tests are discovered.

public sealed class SecondFixedDriveFactAttribute : FactAttribute
{
    public SecondFixedDriveFactAttribute()
    {
        if (Find() == null) Skip = "Needs a second fixed drive (different from the temp folder's) and this machine has only one.";
    }

    public static DriveInfo? Find()
    {
        string? tempRoot = Path.GetPathRoot(Path.GetTempPath());
        return DriveInfo.GetDrives().FirstOrDefault(d =>
            d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.Name, tempRoot, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class SymlinkFactAttribute : FactAttribute
{
    public SymlinkFactAttribute()
    {
        if (!CanCreate()) Skip = "Needs the right to create symbolic links (Developer Mode or an elevated process).";
    }

    private static bool CanCreate()
    {
        string dir = Path.Combine(Path.GetTempPath(), "racks-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "t.txt");
            File.WriteAllText(target, "x");
            File.CreateSymbolicLink(Path.Combine(dir, "l.txt"), target);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}

public sealed class ShortNameFactAttribute : FactAttribute
{
    public ShortNameFactAttribute()
    {
        if (!Enabled()) Skip = "8.3 short names are turned off on the volume of the temp folder.";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    public static string ShortPath(string path)
    {
        var sb = new StringBuilder(1024);
        uint n = GetShortPathNameW(path, sb, (uint)sb.Capacity);
        return n == 0 || n > sb.Capacity ? path : sb.ToString();
    }

    private static bool Enabled()
    {
        string dir = Path.Combine(Path.GetTempPath(), "racks-8dot3-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            return !string.Equals(ShortPath(dir), dir, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally { try { Directory.Delete(dir); } catch { } }
    }
}

public sealed class SendToFolderFactAttribute : FactAttribute
{
    public SendToFolderFactAttribute()
    {
        if (!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.SendTo)))
            Skip = "This machine has no SendTo folder.";
    }
}
