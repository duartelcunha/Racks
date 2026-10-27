using System.IO;
using Racks.Core;
using Racks.Infrastructure.FileSystem;

namespace Racks.Rack;

/// <summary>
/// A virtual (shortcuts-only) rack keeps a reference to each file in its sandbox folder: a .lnk or .url made by
/// Racks, or an extra hard link name. When Explorer moves that reference out of the rack the item may still be
/// left behind in the sandbox. This removes only what is certainly a leftover NAME, never data: a Racks
/// shortcut file, or a hard link name whose data is also reachable under another name. A file that is the only
/// name of its data is left alone, so the user can never lose a file this way.
/// </summary>
internal static class OrphanShortcutCleanup
{
    /// <summary>
    /// The drag-out entry point. Cleans up only for a rack that really is a virtual rack: shortcuts-only AND its
    /// folder is a sandbox inside the virtual frames root. A duplicated Desktop rack is also "shortcuts-only" but
    /// its folder is the real Desktop, where the item a drag just returned must never be touched.
    /// </summary>
    internal static FileOperationResult? RemoveForRack(bool isShortcutsOnly, string? rackFolder, string itemName,
        FileOperationBroker? broker = null, string? virtualFramesRoot = null)
    {
        if (!isShortcutsOnly || string.IsNullOrEmpty(rackFolder)) return null;
        bool inSandbox = InstanceController.IsInsideRoot(rackFolder, virtualFramesRoot ?? InstanceController.VirtualFramesRoot);
        return inSandbox ? Remove(rackFolder, itemName, broker) : null;
    }

    /// <summary>Returns the broker result, or null when there was nothing to do (bad name, or no such file).</summary>
    internal static FileOperationResult? Remove(string rackFolder, string itemName, FileOperationBroker? broker = null)
    {
        if (string.IsNullOrEmpty(rackFolder) || string.IsNullOrEmpty(itemName) || Path.GetFileName(itemName) != itemName)
            return null;

        string path = Path.Combine(rackFolder, itemName);
        if (!File.Exists(path)) return null;

        broker ??= FileOperationBroker.Default;
        string ext = Path.GetExtension(itemName);
        bool isShortcutFile = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase);
        const string reason = "virtual rack: remove the leftover reference after Explorer moved the item out";
        return isShortcutFile
            ? broker.DeleteRacksShortcut(path, rackFolder, reason)
            : broker.Unlink(path, reason);
    }
}
