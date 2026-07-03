using System;
using System.Runtime.InteropServices;

namespace Racks.Util
{
    // Opts the process into Windows' immersive dark mode so the native Win32
    // popup menus rendered by TrackPopupMenuEx (i.e. the shell context menu we
    // open via ShellContextMenu.cs) pick up dark colors when the system theme
    // is dark. Without this, the user gets a white-on-white menu floating over
    // an otherwise-dark UI.
    //
    // Uses the undocumented uxtheme ordinals — same path File Explorer itself
    // takes. Stable since Windows 10 1903 (build 18362). Wrapped in try/catch
    // so a future Windows build that shuffles ordinals only falls back to the
    // light default, never crashes.
    public static class DarkModeHelper
    {
        private enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,    // Use dark colors only when the system theme is dark.
            ForceDark = 2,
            ForceLight = 3,
        }

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(PreferredAppMode appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        public static void EnableForApp()
        {
            try
            {
                // AllowDark, not ForceDark — respect the user's system setting.
                // A user on a light theme keeps light menus.
                SetPreferredAppMode(PreferredAppMode.AllowDark);
                FlushMenuThemes();
            }
            catch
            {
                // Best-effort. If the ordinals ever shift, we silently fall back
                // to legacy light menus.
            }
        }
    }
}
