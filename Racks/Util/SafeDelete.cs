using System;
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
            if (!Directory.Exists(path)) return;

            if (JunctionHelper.IsReparsePoint(path))
            {
                try { Directory.Delete(path, recursive: false); } catch { }
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                if (JunctionHelper.IsReparsePoint(dir))
                {
                    try { Directory.Delete(dir, recursive: false); } catch { }
                }
                else
                {
                    DeleteDirectoryRecursive(dir);
                }
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    // Clear read-only so File.Delete can actually remove it.
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    File.Delete(file);
                }
                catch { }
            }

            try { Directory.Delete(path, recursive: false); } catch { }
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
