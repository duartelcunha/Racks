using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.FileSystem;

// P2 operations: DeleteEmptyDirectory, Unlink, DeleteRacksShortcut (and the cross-volume refusal of MoveSameVolume).
// Real temp directories and real junctions / hard links. Anything aimed at a protected root goes through a
// junction or is a rejection that happens before any delete, so a regression can at worst remove a link.
public sealed class BrokerEmptyDirUnlinkShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-broker2-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingAuditSink _audit = new();
    private readonly FileOperationBroker _broker;
    private readonly List<string> _links = [];

    public BrokerEmptyDirUnlinkShortcutTests()
    {
        Directory.CreateDirectory(_root);
        _broker = new FileOperationBroker(_audit);
    }

    public void Dispose()
    {
        foreach (var link in _links) { try { Directory.Delete(link); } catch { } }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string P(params string[] parts) => Path.Combine([_root, .. parts]);

    private string File_(string relative, string content = "data")
    {
        string path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Link(string target, string link)
    {
        Assert.True(JunctionHelper.TryCreate(target, link), "test setup: junction");
        _links.Add(link);
        return link;
    }

    private void AssertOneRecord(FileOperationResult result, FileOperationKind kind, OperationStatus status, string reason)
    {
        var record = Assert.Single(_audit.Events);
        Assert.Equal(result.OperationId, record.OperationId);
        Assert.Equal(kind.ToString(), record.Kind);
        Assert.Equal(status.ToString(), record.Status);
        Assert.Equal(status, result.Status);
        Assert.Equal(reason, record.Reason);
        Assert.Equal(result.Source, record.Source);
        Assert.Equal(result.Destination, record.Destination);
    }

    // ---- DeleteEmptyDirectory ---------------------------------------------------------------

    [Fact]
    public void DeleteEmptyDirectory_removes_an_empty_folder_and_audits_it()
    {
        Directory.CreateDirectory(P("empty"));

        var r = _broker.DeleteEmptyDirectory(P("empty"), "test empty");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(P("empty")));
        AssertOneRecord(r, FileOperationKind.DeleteEmptyDirectory, OperationStatus.Succeeded, "test empty");
    }

    [Fact]
    public void DeleteEmptyDirectory_never_removes_a_folder_that_holds_anything()
    {
        string file = File_("full\\keep.txt", "keep me");
        Directory.CreateDirectory(P("onlysub", "inner"));
        string hidden = File_("hid\\.secret", "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var withFile = _broker.DeleteEmptyDirectory(P("full"), "test full");
        var withSub = _broker.DeleteEmptyDirectory(P("onlysub"), "test only a subfolder");
        var withHidden = _broker.DeleteEmptyDirectory(P("hid"), "test hidden file");

        Assert.All(new[] { withFile, withSub, withHidden }, r => Assert.Equal(OperationStatus.Rejected, r.Status));
        Assert.Equal("keep me", File.ReadAllText(file));
        Assert.True(Directory.Exists(P("onlysub", "inner")));        // not recursive: the empty child is still there
        Assert.True(File.Exists(hidden));
        Assert.Equal(3, _audit.Events.Count);
    }

    [Fact]
    public void DeleteEmptyDirectory_rejects_a_missing_path_a_file_and_a_bad_path()
    {
        string file = File_("a.txt");

        Assert.Equal(OperationStatus.Rejected, _broker.DeleteEmptyDirectory(P("nope"), "missing").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteEmptyDirectory(file, "a file").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteEmptyDirectory("relative\\dir", "relative").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteEmptyDirectory(P("..", "x"), "dotdot").Status);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DeleteEmptyDirectory_refuses_a_junction_even_to_an_empty_folder_and_leaves_both_alone()
    {
        Directory.CreateDirectory(P("target"));
        string link = Link(P("target"), P("link"));

        var r = _broker.DeleteEmptyDirectory(link, "test link");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Contains("Unlink", r.Reason);
        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(P("target")));
    }

    [Fact]
    public void DeleteEmptyDirectory_rejects_a_protected_folder_before_anything_else()
    {
        // The Windows folder is on SafeMove's protected list and is never empty, so even a regression could
        // not delete it; the reason proves the protected check ran.
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var r = _broker.DeleteEmptyDirectory(windows, "test protected");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Contains("protected", r.Reason);
        Assert.True(Directory.Exists(windows));
    }

    [Fact]
    public void DeleteEmptyDirectory_without_a_reason_is_rejected_and_audited()
    {
        Directory.CreateDirectory(P("empty"));
        var r = _broker.DeleteEmptyDirectory(P("empty"), " ");
        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(P("empty")));
        Assert.Single(_audit.Events);
    }

    // ---- Unlink -----------------------------------------------------------------------------

    [Fact]
    public void Unlink_removes_a_junction_and_leaves_its_target_and_content()
    {
        string keep = File_("target\\keep.txt", "keep me");
        string link = Link(P("target"), P("link"));

        var r = _broker.Unlink(link, "test junction");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(link));
        Assert.Equal("keep me", File.ReadAllText(keep));
        AssertOneRecord(r, FileOperationKind.Unlink, OperationStatus.Succeeded, "test junction");
    }

    [Fact]
    public void Unlink_removes_a_junction_whose_target_is_gone()
    {
        Directory.CreateDirectory(P("target"));
        string link = Link(P("target"), P("link"));
        Directory.Delete(P("target"));

        var r = _broker.Unlink(link, "test dangling");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(JunctionHelper.IsReparsePoint(link));
    }

    [Fact]
    public void Unlink_removes_a_file_symbolic_link_and_not_its_target()
    {
        string target = File_("real.txt", "real");
        string link = P("sym.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }   // needs Developer Mode or elevation

        var r = _broker.Unlink(link, "test file symlink");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(JunctionHelper.IsReparsePoint(link));
        Assert.Equal("real", File.ReadAllText(target));
    }

    [Fact]
    public void Unlink_of_an_extra_hard_link_name_keeps_the_data_under_the_other_name()
    {
        string original = File_("original.txt", "the data");
        string second = P("second.txt");
        Assert.True(HardlinkHelper.TryCreate(original, second), "test setup: hard link");
        Assert.Equal(2, HardlinkHelper.GetLinkCount(original));

        var r = _broker.Unlink(second, "test hardlink");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(File.Exists(second));
        Assert.Equal("the data", File.ReadAllText(original));
        Assert.Equal(1, HardlinkHelper.GetLinkCount(original));
        AssertOneRecord(r, FileOperationKind.Unlink, OperationStatus.Succeeded, "test hardlink");
    }

    [Fact]
    public void Unlink_never_deletes_a_file_that_is_the_only_name_of_its_data()
    {
        string only = File_("only.txt", "irreplaceable");

        var r = _broker.Unlink(only, "test sole name");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Equal("irreplaceable", File.ReadAllText(only));
    }

    [Fact]
    public void Unlink_after_the_other_name_is_gone_is_refused_so_the_last_copy_survives()
    {
        string original = File_("original.txt", "the data");
        string second = P("second.txt");
        Assert.True(HardlinkHelper.TryCreate(original, second));
        Assert.True(_broker.Unlink(second, "first unlink").Succeeded);

        var again = _broker.Unlink(original, "unlink the last name");

        Assert.Equal(OperationStatus.Rejected, again.Status);
        Assert.Equal("the data", File.ReadAllText(original));
    }

    [Fact]
    public void Unlink_rejects_a_real_folder_a_missing_path_and_a_bad_path()
    {
        File_("folder\\inner.txt", "inner");

        var folder = _broker.Unlink(P("folder"), "test folder");
        var missing = _broker.Unlink(P("nope.txt"), "test missing");
        var bad = _broker.Unlink("not\\absolute", "test bad path");

        Assert.All(new[] { folder, missing, bad }, r => Assert.Equal(OperationStatus.Rejected, r.Status));
        Assert.Equal("inner", File.ReadAllText(P("folder", "inner.txt")));
    }

    [Fact]
    public void Unlink_refuses_the_real_protected_folder_and_unlinks_a_link_that_points_at_it()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Assert.True(Directory.Exists(desktop), "test setup: no Desktop folder");
        string before = string.Join("|", Directory.EnumerateFileSystemEntries(desktop).Order());
        string link = Link(desktop, P("desktop-link"));

        var real = _broker.Unlink(desktop, "test protected");
        var viaLink = _broker.Unlink(link, "test link to protected");

        Assert.Equal(OperationStatus.Rejected, real.Status);
        Assert.Contains("protected", real.Reason);
        Assert.True(viaLink.Succeeded, viaLink.Reason);
        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(desktop));
        Assert.Equal(before, string.Join("|", Directory.EnumerateFileSystemEntries(desktop).Order()));
    }

    [Fact]
    public void IsLink_is_true_only_for_junctions_and_symlinks()
    {
        Directory.CreateDirectory(P("dir"));
        string file = File_("f.txt");
        string link = Link(P("dir"), P("junction"));

        Assert.True(JunctionHelper.IsLink(link));
        Assert.False(JunctionHelper.IsLink(P("dir")));
        Assert.False(JunctionHelper.IsLink(file));
        Assert.False(JunctionHelper.IsLink(P("missing")));
    }

    // ---- DeleteRacksShortcut ----------------------------------------------------------------

    [Theory]
    [InlineData("doc.pdf.lnk")]
    [InlineData("Site.URL")]
    public void DeleteRacksShortcut_deletes_a_shortcut_that_sits_in_the_named_folder(string name)
    {
        string path = File_("rack\\" + name, "shortcut");

        var r = _broker.DeleteRacksShortcut(path, P("rack"), "test shortcut");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(File.Exists(path));
        var record = Assert.Single(_audit.Events);
        Assert.Equal("DeleteRacksShortcut", record.Kind);
        Assert.Null(record.Destination);                           // the folder is an input, not a destination
    }

    [Fact]
    public void DeleteRacksShortcut_cannot_be_used_to_delete_other_files()
    {
        string doc = File_("rack\\report.docx", "precious");
        string noExt = File_("rack\\notes", "precious too");
        string tricky = File_("rack\\a.lnk.txt", "not a shortcut");

        foreach (var p in new[] { doc, noExt, tricky })
            Assert.Equal(OperationStatus.Rejected, _broker.DeleteRacksShortcut(p, P("rack"), "test not a shortcut").Status);

        Assert.True(File.Exists(doc));
        Assert.True(File.Exists(noExt));
        Assert.True(File.Exists(tricky));
    }

    [Fact]
    public void DeleteRacksShortcut_only_acts_directly_inside_the_named_folder()
    {
        string inSub = File_("rack\\sub\\x.lnk");
        string elsewhere = File_("other\\y.lnk");

        var sub = _broker.DeleteRacksShortcut(inSub, P("rack"), "test subfolder");
        var other = _broker.DeleteRacksShortcut(elsewhere, P("rack"), "test other folder");
        var viaDotDot = _broker.DeleteRacksShortcut(P("rack", "..", "other", "y.lnk"), P("rack"), "test dotdot");

        Assert.All(new[] { sub, other, viaDotDot }, r => Assert.Equal(OperationStatus.Rejected, r.Status));
        Assert.True(File.Exists(inSub));
        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void DeleteRacksShortcut_rejects_folders_links_missing_and_oversized_items()
    {
        Directory.CreateDirectory(P("rack", "folder.lnk"));
        Directory.CreateDirectory(P("target"));
        string link = Link(P("target"), P("rack", "link.lnk"));
        string big = P("rack", "big.lnk");
        using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(1024 * 1024 + 1);

        Assert.Equal(OperationStatus.Rejected, _broker.DeleteRacksShortcut(P("rack", "folder.lnk"), P("rack"), "folder").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteRacksShortcut(link, P("rack"), "link").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteRacksShortcut(P("rack", "gone.lnk"), P("rack"), "missing").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteRacksShortcut(big, P("rack"), "big").Status);

        Assert.True(Directory.Exists(P("rack", "folder.lnk")));
        Assert.True(Directory.Exists(P("target")));
        Assert.True(File.Exists(big));
    }

    // ---- MoveSameVolume ---------------------------------------------------------------------

    [Fact]
    public void MoveSameVolume_refuses_a_destination_on_another_volume_and_leaves_the_source()
    {
        string? tempRoot = Path.GetPathRoot(_root);
        var other = DriveInfo.GetDrives().FirstOrDefault(d =>
            d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.Name, tempRoot, StringComparison.OrdinalIgnoreCase));
        if (other == null) return;                                 // single-volume machine: nothing to assert (documented)

        string src = File_("a.txt", "stay here");
        string dest = Path.Combine(other.RootDirectory.FullName, "racks-test-never-created-" + Guid.NewGuid().ToString("N") + ".txt");

        var r = _broker.MoveSameVolume(src, dest, "test cross volume");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Equal("stay here", File.ReadAllText(src));
        Assert.False(File.Exists(dest));
        Assert.Single(_audit.Events);
    }
}
