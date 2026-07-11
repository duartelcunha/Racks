using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Racks.Util
{
    // Recursive directory deletion that does NOT follow reparse points.
    //
    // Directory.Delete(path, recursive: true) traverses into junctions and
    // symbolic links and deletes the target's contents — catastrophic when a
    // virtual rack contains a junction pointing at the user's Desktop folder.
    // This walker enumerates entries manually, unlinks reparse points without
    // descending, and only recurses into real subdirectories.
    public static class SafeDelete
    {
        public static void DeleteDirectoryRecursive(string path)
        {
            DirectoryInfo root;
            try { root = new DirectoryInfo(path); if (!root.Exists) return; }
            catch { return; }
            DeleteDirectoryRecursive(root);
        }

        private static void DeleteDirectoryRecursive(DirectoryInfo dir)
        {
            // Classify from the attributes captured DURING enumeration (a single filesystem op),
            // not a separate stat that an attacker/sync-client could race by swapping a real
            // subdirectory for a junction in between. If this dir is a reparse point (junction/
            // symlink), unlink it without descending so the target's contents are never deleted.
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                try { dir.Delete(recursive: false); } catch { }
                return;
            }

            IEnumerable<DirectoryInfo> subDirs;
            try { subDirs = dir.EnumerateDirectories(); }
            catch { subDirs = Array.Empty<DirectoryInfo>(); }
            foreach (var sub in subDirs) DeleteDirectoryRecursive(sub);

            IEnumerable<FileInfo> files;
            try { files = dir.EnumerateFiles(); }
            catch { files = Array.Empty<FileInfo>(); }
            foreach (var file in files)
            {
                try
                {
                    // Clear read-only so Delete can actually remove it.
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    file.Delete();
                }
                catch { }
            }

            try { dir.Delete(recursive: false); } catch { }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;   // send to Recycle Bin instead of permanent delete
        private const ushort FOF_NOERRORUI = 0x0400;

        // Send a file or folder to the Recycle Bin (recoverable), silently. Used where the app
        // removes something the user could conceivably want back, so a mistake or a mid-operation
        // race is never a permanent loss. Returns true on success.
        public static bool ToRecycleBin(string path)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return false;
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0", // pFrom is a double-null-terminated list
                    fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
                };
                return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }
    }
}
