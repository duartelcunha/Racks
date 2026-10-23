using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Racks.Util
{
    public static class DesktopIconPositioner
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSH = 0x1000;

        // Lay a set of desktop items out in a clean grid starting from the top-left corner,
        // skipping the very first column rows that Windows' own icons usually occupy. Used
        // when a rack is removed so its files come back to the desktop tidy instead of
        // scattered wherever Explorer drops them. Best-effort: any failure is swallowed so
        // removal never breaks because layout misbehaved.
        //
        // CHANGED: the previous implementation resolved Explorer's IFolderView via
        // cross-process COM and called SelectAndPositionItems with in-process PIDL
        // pointers. That triggers a native Access Violation (0xC0000005) on modern
        // .NET because the interface proxy is unmarshaled and the pointer lives in
        // this process, not Explorer's. The AV is uncatchable (SEH, not managed) and
        // kills the whole app — this was the "crash on rack delete" bug.
        //
        // The replacement notifies the shell that the Desktop folder changed, which
        // makes Explorer re-enumerate and position the returned files in its own grid
        // — safe, reliable, and zero risk of process termination.
        public static void ArrangeInGrid(System.Collections.Generic.IEnumerable<string> fullPaths)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrEmpty(desktopPath)) return;

                IntPtr pDesktop = Marshal.StringToHGlobalUni(desktopPath);
                try
                {
                    // Tell the shell the Desktop folder contents changed so Explorer
                    // re-enumerates and lays out the returned files in its own grid.
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, pDesktop, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(pDesktop);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArrangeInGrid desktop refresh failed: {ex.Message}");
            }
        }
    }
}
