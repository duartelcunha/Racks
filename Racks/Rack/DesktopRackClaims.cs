using System.IO;

namespace Racks.Rack;

/// <summary>
/// A desktop rack claims files by name (Instance.AssignedFiles) and keeps the real file in the
/// workspace. When the user drags an item out and Explorer MOVES it, nothing is left in the workspace,
/// so only the claim remains to be dropped. Telling "moved away" from "copied, still here" is the one
/// decision the drag-out handler must get right, so it lives here where it can be tested without a window.
/// </summary>
public static class DesktopRackClaims
{
    /// <summary>True when nothing exists at <paramref name="workspaceFullPath"/> any more (a file, a folder or a link).</summary>
    public static bool IsGone(string workspaceFullPath) =>
        !string.IsNullOrEmpty(workspaceFullPath)
        && !File.Exists(workspaceFullPath)
        && !Directory.Exists(workspaceFullPath);
}
