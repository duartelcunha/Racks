using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.FileSystem;

// Real temp directories, real junctions. Anything that touches a protected root is done through a junction
// that points at it, so a regression could only rename or delete the link, never the real folder.
[Collection("AuditLog")]   // one test swaps the process-wide audit sink with AuditLog.Use
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

    // A Ctrl+dropped reference is a junction inside a rack. Renaming it renames the LINK: it must work whatever
    // the link points at (a protected folder, another volume) and never touch the target.
    [Fact]
    public void Rename_of_a_junction_that_points_at_a_protected_folder_renames_the_link_only()
    {
        string link = JunctionToDesktop();                                   // would canonicalize to the Desktop
        string before = string.Join("|", Directory.EnumerateFileSystemEntries(Desktop).Order());

        var r = _broker.Rename(link, "Desktop shortcut", "test link rename");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(link));
        Assert.True(JunctionHelper.IsReparsePoint(P("Desktop shortcut")));
        _links.Add(P("Desktop shortcut"));                                   // so Dispose unlinks it under its new name
        Assert.True(Directory.Exists(Desktop));
        Assert.Equal(before, string.Join("|", Directory.EnumerateFileSystemEntries(Desktop).Order()));
    }

    [Fact]
    public void Rename_of_a_junction_to_Documents_works()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.True(Directory.Exists(docs), "test setup: no Documents folder");
        string link = Link(docs, P("docs-link"));

        var r = _broker.Rename(link, "My documents", "test link rename");

        Assert.True(r.Succeeded, r.Reason);
        _links.Add(P("My documents"));
        Assert.True(Directory.Exists(docs));
    }

    [SecondFixedDriveFact]
    public void Rename_of_a_junction_whose_target_is_on_another_volume_is_not_a_volume_error()
    {
        var other = SecondFixedDriveFactAttribute.Find()!;

        string link = Link(other.RootDirectory.FullName, P("other-volume"));
        var r = _broker.Rename(link, "Elsewhere", "test cross-volume link");

        Assert.True(r.Succeeded, r.Reason);
        _links.Add(P("Elsewhere"));
        Assert.True(Directory.Exists(other.RootDirectory.FullName));
    }

    [SendToFolderFact]
    public void Rename_of_the_real_protected_folder_is_still_rejected()
    {
        // SendTo is on SafeMove's protected list and, unlike the Desktop, harmless if a regression ever renamed it;
        // the finally block puts it back anyway.
        string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        Assert.True(SafeMove.IsProtectedLink(sendTo, out _));
        string renamedTo = Path.Combine(Path.GetDirectoryName(sendTo)!, "SendTo-renamed-by-test");
        try
        {
            var r = _broker.Rename(sendTo, "SendTo-renamed-by-test", "test protected");

            Assert.Equal(OperationStatus.Rejected, r.Status);
            Assert.True(Directory.Exists(sendTo));
            Assert.False(Directory.Exists(renamedTo));
        }
        finally
        {
            if (Directory.Exists(renamedTo) && !Directory.Exists(sendTo)) Directory.Move(renamedTo, sendTo);
        }
    }

    [Fact]
    public void Protected_link_check_looks_at_the_item_itself()
    {
        string link = JunctionToDesktop();
        Assert.True(SafeMove.IsProtectedPath(link, out _));                  // follows the junction: the Desktop
        Assert.False(SafeMove.IsProtectedLink(link, out _));                 // the link itself is just a link
        Assert.True(SafeMove.IsProtectedLink(Desktop, out _));
        Assert.True(SafeMove.IsProtectedLink(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), out _));
    }

    // ---- case-only rename ------------------------------------------------------------------

    [Fact]
    public void NTFS_renames_a_case_only_change_directly_for_files_and_folders()
    {
        string f = File_("lower.txt");
        Directory.CreateDirectory(P("lowerdir"));

        File.Move(f, P("Lower.txt"));
        Directory.Move(P("lowerdir"), P("Lowerdir"));

        var names = Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).ToList();
        Assert.Contains("Lower.txt", names);
        Assert.Contains("Lowerdir", names);
    }

    [Fact]
    public void A_case_only_rename_is_one_direct_move_not_two()
    {
        string f = File_("photo.jpg");
        Directory.CreateDirectory(P("docs"));
        var steps = new List<(string From, string To)>();
        _broker.RenameStepHook = (src, dst) => steps.Add((src, dst));

        var rf = _broker.Rename(f, "Photo.jpg", "case file");
        var rd = _broker.Rename(P("docs"), "Docs", "case folder");

        Assert.True(rf.Succeeded, rf.Reason);
        Assert.True(rd.Succeeded, rd.Reason);
        Assert.Equal(2, steps.Count);
        Assert.DoesNotContain(steps, s => s.To.Contains(".racks-rename-"));
    }

    [Fact]
    public void A_case_only_rename_falls_back_to_a_temporary_name_that_keeps_the_extension_and_still_succeeds()
    {
        string f = File_("photo.jpg");
        var steps = new List<(string From, string To)>();
        _broker.RenameStepHook = (src, dst) =>
        {
            steps.Add((src, dst));
            if (steps.Count == 1) throw new IOException("direct case rename refused (simulated)");
        };

        var r = _broker.Rename(f, "Photo.jpg", "case fallback");

        Assert.True(r.Succeeded, r.Reason);
        Assert.Equal(3, steps.Count);
        Assert.EndsWith(".jpg", steps[1].To);                                // the temp name keeps the extension
        Assert.StartsWith(".racks-rename-", Path.GetFileName(steps[1].To));
        Assert.Contains("Photo.jpg", Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName));
        Assert.Equal("data", File.ReadAllText(P("Photo.jpg")));
    }

    [Fact]
    public void A_failed_second_step_restores_the_original_name_and_says_so()
    {
        string f = File_("photo.jpg", "precious");
        _broker.RenameStepHook = (src, dst) =>
        {
            bool direct = src == f;
            bool secondStep = Path.GetFileName(src).StartsWith(".racks-rename-") && dst == P("Photo.jpg");
            if (direct && dst == P("Photo.jpg")) throw new IOException("direct refused (simulated)");
            if (secondStep) throw new IOException("sharing violation (simulated)");
        };

        var r = _broker.Rename(f, "Photo.jpg", "case fallback fails");

        Assert.Equal(OperationStatus.Failed, r.Status);
        Assert.Contains("sharing violation", r.Reason);
        Assert.Contains("restored", r.Reason);
        Assert.Equal("precious", File.ReadAllText(f));
        Assert.Equal(new[] { "photo.jpg" }, Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void When_the_restore_fails_too_the_item_is_kept_under_a_temp_name_with_its_extension_and_the_reason_names_it()
    {
        string f = File_("photo.jpg", "precious");
        _broker.RenameStepHook = (src, dst) =>
        {
            if (src == f && dst == P("Photo.jpg")) throw new IOException("direct refused (simulated)");
            if (Path.GetFileName(src).StartsWith(".racks-rename-")) throw new IOException("locked (simulated)");
        };

        var r = _broker.Rename(f, "Photo.jpg", "case fallback and restore fail");

        Assert.Equal(OperationStatus.Failed, r.Status);
        string temp = Assert.Single(Directory.EnumerateFileSystemEntries(_root));
        Assert.EndsWith(".jpg", temp);
        Assert.StartsWith(".racks-rename-", Path.GetFileName(temp));
        Assert.Equal("precious", File.ReadAllText(temp));                    // not lost
        Assert.Contains(Path.GetFileName(temp), r.Reason);                   // and the user is told where it is
        Assert.Equal(temp, r.Destination);
    }

    [Fact]
    public void A_locked_file_makes_a_case_only_rename_fail_honestly_without_losing_it()
    {
        string f = File_("locked.txt", "keep");
        using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);

        var r = _broker.Rename(f, "Locked.txt", "case locked");

        Assert.Equal(OperationStatus.Failed, r.Status);
        Assert.NotEmpty(r.Reason);
        Assert.Equal(new[] { "locked.txt" }, Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).ToArray());
    }

    // ---- PartiallyCompleted mapping --------------------------------------------------------

    [Fact]
    public void A_move_that_leaves_the_original_behind_is_PartiallyCompleted_with_the_reason()
    {
        string src = File_("src.txt"), dest = File_("dest.txt");
        var ctx = OperationContext.Create("test partial");

        var partial = FileOperationBroker.ConfirmMoved(ctx, src, dest, "Copied \"src.txt\" but couldn't fully delete the original.");
        var partialNoText = FileOperationBroker.ConfirmMoved(ctx, src, dest, "");
        var clean = FileOperationBroker.ConfirmMoved(ctx, P("gone.txt"), dest, "");
        var lost = FileOperationBroker.ConfirmMoved(ctx, P("gone.txt"), P("never-arrived.txt"), "");

        Assert.Equal(OperationStatus.PartiallyCompleted, partial.Status);
        Assert.Contains("couldn't fully delete", partial.Reason);
        Assert.True(partial.Completed);
        Assert.False(partial.Succeeded);
        Assert.Equal(OperationStatus.PartiallyCompleted, partialNoText.Status);
        Assert.NotEmpty(partialNoText.Reason);
        Assert.Equal(OperationStatus.Succeeded, clean.Status);
        Assert.Equal(OperationStatus.Failed, lost.Status);
        Assert.False(lost.Completed);
    }

    // ---- Recycle Bin fails closed ----------------------------------------------------------

    [Theory]
    [InlineData("C:\\Users\\me\\a.txt", DriveType.Fixed, true)]
    [InlineData("D:\\data\\a.txt", DriveType.Removable, false)]
    [InlineData("E:\\a.txt", DriveType.Network, false)]
    [InlineData("F:\\a.txt", DriveType.CDRom, false)]
    [InlineData("G:\\a.txt", DriveType.Unknown, false)]
    [InlineData("\\\\server\\share\\a.txt", DriveType.Fixed, false)]          // UNC is never recycled
    [InlineData("\\\\server\\share\\a.txt", DriveType.Network, false)]
    [InlineData("", DriveType.Fixed, false)]
    public void Only_fixed_local_locations_count_as_recyclable(string path, DriveType type, bool expected)
        => Assert.Equal(expected, SafeDelete.IsRecyclableLocation(path, type));

    [Fact]
    public void An_unknown_drive_type_is_not_recyclable()
        => Assert.False(SafeDelete.IsRecyclableLocation("C:\\a.txt", null));

    [Fact]
    public void A_file_in_the_temp_folder_is_on_a_recyclable_location_or_the_machine_has_no_fixed_temp_drive()
    {
        string f = File_("probe.txt");
        bool fixedDrive = new DriveInfo(Path.GetPathRoot(f)!).DriveType == DriveType.Fixed;
        Assert.Equal(fixedDrive, SafeDelete.CanRecycle(f));
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
