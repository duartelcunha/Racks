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

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        // Number of directory entries (hard links) that point at this file's data. 1 for a normal
        // file. Returns 0 if it can't be determined, so callers must treat 0 as "unknown, assume 1".
        public static int GetLinkCount(string path)
        {
            try
            {
                using var handle = System.IO.File.OpenHandle(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                return GetFileInformationByHandle(handle, out var info) ? (int)info.NumberOfLinks : 0;
            }
            catch
            {
                return 0;
            }
        }

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
