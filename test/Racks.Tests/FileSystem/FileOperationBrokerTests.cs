using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.FileSystem;

// Real temp directories, real junctions. Anything that touches a protected root is done through a junction
// that points at it, so a regression could only rename or delete the link, never the real folder.
public sealed class FileOperationBrokerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-broker-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingAuditSink _audit = new();
    private readonly FileOperationBroker _broker;
    private readonly List<string> _links = [];

    public FileOperationBrokerTests()
    {
        Directory.CreateDirectory(_root);
        _broker = new FileOperationBroker(_audit);
    }

    public void Dispose()
    {
        // Unlink the junctions this test made first (never recurse into their targets), then the tree.
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

    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    private string JunctionToDesktop()
    {
        Assert.True(Directory.Exists(Desktop), "test setup: this machine has no Desktop folder");
        return Link(Desktop, P("desktop-link"));
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
        Assert.False(string.IsNullOrWhiteSpace(record.OperationId));
        Assert.Equal(kind.ToString(), record.Kind);
        Assert.Equal(status.ToString(), record.Status);
        Assert.Equal(status, result.Status);
        Assert.Equal(reason, record.Reason);
        Assert.Equal(result.Source, record.Source);
        Assert.Equal(result.Destination, record.Destination);
        Assert.Equal(DateTimeKind.Utc, record.TimestampUtc.Kind);
    }

    // ---- Rename ----------------------------------------------------------------------------

    [Fact]
    public void Rename_renames_a_file_in_place()
    {
        string src = File_("a.txt");
        var r = _broker.Rename(src, "b.txt", "test rename");

        Assert.True(r.Succeeded);
        Assert.False(File.Exists(src));
        Assert.Equal("data", File.ReadAllText(P("b.txt")));
        Assert.Equal(P("b.txt"), r.Destination);
        AssertOneRecord(r, FileOperationKind.Rename, OperationStatus.Succeeded, "test rename");
    }

    [Fact]
    public void Rename_renames_a_folder_with_its_content()
    {
        File_("Old\\inner.txt", "inner");
        var r = _broker.Rename(P("Old"), "New", "test rename folder");

        Assert.True(r.Succeeded);
        Assert.False(Directory.Exists(P("Old")));
        Assert.Equal("inner", File.ReadAllText(P("New", "inner.txt")));
    }

    [Fact]
    public void Rename_that_only_changes_case_works_for_files_and_folders()
    {
        string file = File_("photo.jpg");
        Directory.CreateDirectory(P("docs"));

        var f = _broker.Rename(file, "Photo.jpg", "case file");
        var d = _broker.Rename(P("docs"), "Docs", "case folder");

        Assert.True(f.Succeeded, f.Reason);
        Assert.True(d.Succeeded, d.Reason);
        var names = Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).ToList();
        Assert.Contains("Photo.jpg", names);
        Assert.Contains("Docs", names);
        Assert.DoesNotContain("photo.jpg", names);
        Assert.DoesNotContain("docs", names);
        Assert.Equal("data", File.ReadAllText(P("Photo.jpg")));
        Assert.DoesNotContain(names, n => n!.StartsWith(".racks-rename-"));
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        string src = File_("same.txt");
        var r = _broker.Rename(src, "same.txt", "test same");
        Assert.Equal(OperationStatus.Skipped, r.Status);
        Assert.Equal("data", File.ReadAllText(src));
    }

    [Fact]
    public void Rename_onto_an_existing_item_is_skipped_and_nothing_is_overwritten()
    {
        string a = File_("a.txt", "AAA");
        string b = File_("b.txt", "BBB");
        Directory.CreateDirectory(P("folder"));

        var onFile = _broker.Rename(a, "b.txt", "collide file");
        var onFolder = _broker.Rename(a, "folder", "collide folder");
        var otherCase = _broker.Rename(a, "B.TXT", "collide other case");

        Assert.Equal(OperationStatus.Skipped, onFile.Status);
        Assert.Equal(OperationStatus.Skipped, onFolder.Status);
        Assert.Equal(OperationStatus.Skipped, otherCase.Status);
        Assert.Equal("AAA", File.ReadAllText(a));
        Assert.Equal("BBB", File.ReadAllText(b));
        Assert.True(Directory.Exists(P("folder")));
    }

    [Theory]
    [InlineData("..\\moved.txt")]
    [InlineData("..\\..\\moved.txt")]
    [InlineData("sub\\moved.txt")]
    [InlineData("sub/moved.txt")]
    [InlineData("C:\\moved.txt")]
    [InlineData("C:moved.txt")]
    [InlineData("\\\\server\\share\\moved.txt")]
    [InlineData("..")]
    [InlineData("a:ads")]
    [InlineData("CON")]
    [InlineData("name.")]
    [InlineData("")]
    public void Rename_with_a_name_that_is_not_a_leaf_is_rejected_and_nothing_moves(string typed)
    {
        string src = File_("deep\\item.txt");
        Directory.CreateDirectory(P("deep", "sub"));
        var before = AllEntries();

        var r = _broker.Rename(src, typed, "inline rename");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.NotEmpty(r.Reason);
        Assert.Equal("data", File.ReadAllText(src));
        Assert.Equal(before, AllEntries());                                 // nothing appeared anywhere
        Assert.False(File.Exists(P("moved.txt")));
        Assert.False(File.Exists(P("deep", "moved.txt")));
        Assert.False(File.Exists(P("deep", "sub", "moved.txt")));
        AssertOneRecord(r, FileOperationKind.Rename, OperationStatus.Rejected, "inline rename");
    }

    [Fact]
    public void Rename_of_a_protected_path_is_rejected()
    {
        string link = JunctionToDesktop();                                   // canonicalizes to the Desktop

        var r = _broker.Rename(link, "Renamed", "test protected");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(link));
        Assert.False(Directory.Exists(P("Renamed")));
    }

    [Fact]
    public void Rename_of_a_missing_source_is_rejected()
    {
        var r = _broker.Rename(P("nope.txt"), "other.txt", "test missing");
        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.False(File.Exists(P("other.txt")));
    }

    [Fact]
    public void Rename_of_a_relative_or_root_path_is_rejected()
    {
        Assert.Equal(OperationStatus.Rejected, _broker.Rename("a.txt", "b.txt", "relative").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.Rename(Path.GetPathRoot(_root)!, "X", "root").Status);
    }

    [Fact]
    public void Rename_of_a_locked_file_fails_without_losing_it()
    {
        string src = File_("locked.txt");
        using var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);   // blocks the rename

        var r = _broker.Rename(src, "renamed.txt", "test locked");

        Assert.Equal(OperationStatus.Failed, r.Status);
        Assert.NotEmpty(r.Reason);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(P("renamed.txt")));
    }

    // ---- Move ------------------------------------------------------------------------------

    [Fact]
    public void Move_moves_a_file_and_a_folder()
    {
        string file = File_("src\\a.txt");
        File_("src\\Folder\\in.txt", "in");
        Directory.CreateDirectory(P("dest"));

        var f = _broker.Move(file, P("dest", "a.txt"), "test move file");
        var d = _broker.Move(P("src", "Folder"), P("dest", "Folder"), "test move folder");

        Assert.True(f.Succeeded);
        Assert.True(d.Succeeded);
        Assert.False(File.Exists(file));
        Assert.Equal("data", File.ReadAllText(P("dest", "a.txt")));
        Assert.Equal("in", File.ReadAllText(P("dest", "Folder", "in.txt")));
        Assert.Equal(2, _audit.Events.Count);
    }

    [Fact]
    public void Move_onto_an_existing_destination_is_skipped_and_both_survive()
    {
        string src = File_("src.txt", "SRC");
        string dest = File_("dest.txt", "DEST");

        var r = _broker.Move(src, dest, "test skip");

        Assert.Equal(OperationStatus.Skipped, r.Status);
        Assert.Equal("SRC", File.ReadAllText(src));
        Assert.Equal("DEST", File.ReadAllText(dest));
        AssertOneRecord(r, FileOperationKind.Move, OperationStatus.Skipped, "test skip");
    }

    [Fact]
    public void Move_of_a_protected_folder_is_rejected()
    {
        // Same shape as the existing SafeMove test: the real Desktop is only ever a source that must be refused.
        var r = _broker.Move(Desktop, P("target"), "test protected");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(Desktop));
        Assert.False(Directory.Exists(P("target")));
        AssertOneRecord(r, FileOperationKind.Move, OperationStatus.Rejected, "test protected");
    }

    [Fact]
    public void Move_through_a_junction_that_points_at_a_protected_root_is_rejected()
    {
        string link = JunctionToDesktop();

        var r = _broker.Move(link, P("moved-link"), "test junction alias");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(link));
        Assert.False(Directory.Exists(P("moved-link")));
    }

    [Fact]
    public void Move_through_the_extended_length_or_8_3_spelling_of_a_protected_root_is_rejected()
    {
        var aliases = new List<string> { @"\\?\" + Desktop };
        string shortForm = ShortPath(Desktop);
        if (!string.Equals(shortForm, Desktop, StringComparison.OrdinalIgnoreCase)) aliases.Add(shortForm);   // 8.3 can be disabled on a volume

        foreach (var alias in aliases)
        {
            var r = _broker.Move(alias, P("alias-target"), "test alias " + alias);
            Assert.Equal(OperationStatus.Rejected, r.Status);
            Assert.True(Directory.Exists(Desktop));
            Assert.False(Directory.Exists(P("alias-target")));
        }
    }

    [Fact]
    public void Move_of_a_folder_into_itself_is_rejected()
    {
        File_("outer\\x.txt");

        var r = _broker.Move(P("outer"), P("outer", "inner", "outer"), "test into itself");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(File.Exists(P("outer", "x.txt")));
    }

    [Fact]
    public void Move_to_a_destination_whose_folder_is_missing_is_rejected_and_the_source_stays()
    {
        string src = File_("a.txt");

        var r = _broker.Move(src, P("no-such-folder", "a.txt"), "test no parent");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(File.Exists(src));
        Assert.False(Directory.Exists(P("no-such-folder")));
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("C:x.txt")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    public void Move_with_a_path_that_is_not_a_full_path_is_rejected(string bad)
    {
        string src = File_("a.txt");
        Assert.Equal(OperationStatus.Rejected, _broker.Move(src, bad, "bad dest").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.Move(bad, P("b.txt"), "bad source").Status);
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void Move_of_a_locked_file_fails_and_the_file_stays_put()
    {
        string src = File_("locked.txt");
        using var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);

        var r = _broker.Move(src, P("elsewhere.txt"), "test locked");

        Assert.Equal(OperationStatus.Failed, r.Status);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(P("elsewhere.txt")));
        AssertOneRecord(r, FileOperationKind.Move, OperationStatus.Failed, "test locked");
    }

    [Fact]
    public void MoveSameVolume_moves_within_a_volume()
    {
        string src = File_("a.txt");
        Directory.CreateDirectory(P("d"));

        var r = _broker.MoveSameVolume(src, P("d", "a.txt"), "uninstall return");

        Assert.True(r.Succeeded, r.Reason);
        Assert.True(File.Exists(P("d", "a.txt")));
    }

    [Fact]
    public void Move_without_a_reason_is_rejected_but_still_audited()
    {
        string src = File_("a.txt");

        var r = _broker.Move(src, P("b.txt"), "  ");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(File.Exists(src));
        var record = Assert.Single(_audit.Events);
        Assert.Equal(r.OperationId, record.OperationId);
        Assert.False(string.IsNullOrWhiteSpace(record.Reason));
    }

    // ---- CreateDirectory -------------------------------------------------------------------

    [Fact]
    public void CreateDirectory_creates_a_folder_with_a_sanitized_name()
    {
        string name = PathPolicy.SanitizeLeafName("Docs/Reports: 2026?", "fallback");
        var r = _broker.CreateDirectory(P(name), "test create");

        Assert.True(r.Succeeded, r.Reason);
        Assert.True(Directory.Exists(P("Docs_Reports_ 2026_")));
        AssertOneRecord(r, FileOperationKind.CreateDirectory, OperationStatus.Succeeded, "test create");
    }

    [Theory]
    [InlineData("bad:name")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    [InlineData("a|b")]
    public void CreateDirectory_with_an_invalid_leaf_is_rejected(string leaf)
    {
        var r = _broker.CreateDirectory(Path.Combine(_root, leaf), "test invalid");
        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void CreateDirectory_cannot_climb_out_with_dot_dot()
    {
        string sneaky = _root + "\\inner\\..\\..\\" + "racks-escape-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(P("inner"));

        var r = _broker.CreateDirectory(sneaky, "test traversal");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.False(Directory.Exists(Path.GetFullPath(sneaky)));
    }

    [Fact]
    public void CreateDirectory_with_a_missing_parent_is_rejected_and_creates_nothing()
    {
        var r = _broker.CreateDirectory(P("missing", "child"), "test no parent");
        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.False(Directory.Exists(P("missing")));
    }

    [Fact]
    public void CreateDirectory_on_something_that_exists_does_not_touch_it()
    {
        Directory.CreateDirectory(P("there"));
        string file = File_("afile", "keep");

        Assert.Equal(OperationStatus.Skipped, _broker.CreateDirectory(P("there"), "exists").Status);
        Assert.Equal(OperationStatus.Rejected, _broker.CreateDirectory(file, "file in the way").Status);
        Assert.Equal("keep", File.ReadAllText(file));
    }

    // ---- Delete ----------------------------------------------------------------------------

    [Fact]
    public void DeleteTree_of_a_protected_path_is_rejected()
    {
        string link = JunctionToDesktop();

        var r = _broker.DeleteTree(link, "test protected");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(Desktop));
    }

    [Fact]
    public void DeleteToRecycleBin_of_a_protected_path_is_rejected()
    {
        string link = JunctionToDesktop();

        var r = _broker.DeleteToRecycleBin(link, "test protected");

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(Desktop));
    }

    [Fact]
    public void DeleteTree_does_not_follow_a_junction_and_leaves_the_target_alone()
    {
        string keep = File_("target\\keep.txt", "keep me");
        File_("tree\\own.txt", "mine");
        Link(P("target"), P("tree", "link"));

        var r = _broker.DeleteTree(P("tree"), "test junction");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(P("tree")));
        Assert.Equal("keep me", File.ReadAllText(keep));
        AssertOneRecord(r, FileOperationKind.DeleteTree, OperationStatus.Succeeded, "test junction");
    }

    [Fact]
    public void DeleteTree_that_cannot_delete_everything_is_partial_not_a_lie()
    {
        string locked = File_("tree\\locked.txt");
        File_("tree\\free.txt");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);

        var r = _broker.DeleteTree(P("tree"), "test partial");

        Assert.Equal(OperationStatus.PartiallyCompleted, r.Status);
        Assert.True(r.Completed);
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void DeleteTree_refuses_a_file_and_a_missing_path()
    {
        string file = File_("f.txt");
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteTree(file, "a file").Status);
        Assert.True(File.Exists(file));
        Assert.Equal(OperationStatus.Rejected, _broker.DeleteTree(P("gone"), "missing").Status);
    }

    [Fact]
    public void DeleteToRecycleBin_removes_the_file_or_fails_without_losing_it()
    {
        string file = File_("recycle-me.txt", "bye");

        var r = _broker.DeleteToRecycleBin(file, "test recycle");

        // A machine without a usable Recycle Bin (some CI images) reports Failed; it must never report
        // success while the file is still there, nor lose the file while reporting failure.
        if (r.Status == OperationStatus.Succeeded)
            Assert.False(File.Exists(file));
        else
        {
            Assert.Equal(OperationStatus.Failed, r.Status);
            Assert.NotEmpty(r.Reason);
            Assert.True(File.Exists(file));
        }
        Assert.Single(_audit.Events);
    }

    // ---- Audit -----------------------------------------------------------------------------

    [Fact]
    public void Every_call_writes_exactly_one_record_whatever_the_outcome()
    {
        string a = File_("a.txt");
        string locked = File_("locked.txt");
        File_("exists.txt");
        Directory.CreateDirectory(P("d"));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);

        var results = new[]
        {
            _broker.Move(a, P("d", "a.txt"), "r1"),                              // Succeeded
            _broker.Move(P("d", "a.txt"), P("exists.txt"), "r2"),                // Skipped
            _broker.Move(Desktop, P("x"), "r3"),                                 // Rejected
            _broker.Move(locked, P("d", "locked.txt"), "r4"),                    // Failed
            _broker.Rename(P("exists.txt"), "..\\bad", "r5"),                    // Rejected
            _broker.CreateDirectory(P("new"), "r6"),                             // Succeeded
            _broker.DeleteTree(P("new"), "r7"),                                  // Succeeded
        };

        var events = _audit.Events;
        Assert.Equal(results.Length, events.Count);
        Assert.Equal(results.Select(r => r.OperationId), events.Select(e => e.OperationId));
        Assert.Equal(results.Length, events.Select(e => e.OperationId).Distinct().Count());
        Assert.Equal(new[] { "Succeeded", "Skipped", "Rejected", "Failed", "Rejected", "Succeeded", "Succeeded" }, events.Select(e => e.Status));
        Assert.Equal(new[] { "Move", "Move", "Move", "Move", "Rename", "CreateDirectory", "DeleteTree" }, events.Select(e => e.Kind));
        Assert.Equal(new[] { "r1", "r2", "r3", "r4", "r5", "r6", "r7" }, events.Select(e => e.Reason));
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.Source)));
        Assert.Equal(P("d", "a.txt"), events[0].Destination);
    }

    [Fact]
    public void A_failing_audit_sink_never_changes_or_throws_the_operation()
    {
        var broker = new FileOperationBroker(new ThrowingAuditSink());
        string src = File_("a.txt");

        var r = broker.Move(src, P("b.txt"), "test sink failure");

        Assert.True(r.Succeeded);
        Assert.True(File.Exists(P("b.txt")));
        Assert.Equal(OperationStatus.Rejected, broker.Rename(P("b.txt"), "x\\y", "test sink failure 2").Status);
    }

    [Fact]
    public void The_default_broker_writes_to_whatever_sink_AuditLog_holds()
    {
        var sink = new RecordingAuditSink();
        string reason = "default-broker-" + Guid.NewGuid().ToString("N");
        using (Racks.Infrastructure.Logging.AuditLog.Use(sink))
            FileOperationBroker.Default.CreateDirectory(P("via-default"), reason);

        Assert.True(Directory.Exists(P("via-default")));
        // Other tests may use Default at the same time; look only at this test's record.
        Assert.Single(sink.Events, e => e.Reason == reason);
    }

    private List<string> AllEntries() =>
        Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToList();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    private static string ShortPath(string path)
    {
        var sb = new StringBuilder(1024);
        uint n = GetShortPathNameW(path, sb, (uint)sb.Capacity);
        return n == 0 || n > sb.Capacity ? path : sb.ToString();
    }
}
