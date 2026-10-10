using System.IO;
using Racks.Util;

namespace Racks.Rack;

/// <summary>
/// Gives one file a desktop rack owns back to the Desktop when the rack is removed. If the Desktop
/// already has that name the file used to be skipped and stayed in the hidden workspace, unseen. Now:
/// a Ctrl+drop link of the same file is just dropped (the Desktop name is the same data); anything else
/// comes back as "name (from Racks).ext", never overwriting.
/// </summary>
public static class WorkspaceReturn
{
    /// <summary>The path the item ended up at on the Desktop, or null if it could not be returned.</summary>
    public static string? ToDesktop(string workspaceDir, string desktopDir, string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || Path.GetFileName(fileName) != fileName) return null;
        string from = Path.Combine(workspaceDir, fileName);
        bool isDir = Directory.Exists(from);
        if (!isDir && !File.Exists(from)) return null;

        string wanted = Path.Combine(desktopDir, fileName);
        if (!isDir && File.Exists(wanted) && HardlinkHelper.GetLinkCount(from) > 1
            && new FileInfo(from).Length == new FileInfo(wanted).Length)
        {
            // Ctrl+drop left the original on the Desktop and a second name here: same data.
            try { File.Delete(from); return wanted; } catch { return null; }
        }

        string target = wanted;
        for (int attempt = 1; File.Exists(target) || Directory.Exists(target); attempt++)
        {
            if (attempt > 1000) return null;
            target = Path.Combine(desktopDir, UninstallCleanup.CandidateName(fileName, isDir, attempt));
        }
        return SafeMove.TryMove(from, target, out _) == SafeMove.Result.Moved ? target : null;
    }
}
