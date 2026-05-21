using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Racks.Util
{
    // Guard-rail wrapper around File.Move / Directory.Move. The point isn't to
    // do anything fancy — it's to catch the catastrophic "user dropped their
    // Documents folder into a rack" class of mistake BEFORE NTFS happily renames
    // a critical system folder into %AppData%\Racks\VirtualFrames\<guid>.
    //
    // Every drop path (Window_Drop, BootstrapAsVirtualRack, DesktopRouter
    // route-callback) should go through this. Callers should aggregate the
    // returned reasons across a multi-file drop and surface them once.
    public static class SafeMove
    {
        public enum Result
        {
            Moved,      // Move (or copy+delete fallback) completed.
            Skipped,    // Name collision in the destination — no-op.
            Rejected,   // Refused on safety grounds. Reason is populated.
        }

        // Source paths that are NEVER safe to move regardless of the destination.
        // Drive roots, the user's shell-special folders, and Windows itself.
        private static readonly Environment.SpecialFolder[] _protectedSpecialFolders = new[]
        {
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonDocuments,
            Environment.SpecialFolder.Favorites,
            Environment.SpecialFolder.Startup,
            Environment.SpecialFolder.StartMenu,
            Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.SendTo,
        };

        public static Result TryMove(string src, string dest, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dest))
            {
                reason = "Empty source or destination path.";
                return Result.Rejected;
            }
            if (!File.Exists(src) && !Directory.Exists(src))
            {
                reason = $"\"{src}\" no longer exists.";
                return Result.Rejected;
            }

            if (IsProtectedPath(src, out string kind))
            {
                reason = $"\"{TryGetLeafName(src)}\" is a {kind} — refusing to move.";
                return Result.Rejected;
            }

            if (IsAncestorOrEqual(src, dest))
            {
                reason = $"Can't move \"{TryGetLeafName(src)}\" into itself.";
                return Result.Rejected;
            }

            // File-vs-folder collision: skip rather than throwing.
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                reason = $"\"{TryGetLeafName(dest)}\" already exists in the rack — skipping.";
                return Result.Skipped;
            }

            bool srcIsDir = Directory.Exists(src);
            try
            {
                if (srcIsDir) Directory.Move(src, dest);
                else File.Move(src, dest);
                return Result.Moved;
            }
            catch (IOException ex) when (IsCrossVolumeError(ex))
            {
                // Cross-volume Directory.Move throws ERROR_NOT_SAME_DEVICE (0x11)
                // before touching anything; cross-volume File.Move does the same.
                // Fall back to copy + delete so the user's gesture actually works.
                return CopyThenDelete(src, dest, srcIsDir, out reason);
            }
            catch (UnauthorizedAccessException ex)
            {
                reason = $"Permission denied moving \"{TryGetLeafName(src)}\": {ex.Message}";
                return Result.Rejected;
            }
            catch (Exception ex)
            {
                reason = $"Failed to move \"{TryGetLeafName(src)}\": {ex.Message}";
                return Result.Rejected;
            }
        }

        private static Result CopyThenDelete(string src, string dest, bool srcIsDir, out string reason)
        {
            reason = "";
            try
            {
                if (srcIsDir) CopyDirectory(src, dest);
                else File.Copy(src, dest, overwrite: false);
            }
            catch (Exception ex)
            {
                // Best-effort cleanup of a partial copy so we don't leave junk
                // behind in the destination.
                try
                {
                    if (srcIsDir && Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                    else if (!srcIsDir && File.Exists(dest)) File.Delete(dest);
                }
                catch { }
                reason = $"Cross-volume copy of \"{TryGetLeafName(src)}\" failed: {ex.Message}";
                return Result.Rejected;
            }

            try
            {
                if (srcIsDir) Directory.Delete(src, recursive: true);
                else File.Delete(src);
            }
            catch (Exception ex)
            {
                // Copy succeeded but source delete failed — the destination has
                // the data, so call it a Move but tell the user the original is
                // still there. They can clean up manually.
                reason = $"Copied \"{TryGetLeafName(src)}\" but couldn't delete the original: {ex.Message}";
                return Result.Moved;
            }
            return Result.Moved;
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
            }
            foreach (var subDir in Directory.GetDirectories(src))
            {
                CopyDirectory(subDir, Path.Combine(dest, Path.GetFileName(subDir)));
            }
        }

        private static bool IsProtectedPath(string path, out string kind)
        {
            kind = "";
            string full;
            try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return false; }

            // Drive root: "C:" or "C:\".
            if (full.Length <= 3 && full.Length >= 2 && full[1] == ':')
            {
                kind = "drive root";
                return true;
            }

            foreach (var sf in _protectedSpecialFolders)
            {
                string sfPath;
                try { sfPath = Environment.GetFolderPath(sf); }
                catch { continue; }
                if (string.IsNullOrEmpty(sfPath)) continue;
                if (string.Equals(full, sfPath.TrimEnd(Path.DirectorySeparatorChar),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    kind = $"protected system folder ({sf})";
                    return true;
                }
            }

            // Downloads isn't in the SpecialFolder enum, so check the standard path.
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(profile, "Downloads");
                if (string.Equals(full, downloads.TrimEnd(Path.DirectorySeparatorChar),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    kind = "user folder (Downloads)";
                    return true;
                }
            }
            catch { }

            return false;
        }

        // True when dest is the same as src, or sits beneath src in the tree.
        // Prevents Directory.Move from being asked to move a folder into one of
        // its own children — which throws midway and leaves a half-moved tree.
        private static bool IsAncestorOrEqual(string src, string dest)
        {
            try
            {
                string srcFull = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar);
                string destFull = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase)) return true;
                return destFull.StartsWith(srcFull + Path.DirectorySeparatorChar,
                                           StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ERROR_NOT_SAME_DEVICE = 0x11; HRESULT-wrapped = 0x80070011.
        private static bool IsCrossVolumeError(IOException ex)
        {
            return ex.HResult == unchecked((int)0x80070011);
        }

        private static string TryGetLeafName(string path)
        {
            try
            {
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch { return path; }
        }
    }
}
