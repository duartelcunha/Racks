using System;
using System.Collections.Generic;
using System.IO;

namespace Racks.Core
{
    // Records what the last Magic Organize run did so it can be fully undone: every file it
    // moved (where it came from, where it went), the racks it created, and the folders it
    // created. Undo moves every file back to its original desktop spot, removes the created
    // racks/folders, and leaves the desktop exactly as it was.
    public class MagicOrganizeUndo
    {
        public enum Mode { Racks, Folders }

        public class MovedItem
        {
            public string OriginalPath { get; set; } = "";   // where it was on the desktop
            public string NewPath { get; set; } = "";        // where Magic Organize put it
            public bool Completed { get; set; }
            public bool UndoPending { get; set; }
            public long Length { get; set; }
            public DateTime LastWriteUtc { get; set; }
            public bool IsDirectory { get; set; }
        }

        public Mode OrganizeMode { get; set; }
        public List<MovedItem> Moved { get; set; } = new();
        public List<string> CreatedRackNames { get; set; } = new();
        public List<string> CreatedFolders { get; set; } = new();

        // Only the most recent run is undoable (kept in memory + persisted for safety).
        private static string StoragePath => Path.Combine(
            Environment.GetEnvironmentVariable("RACKS_TEST_PROFILE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RacksData"), "legacy-organize-undo.json");
        private static MagicOrganizeUndo? _last;
        public static MagicOrganizeUndo? Last
        {
            get
            {
                if (_last == null && File.Exists(StoragePath)) _last = JsonStore.Read<MagicOrganizeUndo>(StoragePath);
                return _last;
            }
            private set => _last = value;
        }
        public void Save() => JsonStore.Write(StoragePath, this);

        public static MagicOrganizeUndo Begin(Mode mode)
        {
            if (Last?.Moved.Count > 0) throw new InvalidOperationException("An earlier organize operation is still available to undo. Keep or undo it before organizing again.");
            Last = new MagicOrganizeUndo { OrganizeMode = mode };
            Last.Save();
            return Last;
        }

        public void RecordIntent(string original, string moved)
        {
            Moved.Add(new MovedItem { OriginalPath = original, NewPath = moved });
            Save();
        }
        public void RecordMove(string original, string moved)
        {
            var item = Moved.Find(x => x.OriginalPath == original && x.NewPath == moved);
            if (item == null) { item = new MovedItem { OriginalPath = original, NewPath = moved }; Moved.Add(item); }
            item.Completed = true;
            item.IsDirectory = Directory.Exists(moved);
            item.Length = item.IsDirectory ? 0 : new FileInfo(moved).Length;
            item.LastWriteUtc = File.GetLastWriteTimeUtc(moved);
            Save();
        }
        public void CancelIntent(string original, string moved)
        {
            Moved.RemoveAll(x => !x.Completed && x.OriginalPath == original && x.NewPath == moved);
            Save();
        }

        public bool HasAnything => Moved.Count > 0 || CreatedRackNames.Count > 0 || CreatedFolders.Count > 0;
        public bool HasUnresolved => Moved.Exists(x => !x.Completed || x.UndoPending);

        // Move everything back where it came from. Returns how many items were restored.
        // Leaves the undo record cleared afterwards (can only undo once).
        public int RestoreFiles()
        {
            int restored = 0;
            foreach (var m in Moved.ToArray())
            {
                try
                {
                    if (!m.Completed || m.UndoPending || (!File.Exists(m.NewPath) && !Directory.Exists(m.NewPath))) continue;
                    // If something already sits at the original spot, don't clobber it.
                    if (File.Exists(m.OriginalPath) || Directory.Exists(m.OriginalPath)) continue;
                    if (!m.IsDirectory && (new FileInfo(m.NewPath).Length != m.Length || File.GetLastWriteTimeUtc(m.NewPath) != m.LastWriteUtc)) continue;
                    m.UndoPending = true; Save();

                    if (Racks.Util.SafeMove.TryMove(m.NewPath, m.OriginalPath, out _) == Racks.Util.SafeMove.Result.Moved)
                    {
                        Racks.Util.Interop.NotifyShellMove(m.NewPath, m.OriginalPath, Directory.Exists(m.OriginalPath));
                        restored++;
                        Moved.Remove(m);
                        Save();
                    }
                    else { m.UndoPending = false; Save(); }
                }
                catch { /* best-effort per item */ }
            }
            return restored;
        }

        // Remove the empty desktop folders this run created (Folders mode). Only deletes them
        // if they're empty after files were moved back, so we never delete user data.
        public void RemoveCreatedFolders()
        {
            foreach (var f in CreatedFolders)
            {
                try
                {
                    if (Directory.Exists(f) && Directory.GetFileSystemEntries(f).Length == 0)
                        Directory.Delete(f);
                }
                catch { }
            }
        }

        public static void Clear()
        {
            if (File.Exists(StoragePath)) File.Delete(StoragePath);
            Last = null;
        }
    }
}
