using System;
using System.Collections.Generic;
using Racks.Core;
using Racks.Util;

namespace Racks.Infrastructure.Shell;

/// <summary>
/// The one place where presentation code (windows, dialogs, App) reaches the Windows shell for
/// change notifications, desktop-icon handling and the Racks workspace folder. Presentation calls this
/// class only; <c>Interop.NotifyShell*</c>, <c>DesktopIcons</c>, <c>DesktopIconPositioner</c> and
/// <c>DesktopIconManager</c> are internal details behind it (a PresentationBoundaryTests guard enforces it).
///
/// Every method is a thin delegation: same calls, same threading (the caller's thread), same exception
/// handling as before the gateway existed. The notifications are best-effort and never throw; the others
/// let the same exceptions through that the underlying helper did.
///
/// Out of scope on purpose (they stay in presentation): the shell context menu, window geometry/style
/// Win32 calls, hotkeys, <c>Process.Start</c> launches and the tray.
/// </summary>
public sealed class ShellOperationGateway
{
    public static ShellOperationGateway Default { get; } = new(ShellBackend.Real);

    private readonly ShellBackend _backend;

    internal ShellOperationGateway(ShellBackend backend) => _backend = backend;

    /// <summary>Folder where files parked in desktop racks live (empty until <see cref="InitializeWorkspace"/> ran).</summary>
    public string WorkspacePath => _backend.GetWorkspacePath();

    /// <summary>Creates the workspace folder (icon, desktop library). Failures are swallowed inside, as before.</summary>
    public void InitializeWorkspace() => _backend.InitializeWorkspace();

    /// <summary>
    /// Tells Explorer that one file or folder was moved/renamed so an open view repaints at once. Best-effort:
    /// an empty path is ignored and nothing ever throws, because a failed notification must not fail the move.
    /// </summary>
    public void NotifyItemMoved(string oldPath, string newPath, bool isDirectory)
    {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;
        try { _backend.NotifyItemMoved(oldPath, newPath, isDirectory); }
        catch { /* best-effort, like Interop.NotifyShellMove */ }
    }

    /// <summary>Tells Explorer a folder's contents changed (call once per source parent after a batch). Empty path = no-op.</summary>
    public void NotifyDirectoryChanged(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        try { _backend.NotifyDirectoryChanged(folderPath); }
        catch { /* best-effort, like Interop.NotifyShellUpdateDir */ }
    }

    /// <summary>
    /// Asks Explorer to re-lay-out the Desktop after files were returned to it. The paths are not used to
    /// position anything (Explorer places them in its own grid); a failure is swallowed so removing a rack
    /// never breaks because of layout.
    /// </summary>
    public void ArrangeDesktopIcons(IEnumerable<string> returnedPaths)
    {
        try { _backend.ArrangeDesktopIcons(returnedPaths); }
        catch { /* best-effort, like DesktopIconPositioner.ArrangeInGrid */ }
    }

    /// <summary>Hides or shows the Windows desktop icons (racks stay visible). Swallows failures inside, as before.</summary>
    public void SetDesktopIconsHidden(bool hidden) => _backend.SetDesktopIconsHidden(hidden);

    /// <summary>Makes sure the desktop view is visible (an older or killed Racks may have left it hidden).</summary>
    public void EnsureDesktopVisible() => _backend.EnsureDesktopVisible();

    /// <summary>The desktop icon list window, or zero if Explorer is not running.</summary>
    public IntPtr FindDesktopIconList() => _backend.FindDesktopIconList();

    /// <summary>Undoes a hide done by Racks (no-op if Racks did not hide the icons). For exit.</summary>
    public void RestoreDesktopIconsIfHiddenByRacks() => _backend.RestoreDesktopIconsIfHiddenByRacks();
}

/// <summary>The real shell calls behind <see cref="ShellOperationGateway"/>; tests replace the delegates.</summary>
internal sealed class ShellBackend
{
    public Func<string> GetWorkspacePath { get; init; } = () => string.Empty;
    public Action InitializeWorkspace { get; init; } = () => { };
    public Action<string, string, bool> NotifyItemMoved { get; init; } = (_, _, _) => { };
    public Action<string> NotifyDirectoryChanged { get; init; } = _ => { };
    public Action<IEnumerable<string>> ArrangeDesktopIcons { get; init; } = _ => { };
    public Action<bool> SetDesktopIconsHidden { get; init; } = _ => { };
    public Action EnsureDesktopVisible { get; init; } = () => { };
    public Func<IntPtr> FindDesktopIconList { get; init; } = () => IntPtr.Zero;
    public Action RestoreDesktopIconsIfHiddenByRacks { get; init; } = () => { };

    public static ShellBackend Real { get; } = new()
    {
        GetWorkspacePath = () => DesktopIconManager.RacksWorkspacePath,
        InitializeWorkspace = DesktopIconManager.Initialize,
        NotifyItemMoved = Interop.NotifyShellMove,
        NotifyDirectoryChanged = Interop.NotifyShellUpdateDir,
        ArrangeDesktopIcons = DesktopIconPositioner.ArrangeInGrid,
        SetDesktopIconsHidden = DesktopIcons.SetHidden,
        EnsureDesktopVisible = DesktopIcons.EnsureDefViewVisible,
        FindDesktopIconList = DesktopIcons.FindIconList,
        RestoreDesktopIconsIfHiddenByRacks = DesktopIcons.RestoreIfHiddenByRacks,
    };
}
