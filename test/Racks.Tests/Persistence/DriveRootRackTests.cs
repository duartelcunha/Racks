using System.IO;

namespace Racks.Tests.Persistence;

// P11b investigation (found in the P6 review): why a rack whose folder is a drive root (D:\) has the name ""
// and is never persisted. This documents the CURRENT behavior end to end; nothing here changes it.
//
// The chain: dropping a folder on an empty rack runs `Instance.Name = Path.GetFileName(folder)` (RackWindow.DragDrop,
// the "bind the rack to that folder" path). For D:\ that is "". The rack's name is also its registry key
// (HKCU\SOFTWARE\Racks\Instances\<Name>), and there is no key for "", so every save is a silent no-op: the rack works
// for the session (title empty, folder listed) and is gone after a restart, with nothing logged and nothing shown.
[Collection("Registry")]
public sealed class DriveRootRackTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"D:")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    public void The_name_the_drop_handler_derives_from_a_drive_or_share_root_is_empty(string folder)
        => Assert.Equal("", Path.GetFileName(folder));

    [Theory]
    [InlineData(@"\\server\share\Data")]
    [InlineData(@"D:\Data")]
    [InlineData(@"D:\Data\")]
    public void Any_folder_below_a_root_or_a_share_does_get_a_name(string folder)
        => Assert.NotEqual("", Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));

    [Fact]
    public void A_rack_bound_to_a_drive_root_is_never_written_and_is_gone_after_a_restart()
    {
        string driveRoot = Path.GetPathRoot(Path.GetTempPath())!;   // only read; the rack is never started
        var rack = new Instance("empty", false);

        // Exactly the handler's steps for a dropped folder.
        rack.Folder = driveRoot;
        rack.Name = Path.GetFileName(driveRoot);
        rack.IconSize = 64;                                     // later edits are write-through too
        _reg.Controller.WriteInstanceToKey(rack);                // SaveInstance()

        Assert.Equal("", rack.Name);
        Assert.Equal("", rack.GetKeyLocation());
        Assert.False(_reg.Repository.Save(rack));
        Assert.False(_reg.RackKeyExists(""));
        Assert.False(_reg.RackKeyExists("empty"));
        Assert.Empty(_reg.Repository.LoadAll().Racks);           // next start: the rack does not come back
        Assert.Empty(_reg.Audit.Events);                         // and nobody was told
    }

    [Fact]
    public void A_rack_bound_to_an_ordinary_folder_is_written_under_the_folder_name_and_comes_back()
    {
        string folder = Path.Combine(Path.GetTempPath(), "racks-driveroot-control-" + Guid.NewGuid().ToString("N"));
        var rack = new Instance("empty", false);

        rack.Folder = folder;
        rack.Name = Path.GetFileName(folder);
        _reg.Controller.WriteInstanceToKey(rack);

        Assert.True(_reg.RackKeyExists(rack.Name));
        Assert.Single(_reg.Repository.LoadAll().Racks);
    }
}
