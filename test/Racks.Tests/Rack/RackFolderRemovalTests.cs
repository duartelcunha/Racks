using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Rack;
using Racks.Tests.Adversarial;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Rack;

// Removing a rack that lives in a folder on the Desktop promises "the folder will be deleted". The broker deletes it
// only when it is really empty; when it is not, the user is told once and the folder is left alone. The decision is
// RackFolderRemoval.DeleteEmptiedFolder (the dialog itself is a window and is not tested here).
[Trait("Category", "Adversarial")]
public sealed class RackFolderRemovalTests : AdversarialTree
{
    private readonly RecordingAuditSink _audit = new();
    private FileOperationBroker Broker => new(_audit);

    [Fact]
    public void An_emptied_folder_is_deleted_and_nothing_is_reported()
    {
        string folder = Dir("Desktop rack");

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, folder);

        Assert.Null(message);
        Assert.False(Directory.Exists(folder));
        var record = Assert.Single(_audit.Events);
        Assert.Equal("DeleteEmptyDirectory", record.Kind);
        Assert.Equal("Succeeded", record.Status);
    }

    [Fact]
    public void A_folder_that_still_holds_something_stays_and_the_user_is_told_once_with_the_folder_name()
    {
        string folder = Dir("My rack");
        string stuck = Write(Path.Combine(folder, "open.pdf"), "pdf");

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, folder);

        Assert.NotNull(message);
        Assert.Contains("\"My rack\"", message);
        Assert.Contains("Desktop", message);
        Assert.Equal("pdf", File.ReadAllText(stuck));
        // The attempt is audited by the broker with the reason, so the log says why the folder stayed.
        var record = Assert.Single(_audit.Events);
        Assert.Equal("DeleteEmptyDirectory", record.Kind);
        Assert.Equal("Rejected", record.Status);
        Assert.Contains("isn't empty", record.Detail);
        Assert.StartsWith("remove rack:", record.Reason);
    }

    [Fact]
    public void A_folder_with_only_hidden_content_counts_as_not_empty_and_is_reported()
    {
        string folder = Dir("Hidden stuff");
        string hidden = Write(Path.Combine(folder, "desktop.ini"), "[.ShellClassInfo]");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, folder);

        Assert.NotNull(message);
        Assert.True(File.Exists(hidden));
    }

    [Fact]
    public void A_folder_that_is_already_gone_is_not_reported()
    {
        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, P("already-gone"));

        Assert.Null(message);
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_answer_or_the_name_shown()
    {
        string folder = Dir("With slash");
        Write(Path.Combine(folder, "x.txt"), "x");

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, folder + Path.DirectorySeparatorChar);

        Assert.NotNull(message);
        Assert.Contains("\"With slash\"", message);
    }

    [Fact]
    public void A_protected_folder_is_never_deleted_and_is_reported()
    {
        string folder = Dir("protected-folder");
        using var scope = SafeMove.UseProtectedFoldersForTest(folder);

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, folder);

        Assert.NotNull(message);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("Rejected", Assert.Single(_audit.Events).Status);
    }

    [Fact]
    public void A_junction_in_place_of_the_folder_is_not_deleted_and_its_target_is_untouched()
    {
        string target = Write(P("outside", "keep.txt"), "outside");
        string link = Link(P("outside"), P("Linked rack"));

        var message = RackFolderRemoval.DeleteEmptiedFolder(Broker, link);

        Assert.NotNull(message);
        Assert.True(Directory.Exists(link));
        Assert.Equal("outside", File.ReadAllText(target));
    }

    [Fact]
    public void The_message_comes_from_the_resources_and_names_what_to_do()
    {
        string text = string.Format(Racks.Properties.Lang.RemoveRack_FolderKept, "Anything");

        Assert.Contains("\"Anything\"", text);
        Assert.Contains("Nothing was deleted", text);
    }
}
