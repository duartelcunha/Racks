using System.IO;
using Racks.Core;

namespace Racks.Tests.Core;

// Undo of "Folders" mode removes the folders Magic Organize created, but only the empty ones and never
// recursively, so a file the user (or the restore step) left inside is never touched.
public sealed class MagicOrganizeUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-undo-" + Guid.NewGuid().ToString("N"));

    public MagicOrganizeUndoTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private MagicOrganizeUndo Undo(params string[] folders)
    {
        var undo = new MagicOrganizeUndo { OrganizeMode = MagicOrganizeUndo.Mode.Folders };
        undo.CreatedFolders.AddRange(folders);
        return undo;
    }

    [Fact]
    public void Empty_folders_are_removed()
    {
        string a = Path.Combine(_root, "Images"), b = Path.Combine(_root, "Docs");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);

        Undo(a, b).RemoveCreatedFolders();

        Assert.False(Directory.Exists(a));
        Assert.False(Directory.Exists(b));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void A_folder_that_still_holds_a_file_is_kept_with_its_content()
    {
        string full = Path.Combine(_root, "Images"), empty = Path.Combine(_root, "Docs");
        Directory.CreateDirectory(full);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(full, "keep.png"), "pixels");

        Undo(full, empty).RemoveCreatedFolders();

        Assert.Equal("pixels", File.ReadAllText(Path.Combine(full, "keep.png")));
        Assert.False(Directory.Exists(empty));
    }

    [Fact]
    public void It_is_never_recursive_not_even_for_empty_subfolders_or_hidden_files()
    {
        string withSub = Path.Combine(_root, "WithSub"), withHidden = Path.Combine(_root, "WithHidden");
        Directory.CreateDirectory(Path.Combine(withSub, "inner"));
        Directory.CreateDirectory(withHidden);
        string hidden = Path.Combine(withHidden, ".hidden");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Undo(withSub, withHidden).RemoveCreatedFolders();

        Assert.True(Directory.Exists(Path.Combine(withSub, "inner")));
        Assert.True(File.Exists(hidden));
    }

    [Fact]
    public void Missing_folders_and_odd_entries_are_ignored_without_throwing()
    {
        string real = Path.Combine(_root, "Real");
        Directory.CreateDirectory(real);

        Undo(Path.Combine(_root, "Gone"), "", "relative\\path", real).RemoveCreatedFolders();

        Assert.False(Directory.Exists(real));
    }

    [Fact]
    public void A_folder_the_user_replaced_by_a_file_is_left_alone()
    {
        string path = Path.Combine(_root, "Notes");
        File.WriteAllText(path, "now a file");

        Undo(path).RemoveCreatedFolders();

        Assert.Equal("now a file", File.ReadAllText(path));
    }

    [Fact]
    public void A_protected_folder_in_the_list_is_never_removed()
    {
        // The Windows folder is on the protected list (and is not empty): the undo record must not be able to name it.
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Undo(windows).RemoveCreatedFolders();

        Assert.True(Directory.Exists(windows));
    }
}
