using System.IO;

namespace Racks.Util;

/// <summary>Where Racks keeps things on disk. Injectable so tests can use temporary folders.</summary>
public sealed record CleanupPaths(
    string Workspace,
    string VirtualFramesRoot,
    string Desktop,
    string MirrorRoot,
    string LibraryFile)
{
    public static CleanupPaths ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RacksWorkspace"),
        InstanceController.VirtualFramesRoot,
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        RackMirror.MirrorRoot,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Libraries", "DesktopWorkspace.library-ms"));
}

public sealed class CleanupReport
{
    public int Returned { get; internal set; }
    public int Unlinked { get; internal set; }
    public int Kept { get; internal set; }
    public List<string> KeptFolders { get; } = new();
    public List<string> Errors { get; } = new();

    public IEnumerable<string> Lines()
    {
        yield return $"returned={Returned}";
        yield return $"unlinked={Unlinked}";
        yield return $"kept={Kept}";
        foreach (var f in KeptFolders) yield return $"keptFolder={f}";
        foreach (var e in Errors) yield return "error=" + e.Replace('\r', ' ').Replace('\n', ' ');
    }

    public void WriteTo(string path) => File.WriteAllLines(path, Lines());
}

/// <summary>
/// Runs when Racks is uninstalled. Gives the user's files back instead of deleting them.
///
/// Safety rules (see docs/SECURITY-INVARIANTS.md, INV-UNINSTALL-1):
/// 1. Nothing that holds data which exists nowhere else is ever deleted. Only links (junctions,
///    symlinks, an extra name of a multi-linked file), empty folders and Racks' own files go.
/// 2. Nothing on the Desktop is overwritten. Name clashes get " (from Racks)", " (from Racks 2)"...
/// 3. Only same-volume renames. Anything that can't be renamed (other drive, locked, denied)
///    stays put, in a visible folder, and is counted as kept.
/// 4. Running it twice is harmless.
/// </summary>
public static class UninstallCleanup
{
    internal const string WorkspaceIni = "desktop.ini";
    internal const string WorkspaceIcon = ".RacksIcon.ico";
    internal const string KeptNoteName = "Racks - files not returned.txt";

    public static CleanupReport Run(
        CleanupPaths paths,
        IReadOnlyDictionary<string, string>? sandboxTitles = null,
        bool touchShell = false)
    {
        var report = new CleanupReport();

        // 1. Files parked in the workspace by "New rack" (desktop filter) racks.
        foreach (var entry in Entries(paths.Workspace))
        {
            if (IsRacksOwnedFile(Path.GetFileName(entry))) continue;
            ReturnToDesktop(entry, paths.Desktop, report);
        }

        // 2. Files moved into per-rack sandboxes under %AppData%\Racks\VirtualFrames.
        var leftoverSandboxes = new List<string>();
        foreach (var sandbox in Entries(paths.VirtualFramesRoot))
        {
            if (JunctionHelper.IsReparsePoint(sandbox) || !Directory.Exists(sandbox))
            {
                ReturnToDesktop(sandbox, paths.Desktop, report);
                continue;
            }
            foreach (var entry in Entries(sandbox))
                ReturnToDesktop(entry, paths.Desktop, report);
            if (!TryDeleteEmptyDirectory(sandbox)) leftoverSandboxes.Add(sandbox);
        }

        // 3. Sandboxes that still hold something move next to the workspace leftovers, named
        //    after their rack, so everything the user still has is in one visible place.
        foreach (var sandbox in leftoverSandboxes)
        {
            try
            {
                Directory.CreateDirectory(paths.Workspace);
                string title = TitleFor(sandbox, sandboxTitles);
                string dest = FreePath(paths.Workspace, RackMirror.Sanitize(title), isDirectory: true);
                Directory.Move(sandbox, dest);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{sandbox}: {ex.Message}");
                report.KeptFolders.Add(sandbox);
            }
        }
        TryDeleteEmptyDirectory(paths.VirtualFramesRoot);

        // 4. The workspace itself: gone if empty, otherwise a normal visible folder with a note.
        FinishWorkspace(paths.Workspace, report);

        // 5. Shell integration Racks created: Quick Access pin, mirror junctions, library.
        if (touchShell) RackMirror.UnpinFromQuickAccess();
        RackMirror.RemoveJunctions(paths.MirrorRoot);
        TryDeleteEmptyDirectory(paths.MirrorRoot);
        try { if (File.Exists(paths.LibraryFile)) File.Delete(paths.LibraryFile); }
        catch (Exception ex) { report.Errors.Add($"{paths.LibraryFile}: {ex.Message}"); }

        // 6. A force-killed Racks can leave the desktop icons hidden.
        if (touchShell)
        {
            try { Interop.SetDesktopIconsVisibility(true); }
            catch (Exception ex) { report.Errors.Add($"desktop icons: {ex.Message}"); }
        }

        return report;
    }

    private static void ReturnToDesktop(string entry, string desktop, CleanupReport report)
    {
        string name = Path.GetFileName(entry);
        try
        {
            // A junction or symlink is only a pointer (Link on drop). The data lives at the target.
            if (JunctionHelper.IsReparsePoint(entry))
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: false);
                else File.Delete(entry);
                report.Unlinked++;
                return;
            }

            bool isDirectory = Directory.Exists(entry);

            // A hard link with another name elsewhere: the data survives under that name.
            if (!isDirectory && HardlinkHelper.GetLinkCount(entry) > 1)
            {
                File.Delete(entry);
                report.Unlinked++;
                return;
            }

            if (!SameVolume(entry, desktop))
            {
                Keep(report, $"{name}: on a different drive than the Desktop, left in place.");
                return;
            }

            Directory.CreateDirectory(desktop);
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string dest = Path.Combine(desktop, CandidateName(name, isDirectory, attempt));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                try
                {
                    if (isDirectory) Directory.Move(entry, dest);
                    else File.Move(entry, dest, overwrite: false);
                    report.Returned++;
                    return;
                }
                catch (IOException) when (File.Exists(dest) || Directory.Exists(dest))
                {
                    // Something took the name between the check and the move. Try the next one.
                }
            }
            Keep(report, $"{name}: no free name on the Desktop.");
        }
        catch (Exception ex)
        {
            Keep(report, $"{name}: {ex.Message}");
        }
    }

    private static void Keep(CleanupReport report, string why)
    {
        report.Kept++;
        report.Errors.Add(why);
    }

    internal static string CandidateName(string name, bool isDirectory, int attempt)
    {
        if (attempt == 0) return name;
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string ext = isDirectory ? "" : Path.GetExtension(name);
        string suffix = attempt == 1 ? " (from Racks)" : $" (from Racks {attempt})";
        return stem + suffix + ext;
    }

    private static string FreePath(string parent, string name, bool isDirectory)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            string candidate = Path.Combine(parent, CandidateName(name, isDirectory, attempt));
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(parent, name + " " + Guid.NewGuid().ToString("N"));
    }

    private static void FinishWorkspace(string workspace, CleanupReport report)
    {
        if (!Directory.Exists(workspace)) return;

        var remaining = Entries(workspace)
            .Where(e => !IsRacksOwnedFile(Path.GetFileName(e)) && Path.GetFileName(e) != KeptNoteName)
            .ToList();

        foreach (var own in new[] { WorkspaceIni, WorkspaceIcon })
        {
            string p = Path.Combine(workspace, own);
            try
            {
                if (File.Exists(p))
                {
                    File.SetAttributes(p, FileAttributes.Normal);
                    File.Delete(p);
                }
            }
            catch { }
        }

        try { new DirectoryInfo(workspace).Attributes = FileAttributes.Directory; }
        catch { }

        if (remaining.Count == 0)
        {
            try { File.Delete(Path.Combine(workspace, KeptNoteName)); } catch { }
            TryDeleteEmptyDirectory(workspace);
            return;
        }

        report.KeptFolders.Add(workspace);
        try
        {
            File.WriteAllText(Path.Combine(workspace, KeptNoteName),
                "Racks was uninstalled. Your files from racks were moved back to the Desktop.\r\n" +
                "The items in this folder could not be moved back safely (for example a file was\r\n" +
                "open, or it is on a different drive). Nothing was deleted. You can move them\r\n" +
                "yourself and then delete this folder.\r\n");
        }
        catch { }
    }

    private static bool IsRacksOwnedFile(string name) =>
        string.Equals(name, WorkspaceIni, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, WorkspaceIcon, StringComparison.OrdinalIgnoreCase);

    private static string TitleFor(string sandbox, IReadOnlyDictionary<string, string>? titles)
    {
        if (titles != null)
        {
            string key = Normalize(sandbox);
            foreach (var (folder, title) in titles)
                if (Normalize(folder) == key && !string.IsNullOrWhiteSpace(title)) return title;
        }
        return Path.GetFileName(sandbox);
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
        catch { return path.ToUpperInvariant(); }
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static List<string> Entries(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).ToList() : new(); }
        catch { return new(); }
    }

    private static bool TryDeleteEmptyDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return true;
            if (Directory.EnumerateFileSystemEntries(dir).Any()) return false;
            new DirectoryInfo(dir).Attributes = FileAttributes.Directory;
            Directory.Delete(dir, recursive: false);
            return true;
        }
        catch { return false; }
    }
}
