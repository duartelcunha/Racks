using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Racks.Util
{
    // Maintains %USERPROFILE%\Racks\ as a "user-friendly index" of every rack:
    // one directory junction per rack, named after the rack's title, pointing
    // at the rack's sandbox in AppData. Purpose:
    //   1. The visual Desktop stays clean (files moved into the sandbox).
    //   2. Any file picker that lands in the user-profile root or pulls from
    //      Quick Access sees the rack names directly and can navigate into
    //      them like any other folder.
    //
    // The mirror is rebuilt as a single operation rather than reasoned about
    // entry-by-entry — racks are typically a handful, the cost is negligible,
    // and the simpler algorithm avoids subtle bugs around renames and dupes.
    //
    // Safety: only reparse-point children of the mirror folder are ever
    // touched. If the user drops a real file or folder into %USERPROFILE%\Racks
    // by hand, it survives a Rebuild call.
    public static class RackMirror
    {
        public static string MirrorRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Racks");

        // Sanitize a rack title into a usable folder name. Strips reserved
        // chars and trims edges; falls back to "Rack" if nothing survives.
        public static string Sanitize(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Rack";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = title.Where(c => !invalid.Contains(c)).ToArray();
            string s = new string(chars).Trim().Trim('.');
            if (string.IsNullOrEmpty(s)) return "Rack";
            // Some shell features dislike trailing dots/spaces; double-trim.
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }

        // Rebuild the entire mirror from scratch. `racks` is a list of
        // (displayTitle, sandboxPath) pairs — caller passes only virtual
        // (sandbox-backed) racks. Folder-backed racks are excluded because
        // the user already has a real path for them.
        public static void Rebuild(IEnumerable<(string Title, string SandboxPath)> racks)
        {
            try
            {
                Directory.CreateDirectory(MirrorRoot);

                // 1. Remove existing reparse-point children (leave anything
                //    the user manually dropped in there).
                RemoveJunctions(MirrorRoot);

                // 2. Materialize a junction per rack with collision resolution.
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var existing in Directory.EnumerateFileSystemEntries(MirrorRoot))
                    used.Add(Path.GetFileName(existing));

                foreach (var (title, sandbox) in racks)
                {
                    if (string.IsNullOrEmpty(sandbox) || !Directory.Exists(sandbox))
                        continue;

                    string baseName = Sanitize(title);
                    string name = baseName;
                    int i = 2;
                    while (used.Contains(name))
                    {
                        name = $"{baseName} ({i++})";
                        if (i > 999) break; // give up rather than loop forever
                    }
                    string junctionPath = Path.Combine(MirrorRoot, name);
                    if (JunctionHelper.TryCreate(sandbox, junctionPath))
                        used.Add(name);
                }
            }
            catch
            {
                // The mirror is a UX nicety; failure must NOT break the app.
            }
        }

        // Remove only the reparse-point children of `root` (the junctions Racks created). Real files
        // and folders the user put there by hand are left alone. Never descends into a junction.
        public static int RemoveJunctions(string root)
        {
            int removed = 0;
            if (!Directory.Exists(root)) return 0;
            foreach (var existing in Directory.EnumerateFileSystemEntries(root))
            {
                try
                {
                    if (!JunctionHelper.IsReparsePoint(existing)) continue;
                    if (Directory.Exists(existing)) Directory.Delete(existing, recursive: false);
                    else File.Delete(existing);
                    removed++;
                }
                catch { /* best-effort */ }
            }
            return removed;
        }

        // Undo PinToQuickAccess. Works on the Quick Access namespace itself (invoking the verb on
        // the folder through its parent does nothing), so it also removes a stale pin whose folder
        // is already gone. Best-effort, like pinning.
        public static void UnpinFromQuickAccess()
        {
            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return;
                dynamic? shell = Activator.CreateInstance(shellAppType);
                if (shell == null) return;
                dynamic? quickAccess = shell.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
                if (quickAccess != null)
                {
                    foreach (dynamic item in quickAccess.Items())
                    {
                        string? path = item.Path;
                        if (string.Equals(path, MirrorRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            try { item.InvokeVerb("unpinfromhome"); } catch { }
                        }
                    }
                    Marshal.FinalReleaseComObject(quickAccess);
                }
                Marshal.FinalReleaseComObject(shell);
            }
            catch
            {
                // Best-effort.
            }
        }

        // True when the mirror folder is currently pinned to Quick Access.
        public static bool IsPinnedToQuickAccess()
        {
            try
            {
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return false;
                dynamic? shell = Activator.CreateInstance(shellAppType);
                if (shell == null) return false;
                bool pinned = false;
                dynamic? quickAccess = shell.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
                if (quickAccess != null)
                {
                    foreach (dynamic item in quickAccess.Items())
                    {
                        string? path = item.Path;
                        if (string.Equals(path, MirrorRoot, StringComparison.OrdinalIgnoreCase)) { pinned = true; break; }
                    }
                    Marshal.FinalReleaseComObject(quickAccess);
                }
                Marshal.FinalReleaseComObject(shell);
                return pinned;
            }
            catch
            {
                return false;
            }
        }

        // Pin the mirror folder to Quick Access (Win11 "Home" / "Pinned"
        // section). Uses the shell "pintohome" verb via Shell.Application
        // COM automation. Idempotent in practice — pinning an already-pinned
        // folder is a no-op.
        public static void PinToQuickAccess()
        {
            try
            {
                Directory.CreateDirectory(MirrorRoot);
                if (IsPinnedToQuickAccess()) return;
                Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null) return;
                dynamic? shell = Activator.CreateInstance(shellAppType);
                if (shell == null) return;

                string? parent = Path.GetDirectoryName(MirrorRoot);
                string? leaf = Path.GetFileName(MirrorRoot);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

                dynamic ns = shell.NameSpace(parent);
                if (ns == null) return;
                dynamic item = ns.ParseName(leaf);
                if (item == null) return;
                // "pintohome" is the canonical (language independent) verb name. The old fallback
                // matched translated captions in English and Portuguese only, so on any other Windows
                // language it could never work; it was removed rather than extended.
                try { item.InvokeVerb("pintohome"); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"pintohome failed: {ex.Message}"); }
                if (item is object) Marshal.FinalReleaseComObject(item);
                if (ns is object) Marshal.FinalReleaseComObject(ns);
                if (shell is object) Marshal.FinalReleaseComObject(shell);
            }
            catch
            {
                // Quick Access pinning is best-effort. App works without it.
            }
        }
    }
}
