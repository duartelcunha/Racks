namespace Racks.UseCases.Startup;

/// <summary>What this process was started to do (decided by the first command-line argument).</summary>
public enum StartupMode
{
    /// <summary>The normal tray app: migrations, single instance, workspace, main window.</summary>
    Normal,
    /// <summary><c>--uninstall-cleanup</c>: headless, gives the rack files back and removes what Racks added to the shell.</summary>
    UninstallCleanup,
    /// <summary><c>--uninstall-anim</c>: only the goodbye animation, then exit.</summary>
    UninstallAnimation,
}
