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
        }

        public Mode OrganizeMode { get; set; }
        public List<MovedItem> Moved { get; } = new();
        public List<string> CreatedRackNames { get; } = new();   // instance names to remove (Racks mode)
        public List<string> CreatedFolders { get; } = new();     // desktop folders to remove (Folders mode)

        // Only the most recent run is undoable (kept in memory + persisted for safety).
        public static MagicOrganizeUndo? Last { get; private set; }

        public static MagicOrganizeUndo Begin(Mode mode)
        {
            Last = new MagicOrganizeUndo { OrganizeMode = mode };
            return Last;
        }

        public void RecordMove(string original, string moved) =>
            Moved.Add(new MovedItem { OriginalPath = original, NewPath = moved });

        public bool HasAnything => Moved.Count > 0 || CreatedRackNames.Count > 0 || CreatedFolders.Count > 0;

        // Move everything back where it came from. Returns how many items were restored.
        // Leaves the undo record cleared afterwards (can only undo once).
        // The broker and the shell gateway default to the app's own; tests pass their own.
        public int RestoreFiles(Racks.Infrastructure.FileSystem.FileOperationBroker? broker = null, Racks.Infrastructure.Shell.ShellOperationGateway? shell = null)
        {
            broker ??= Racks.Infrastructure.FileSystem.FileOperationBroker.Default;
            shell ??= Racks.Infrastructure.Shell.ShellOperationGateway.Default;
            int restored = 0;
            foreach (var m in Moved)
            {
                try
                {
                    if (!File.Exists(m.NewPath) && !Directory.Exists(m.NewPath)) continue;
                    // If something already sits at the original spot, don't clobber it.
                    if (File.Exists(m.OriginalPath) || Directory.Exists(m.OriginalPath)) continue;

                    if (broker.Move(m.NewPath, m.OriginalPath, "magic organize: undo").Completed)
                    {
                        shell.NotifyItemMoved(m.NewPath, m.OriginalPath, Directory.Exists(m.OriginalPath));
                        restored++;
                    }
                }
                catch { /* best-effort per item */ }
            }
            return restored;
        }

        // Remove the empty desktop folders this run created (Folders mode). Only deletes them
        // if they're empty after files were moved back, so we never delete user data.
        public void RemoveCreatedFolders(Racks.Infrastructure.FileSystem.FileOperationBroker? broker = null)
        {
            broker ??= Racks.Infrastructure.FileSystem.FileOperationBroker.Default;
            foreach (var f in CreatedFolders)
            {
                try
                {
                    // The broker refuses anything that is not an empty, real, unprotected folder (never recursive).
                    if (Directory.Exists(f))
                        broker.DeleteEmptyDirectory(f, "magic organize: undo, remove the folder it created");
                }
                catch { }
            }
        }

        public static void Clear() => Last = null;
    }
}
