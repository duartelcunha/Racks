using System;
using System.Runtime.InteropServices;

namespace Racks.Util
{
    // Thin wrapper around CreateHardLinkW. Used by the rack-drop path so that
    // dropping a file from Desktop into a virtual rack produces a real second
    // directory entry pointing at the same NTFS inode, NOT a .lnk shortcut.
    //
    // Why this matters: the user sees the file in the rack with its normal
    // icon (no overlay arrow, no .lnk extension). Deleting either entry leaves
    // the data alive until both are gone. Renaming one doesn't rename the other
    // — they're independent names for the same content.
    //
    // Limitations baked into NTFS, not our code:
    //   - Files only. Directories can't be hardlinked; caller falls back to .lnk.
    //   - Same volume only. Hardlinks across drives fail with ERROR_NOT_SAME_DEVICE.
    //   - Source must exist; destination must not.
    public static class HardlinkHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        public static bool TryCreate(string existingFile, string newLinkPath)
        {
            try
            {
                return CreateHardLinkW(newLinkPath, existingFile, IntPtr.Zero);
            }
            catch
            {
                return false;
            }
        }
    }
}
