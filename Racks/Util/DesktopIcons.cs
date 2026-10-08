namespace Racks.Util;

/// <summary>
/// Shows and hides the Windows desktop icons.
///
/// The desktop is a <c>SHELLDLL_DefView</c> window that holds the icon list (<c>SysListView32</c>).
/// Racks attaches every rack to that same DefView as a child window, so hiding the DefView hides
/// every rack too. "Hide desktop icons" therefore hides only the icon list.
/// </summary>
public static class DesktopIcons
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    // True while the icons are hidden because Racks hid them, so Racks only ever undoes its own
    // change: icons the user hid with Windows' own "Show desktop icons" option stay hidden.
    private static bool s_hiddenByRacks;

    /// <summary>The DefView window that hosts the icon list and the racks. Zero if Explorer is not running.</summary>
    public static IntPtr FindDefView()
    {
        IntPtr progman = Interop.FindWindow("Progman", null!);
        IntPtr defView = Interop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null!);
        if (defView != IntPtr.Zero) return defView;

        // With a wallpaper slideshow, the DefView lives under a WorkerW window instead.
        IntPtr workerW = IntPtr.Zero;
        do
        {
            workerW = Interop.FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null!);
            defView = Interop.FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null!);
        } while (workerW != IntPtr.Zero && defView == IntPtr.Zero);
        return defView;
    }

    /// <summary>The icon list itself (the part that holds the file icons, not the racks).</summary>
    public static IntPtr FindIconList()
    {
        IntPtr defView = FindDefView();
        return defView == IntPtr.Zero ? IntPtr.Zero : Interop.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
    }

    /// <summary>Hides or shows the desktop icons. Racks stay visible either way.</summary>
    public static void SetHidden(bool hidden)
    {
        try
        {
            EnsureDefViewVisible();
            IntPtr list = FindIconList();
            if (list == IntPtr.Zero) return;

            if (hidden)
            {
                // Only take ownership when the icons were visible, so Restore never un-hides icons
                // that Windows' own setting had hidden.
                if (Interop.IsWindowVisible(list)) s_hiddenByRacks = true;
                Interop.ShowWindow(list, SW_HIDE);
            }
            else
            {
                Interop.ShowWindow(list, SW_SHOW);
                s_hiddenByRacks = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DesktopIcons.SetHidden failed: {ex.Message}");
        }
    }

    /// <summary>Undoes a hide done by Racks (no-op if Racks did not hide them). Called when Racks exits.</summary>
    public static void RestoreIfHiddenByRacks()
    {
        if (s_hiddenByRacks) SetHidden(false);
    }

    /// <summary>
    /// Shows the icon list unconditionally. For the uninstaller, which cannot know whether a killed
    /// Racks had hidden them.
    /// </summary>
    public static void ForceShow()
    {
        s_hiddenByRacks = true;
        SetHidden(false);
    }

    /// <summary>
    /// Older versions hid the whole DefView, which also hid every rack, and a Racks that was killed
    /// (an update, a crash) left the desktop blank. Make sure it is visible again.
    /// </summary>
    public static void EnsureDefViewVisible()
    {
        IntPtr defView = FindDefView();
        if (defView != IntPtr.Zero && !Interop.IsWindowVisible(defView)) Interop.ShowWindow(defView, SW_SHOW);
    }
}
