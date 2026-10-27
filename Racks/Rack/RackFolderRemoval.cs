using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Properties;

namespace Racks.Rack;

/// <summary>
/// The last step of removing a rack whose folder sits on the Desktop: the dialog promised that the (emptied) folder
/// would be deleted. The broker only removes a folder that is really empty, so when an item could not be returned (it
/// is open, or the Desktop name was taken by something that appeared meanwhile) the folder stays with it. That is the
/// safe outcome, but the user was told otherwise, so it is reported once. The broker already writes the audit record
/// of the attempt (<c>DeleteEmptyDirectory</c>, with its status and reason).
/// </summary>
public static class RackFolderRemoval
{
    /// <summary>
    /// Tries to delete the emptied rack folder. Returns null when it is gone (deleted now, or already gone), otherwise
    /// the text to show the user: the folder is still there.
    /// </summary>
    public static string? DeleteEmptiedFolder(FileOperationBroker broker, string folder)
    {
        var result = broker.DeleteEmptyDirectory(folder, "remove rack: delete the emptied rack folder");
        if (result.Succeeded) return null;

        string trimmed = Path.TrimEndingDirectorySeparator(folder);
        bool stillThere = Directory.Exists(trimmed) || File.Exists(trimmed);
        return stillThere ? string.Format(Lang.RemoveRack_FolderKept, Path.GetFileName(trimmed)) : null;
    }
}
