using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Racks.Util
{
    // Guard-rail wrapper around File.Move / Directory.Move. The point isn't to
    // do anything fancy — it's to catch the catastrophic "user dropped their
    // Documents folder into a rack" class of mistake BEFORE NTFS happily renames
    // a critical system folder into %AppData%\Racks\VirtualFrames\<guid>.
    //
    // Every drop path (Window_Drop, BootstrapAsVirtualRack, DesktopRouter
    // route-callback) goes through Infrastructure.FileSystem.FileOperationBroker,
    // which is the only caller of this primitive (enforced by FileMutationGuardTests).
    // Callers aggregate the returned reasons across a multi-file drop and surface them once.
    internal static class SafeMove
    {
        public enum Result
        {
            Moved,      // Move (or copy+delete fallback) completed. A folder may carry a reason: its original could not be fully removed.
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

        // The preflight half of TryMove: null means "go ahead", anything else is the verdict with
        // its reason. Split out so the broker can tell a policy refusal (Rejected here) from an
        // I/O failure while moving (Rejected by TryMove).
        internal static Result? Check(string src, string dest, out string reason)
        {
            t_checkCalls++;
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

            if (IsProtectedOrAncestorPath(src, out string kind))
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
            return null;
        }

        // Test seam: how many times Check ran on this thread (it makes ~50 CreateFile calls, so the broker runs it once).
        [ThreadStatic] private static int t_checkCalls;
        internal static int CheckCallsOnThisThread => t_checkCalls;

        // allowCrossVolumeCopy: false refuses the copy+delete fallback, for callers that must only
        // ever rename (the uninstall path): a move that would need a copy is Rejected untouched.
        public static Result TryMove(string src, string dest, out string reason, bool allowCrossVolumeCopy = true)
        {
            var verdict = Check(src, dest, out reason);
            if (verdict != null) return verdict.Value;
            return MoveAfterCheck(src, dest, out reason, allowCrossVolumeCopy);
        }

        // The move itself, for a caller that already ran Check (the broker does, and adds its own checks after it,
        // so the preflight is paid once). A name that appeared since Check is not overwritten: File.Move and
        // Directory.Move refuse an existing destination, which then surfaces as a failure with the system's reason.
        internal static Result MoveAfterCheck(string src, string dest, out string reason, bool allowCrossVolumeCopy = true)
        {
            reason = "";
            bool srcIsDir = Directory.Exists(src);
            try
            {
                if (srcIsDir) Directory.Move(src, dest);
                else File.Move(src, dest);
                return Result.Moved;
            }
            catch (IOException ex) when (IsCrossVolumeError(ex) && !allowCrossVolumeCopy)
            {
                reason = $"\"{TryGetLeafName(src)}\" is on another volume and this operation may not copy — nothing was changed.";
                return Result.Rejected;
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

        // Test seam: runs on this thread right before CopyThenDelete creates anything, with (src, dest). It stands
        // for the other process that creates the destination between the preflight and the copy.
        [ThreadStatic] private static Action<string, string>? t_beforeCopyHook;

        internal static IDisposable UseBeforeCopyHookForTest(Action<string, string> hook)
        {
            t_beforeCopyHook = hook;
            return new BeforeCopyScope();
        }

        private sealed class BeforeCopyScope : IDisposable
        {
            public void Dispose() => t_beforeCopyHook = null;
        }

        internal static Result CopyThenDelete(string src, string dest, bool srcIsDir, out string reason)
        {
            reason = "";
            // Everything THIS call creates, in creation order. A failed copy removes exactly these and nothing
            // else: the destination can appear between the preflight and here, and whatever somebody else put
            // there is not ours to delete. Every name is created with "must not exist" semantics, so the list
            // only ever holds things that did not exist a moment before.
            var created = new List<(string Path, bool IsDirectory)>();
            try
            {
                t_beforeCopyHook?.Invoke(src, dest);
                if (srcIsDir) CopyDirectory(src, dest, created);
                else CopyFileNew(src, dest, created);
            }
            catch (Exception ex)
            {
                RemoveCreated(created);
                reason = $"Cross-volume copy of \"{TryGetLeafName(src)}\" failed: {ex.Message}";
                return Result.Rejected;
            }

            // Copy succeeded; remove the source. Use SafeDelete (never follows junctions) rather
            // than a raw recursive delete. A folder that can't be fully removed is already partly
            // gone, so the destination holds the only complete copy: keep it and say so. A single
            // file is untouched when its delete fails, so take the copy back out - otherwise the
            // file would exist twice and the rack would claim one of them.
            if (srcIsDir)
            {
                SafeDelete.DeleteDirectoryRecursive(src);
                if (Directory.Exists(src))
                    reason = $"Copied \"{TryGetLeafName(src)}\" but couldn't fully delete the original.";
            }
            else
            {
                try { File.Delete(src); }
                catch (Exception ex)
                {
                    RemoveCreated(created);
                    reason = $"Couldn't move \"{TryGetLeafName(src)}\" because the original is in use or protected ({ex.Message}). Nothing was changed.";
                    return Result.Rejected;
                }
            }
            return Result.Moved;
        }

        // Removes what a failed copy created, newest first, and only that: files by name, folders with a plain
        // (non-recursive) delete so a folder that has gained somebody else's content stays with it.
        internal static void RemoveCreated(IReadOnlyList<(string Path, bool IsDirectory)> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                var (path, isDirectory) = created[i];
                try
                {
                    if (isDirectory) Directory.Delete(path, recursive: false);
                    else
                    {
                        File.SetAttributes(path, FileAttributes.Normal); // a copied read-only file is ours to remove
                        File.Delete(path);
                    }
                }
                catch { /* best effort: a leftover is reported by the caller's failure, never hidden by deleting more */ }
            }
        }

        // Claims the name first (CreateNew: throws if anything is there, so nothing of ours exists yet), then copies
        // over the empty file we just made, which keeps File.Copy's handling of attributes, times and streams.
        private static void CopyFileNew(string src, string dest, List<(string Path, bool IsDirectory)> created)
        {
            using (new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            created.Add((dest, false));
            File.Copy(src, dest, overwrite: true);
        }

        private static void CopyDirectory(string src, string dest, List<(string Path, bool IsDirectory)> created, int depth = 0)
        {
            // Backstop against a junction loop (a link pointing at an ancestor) blowing the stack
            // or filling the disk before it aborts.
            if (depth > 64) throw new IOException("Directory nesting too deep (possible junction loop).");

            CreateNewDirectory(dest);
            created.Add((dest, true));
            var srcInfo = new DirectoryInfo(src);
            foreach (var file in srcInfo.EnumerateFiles())
            {
                CopyFileNew(file.FullName, Path.Combine(dest, file.Name), created);
            }
            foreach (var sub in srcInfo.EnumerateDirectories())
            {
                // Skip reparse points (junctions/symlinks): descending would copy the link
                // TARGET's whole tree instead of the link, and a link to an ancestor would recurse
                // until it aborts. A cross-volume move can't recreate a junction anyway - the link
                // isn't real data. Classified from the enumeration's own attributes.
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                CopyDirectory(sub.FullName, Path.Combine(dest, sub.Name), created, depth + 1);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(string lpPathName, IntPtr lpSecurityAttributes);
        private const int ERROR_ALREADY_EXISTS = 183;

        // Directory.CreateDirectory succeeds silently when the folder is already there, which would let a copy
        // merge into somebody else's folder. CreateDirectoryW reports ERROR_ALREADY_EXISTS instead. The parent
        // must exist (the broker checks that before any move). The extended-length spelling keeps long paths working.
        private static void CreateNewDirectory(string path)
        {
            string full = Path.GetFullPath(path);
            string extended = full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full
                : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full.Substring(2)
                : @"\\?\" + full;
            if (CreateDirectoryW(extended, IntPtr.Zero)) return;
            int error = Marshal.GetLastWin32Error();
            if (error == ERROR_ALREADY_EXISTS) throw new IOException($"The folder '{path}' already exists.");
            throw new IOException($"Couldn't create the folder '{path}': {new System.ComponentModel.Win32Exception(error).Message}");
        }

        internal static bool IsProtectedPath(string path, out string kind)
            => MatchProtected(Canonicalize(path), ProtectedRoots(), includeAncestors: false, out kind);

        // Like IsProtectedPath, but ALSO true for a path that CONTAINS a protected folder (an ancestor):
        // %USERPROFILE%\AppData, C:\Users, the OneDrive root when Desktop/Documents are redirected into it.
        // Moving or deleting such a folder takes the protected folder with it, so equality is not enough.
        internal static bool IsProtectedOrAncestorPath(string path, out string kind)
            => MatchProtected(Canonicalize(path), ProtectedRoots(), includeAncestors: true, out kind);

        // For operations that act on the item ITSELF and never on what it points to (renaming or
        // unlinking a junction or symlink): canonicalize the parent folder, keep the leaf as it is, and
        // compare that. A Ctrl+dropped reference to Documents is a link inside a rack and is not Documents;
        // the real Documents folder still matches.
        internal static bool IsProtectedLink(string path, out string kind)
            => MatchProtected(CanonicalizeLink(path), ProtectedRoots(), includeAncestors: false, out kind);

        // IsProtectedLink plus the ancestor rule of IsProtectedOrAncestorPath.
        internal static bool IsProtectedOrAncestorLink(string path, out string kind)
            => MatchProtected(CanonicalizeLink(path), ProtectedRoots(), includeAncestors: true, out kind);

        // The item's own path: the parent folder resolved, the leaf kept (a link is not followed). An 8.3
        // spelling of the leaf is expanded to the long name (GetLongPathName does not follow links).
        private static string CanonicalizeLink(string path)
        {
            string trimmed = Path.TrimEndingDirectorySeparator(path);
            string? parent = Path.GetDirectoryName(trimmed);
            string leaf = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return Canonicalize(path);
            string canonicalParent = Canonicalize(parent).TrimEnd(Path.DirectorySeparatorChar);
            return ExpandShortName(canonicalParent + Path.DirectorySeparatorChar + leaf);
        }

        private static string ExpandShortName(string path)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint n = GetLongPathNameW(path, sb, (uint)sb.Capacity);
                if (n > 0 && n < sb.Capacity) return StripPrefix(sb.ToString());
            }
            catch { }
            return path;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

        // Test seam: extra folders treated as protected on THIS thread only (the broker runs synchronously
        // on the caller's thread), so parallel tests cannot see each other's roots.
        [ThreadStatic] private static List<string>? t_extraProtectedFolders;

        internal static IDisposable UseProtectedFoldersForTest(params string[] folders)
        {
            t_extraProtectedFolders = [.. folders];
            return new ExtraRootsScope();
        }

        private sealed class ExtraRootsScope : IDisposable
        {
            public void Dispose() => t_extraProtectedFolders = null;
        }

        // The protected folders, already canonicalized, each with the label shown to the user.
        // Callers canonicalize (resolve 8.3 short names, \\?\ prefixes, subst drives, junctions and
        // symlinks) BEFORE comparing. A literal string compare could be bypassed by an alias of a
        // protected folder (an 8.3 name, or a junction whose target is the Desktop), letting SafeMove
        // move/delete the real protected folder. Limit: over SMB GetFinalPathNameByHandle returns the
        // UNC form, so an admin-share spelling of a local folder (\\localhost\C$\Users\me\Desktop) does
        // NOT match; the broker's PathPolicy refuses admin shares for that reason.
        //
        // Each folder is listed in every spelling a caller may compare against: fully resolved (what a
        // path-following check sees), with only its parent resolved and the leaf kept (what an item-itself
        // check sees: when Desktop/Documents/AppData were relocated with a junction, the junction's own path
        // is that form and the resolved form is its target), and as the raw string Windows reports.
        private static List<(string Path, string Kind)> ProtectedRoots()
        {
            var raw = new List<(string Path, string Kind)>();
            foreach (var sf in _protectedSpecialFolders)
            {
                string sfPath;
                try { sfPath = Environment.GetFolderPath(sf); }
                catch { continue; }
                if (string.IsNullOrEmpty(sfPath)) continue;
                raw.Add((sfPath, $"protected system folder ({sf})"));
            }

            // Downloads isn't in the SpecialFolder enum, so add the standard path.
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                raw.Add((Path.Combine(profile, "Downloads"), "user folder (Downloads)"));
            }
            catch { }

            if (t_extraProtectedFolders != null)
                foreach (var extra in t_extraProtectedFolders) raw.Add((extra, "protected test folder"));

            var roots = new List<(string Path, string Kind)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, kind) in raw)
            {
                foreach (var form in Spellings(path))
                    if (!string.IsNullOrEmpty(form) && seen.Add(kind + "|" + form)) roots.Add((form, kind));
            }
            return roots;
        }

        private static IEnumerable<string> Spellings(string path)
        {
            yield return Canonicalize(path);
            yield return CanonicalizeLink(path);
            string? full = null;
            try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); } catch { }
            if (full != null) yield return full;
        }

        // Pure (no I/O): is the canonical path <full> a drive root, equal to one of the canonical
        // <roots>, or (when asked) a folder that CONTAINS one of them? The containment test is a prefix
        // test on a trailing separator, so "C:\Users\a" is not an ancestor of "C:\Users\ab\Desktop".
        internal static bool MatchProtected(string full, IReadOnlyList<(string Path, string Kind)> roots, bool includeAncestors, out string kind)
        {
            kind = "";
            if (string.IsNullOrEmpty(full)) return false;

            // Drive root: "C:" or "C:\".
            if (full.Length <= 3 && full.Length >= 2 && full[1] == ':')
            {
                kind = "drive root";
                return true;
            }

            foreach (var (rootPath, rootKind) in roots)
            {
                if (string.IsNullOrEmpty(rootPath)) continue;
                if (string.Equals(full, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    kind = rootKind;
                    return true;
                }
            }

            if (includeAncestors)
            {
                string prefix = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var (rootPath, rootKind) in roots)
                {
                    if (string.IsNullOrEmpty(rootPath)) continue;
                    if (rootPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        kind = "folder that contains a " + rootKind;
                        return true;
                    }
                }
            }

            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandleW(IntPtr hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // required to open a directory handle
        private const uint FILE_SHARE_ALL = 0x7;                    // read | write | delete
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        // Resolve a path to its canonical, volume-rooted final form. Falls back to GetFullPath
        // for paths that can't be opened (e.g. don't exist yet). Returns without a trailing
        // separator and without the \\?\ prefix so results compare cleanly.
        internal static string Canonicalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                IntPtr h = CreateFileW(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                if (h == INVALID_HANDLE) return NormalizeFallback(path);
                try
                {
                    var buf = new char[1024];
                    uint len = GetFinalPathNameByHandleW(h, buf, (uint)buf.Length, 0);
                    if (len == 0 || len > buf.Length) return NormalizeFallback(path);
                    return StripPrefix(new string(buf, 0, (int)len));
                }
                finally { CloseHandle(h); }
            }
            catch { return NormalizeFallback(path); }
        }

        private static string NormalizeFallback(string path)
        {
            try { return StripPrefix(Path.GetFullPath(path)); }
            catch { return path.TrimEnd(Path.DirectorySeparatorChar); }
        }

        private static string StripPrefix(string p)
        {
            if (p.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) p = @"\\" + p.Substring(8);
            else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            return p.TrimEnd(Path.DirectorySeparatorChar);
        }

        // True when dest is the same as src, or sits beneath src in the tree.
        // Prevents Directory.Move from being asked to move a folder into one of
        // its own children — which throws midway and leaves a half-moved tree.
        private static bool IsAncestorOrEqual(string src, string dest)
        {
            try
            {
                // Canonicalize so an alias of src (8.3 name, junction, subst) can't sneak dest
                // "outside" src in a literal compare while really sitting inside it.
                string srcFull = Canonicalize(src);
                string destFull = Canonicalize(dest);
                if (string.IsNullOrEmpty(srcFull) || string.IsNullOrEmpty(destFull)) return false;
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
