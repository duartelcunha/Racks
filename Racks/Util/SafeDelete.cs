using System;
using System.IO;

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
    }
}
