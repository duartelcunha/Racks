using System.IO;

namespace Racks.Util
{
    // Creates the "reference" a Ctrl+drop leaves in a rack's folder: something the user perceives as
    // the file or folder itself, not a shortcut. Strategies, in order:
    //   1. .url                       -> copy as-is (already a reference file).
    //   2. File + same NTFS volume    -> hardlink. Same data, no .lnk, no arrow overlay; visible to
    //      file pickers under both names.
    //   3. Folder + same NTFS volume  -> directory junction. Looks like a real folder.
    //   4. Cross-volume / special FS  -> .lnk shortcut fallback.
    // The source is always left where it was. Returns the path that was actually created, because
    // callers have to claim exactly that name (a hardlink keeps the file name, a .lnk gains an extension).
    public static class ShortcutFactory
    {
        public static string Create(string source, string folder, string? destFileName = null)
        {
            string name = string.IsNullOrEmpty(destFileName) ? Path.GetFileName(source) : destFileName;
            string sameName = Path.Combine(folder, name);
            bool destExists = File.Exists(sameName) || Directory.Exists(sameName);

            if (Path.GetExtension(source).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                string copy = destExists ? UniquePath(sameName) : sameName;
                File.Copy(source, copy);
                return copy;
            }

            if (!destExists)
            {
                if (File.Exists(source) && HardlinkHelper.TryCreate(source, sameName)) return sameName;
                if (Directory.Exists(source) && JunctionHelper.TryCreate(source, sameName)) return sameName;
                // Hardlink / junction failed (cross-volume, ACL, race): fall through to a .lnk so the
                // gesture still produces something. A .lnk to a folder opens but can't be traversed by
                // Win32 file pickers, which is a worse experience; hopefully rare.
            }

            // Keep the full file name ("a.jpg.lnk") so a.jpg and a.png can't collide on "a.lnk".
            string shortcutPath = UniquePath(Path.Combine(folder, name + ".lnk"));
            ShellLinkHelper.Create(
                shortcutPath: shortcutPath,
                targetPath: source,
                workingDirectory: Path.GetDirectoryName(source) ?? folder,
                description: Path.GetFileName(source));
            return shortcutPath;
        }

        // "name.ext" -> "name (1).ext" -> "name (2).ext" ... like Explorer, never overwriting.
        public static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path)!;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }
    }
}
