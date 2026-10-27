using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Rack;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Rack;

// The leftover reference in a virtual rack after Explorer moved the item out. Only a NAME may go: a Racks
// shortcut file, or an extra hard link name. A file that is the only name of its data stays.
public sealed class OrphanShortcutCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-orphan-" + Guid.NewGuid().ToString("N"));
    private readonly string _rack;
    private readonly RecordingAuditSink _audit = new();
    private readonly FileOperationBroker _broker;

    public OrphanShortcutCleanupTests()
    {
        _rack = Path.Combine(_root, "rack");
        Directory.CreateDirectory(_rack);
        _broker = new FileOperationBroker(_audit);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("photo.jpg.lnk")]
    [InlineData("Docs.URL")]
    public void A_shortcut_file_left_behind_is_removed_and_audited(string name)
    {
        string path = Path.Combine(_rack, name);
        File.WriteAllText(path, "shortcut");

        var r = OrphanShortcutCleanup.Remove(_rack, name, _broker);

        Assert.NotNull(r);
        Assert.True(r!.Succeeded, r.Reason);
        Assert.False(File.Exists(path));
        var record = Assert.Single(_audit.Events);
        Assert.Equal("DeleteRacksShortcut", record.Kind);
        Assert.Equal(path, record.Source);
    }

    [Fact]
    public void A_hard_link_name_is_removed_and_the_original_keeps_the_data()
    {
        string original = Path.Combine(_root, "original.png");
        File.WriteAllText(original, "pixels");
        string name = "original.png";
        Assert.True(HardlinkHelper.TryCreate(original, Path.Combine(_rack, name)), "test setup: hard link");

        var r = OrphanShortcutCleanup.Remove(_rack, name, _broker);

        Assert.True(r!.Succeeded, r.Reason);
        Assert.False(File.Exists(Path.Combine(_rack, name)));
        Assert.Equal("pixels", File.ReadAllText(original));
        Assert.Equal("Unlink", Assert.Single(_audit.Events).Kind);
    }

    [Fact]
    public void A_plain_file_that_is_the_only_name_of_its_data_is_never_deleted()
    {
        string path = Path.Combine(_rack, "thesis.docx");
        File.WriteAllText(path, "years of work");

        var r = OrphanShortcutCleanup.Remove(_rack, "thesis.docx", _broker);

        Assert.Equal(OperationStatus.Rejected, r!.Status);
        Assert.Equal("years of work", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\outside.lnk")]
    [InlineData("sub\\x.lnk")]
    [InlineData("missing.lnk")]
    public void Odd_or_missing_names_do_nothing(string name)
    {
        string outside = Path.Combine(_root, "outside.lnk");
        File.WriteAllText(outside, "x");

        Assert.Null(OrphanShortcutCleanup.Remove(_rack, name, _broker));

        Assert.True(File.Exists(outside));
        Assert.Empty(_audit.Events);
    }

    // ---- the rack-level guard (RackWindow drag-out) ---------------------------------------------

    [Fact]
    public void A_virtual_rack_sandbox_inside_the_frames_root_is_cleaned()
    {
        string frames = Path.Combine(_root, "VirtualFrames");
        string sandbox = Path.Combine(frames, "abc123");
        Directory.CreateDirectory(sandbox);
        string path = Path.Combine(sandbox, "x.lnk");
        File.WriteAllText(path, "shortcut");

        var r = OrphanShortcutCleanup.RemoveForRack(isShortcutsOnly: true, sandbox, "x.lnk", _broker, frames);

        Assert.True(r!.Succeeded, r.Reason);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_duplicated_desktop_rack_is_shortcuts_only_but_its_folder_is_the_desktop_so_nothing_is_touched()
    {
        // InstanceController.DuplicateInstance sets IsShortcutsOnly and a Folder that is the real Desktop.
        string frames = Path.Combine(_root, "VirtualFrames");
        Directory.CreateDirectory(frames);
        string desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(desktop);
        string original = Path.Combine(_root, "elsewhere.png");
        File.WriteAllText(original, "pixels");
        string hardLink = Path.Combine(desktop, "elsewhere.png");
        Assert.True(HardlinkHelper.TryCreate(original, hardLink), "test setup: hard link");
        string shortcut = Path.Combine(desktop, "site.url");
        File.WriteAllText(shortcut, "[InternetShortcut]");

        Assert.Null(OrphanShortcutCleanup.RemoveForRack(true, desktop, "elsewhere.png", _broker, frames));
        Assert.Null(OrphanShortcutCleanup.RemoveForRack(true, desktop, "site.url", _broker, frames));

        Assert.Equal("pixels", File.ReadAllText(hardLink));
        Assert.True(File.Exists(shortcut));
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public void A_rack_that_is_not_shortcuts_only_or_has_no_folder_is_never_cleaned()
    {
        string frames = Path.Combine(_root, "VirtualFrames");
        string sandbox = Path.Combine(frames, "abc123");
        Directory.CreateDirectory(sandbox);
        string path = Path.Combine(sandbox, "x.lnk");
        File.WriteAllText(path, "shortcut");

        Assert.Null(OrphanShortcutCleanup.RemoveForRack(false, sandbox, "x.lnk", _broker, frames));
        Assert.Null(OrphanShortcutCleanup.RemoveForRack(true, "", "x.lnk", _broker, frames));
        Assert.Null(OrphanShortcutCleanup.RemoveForRack(true, null, "x.lnk", _broker, frames));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_folder_that_only_shares_the_frames_root_name_prefix_is_not_inside_it()
    {
        string frames = Path.Combine(_root, "VirtualFrames");
        string lookalike = Path.Combine(_root, "VirtualFrames-old", "abc");
        Directory.CreateDirectory(frames);
        Directory.CreateDirectory(lookalike);
        string path = Path.Combine(lookalike, "x.lnk");
        File.WriteAllText(path, "shortcut");

        Assert.Null(OrphanShortcutCleanup.RemoveForRack(true, lookalike, "x.lnk", _broker, frames));
        Assert.True(File.Exists(path));
    }
}
