using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Adversarial;

/// <summary>A second fixed drive that this process may also create a folder on (a test needs more than its existence).</summary>
public sealed class WritableSecondDriveFactAttribute : FactAttribute
{
    public WritableSecondDriveFactAttribute()
    {
        if (CreateScratchFolder() is not { } folder)
            Skip = "Needs a second fixed drive where the current user can create a folder.";
        else
            try { Directory.Delete(folder); } catch { }
    }

    public static string? CreateScratchFolder()
    {
        var other = SecondFixedDriveFactAttribute.Find();
        if (other == null) return null;
        string folder = Path.Combine(other.RootDirectory.FullName, "racks-adv-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(folder); return folder; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return null; }
    }
}

// P11: SafeMove and the broker's Move against hostile paths, on real NTFS. Folders that must count as
// "protected" are temp folders declared through SafeMove.UseProtectedFoldersForTest (per thread), never the
// real Desktop or Documents. Every test asserts that the source (and any pre-existing destination) is intact
// after a refusal, not only the verdict.
[Trait("Category", "Adversarial")]
public sealed class SafeMoveAdversarialTests : AdversarialTree
{
    private readonly RecordingAuditSink _audit = new();
    private FileOperationBroker Broker => new(_audit);

    private string ProtectedFolder(string name = "protected-folder-with-a-long-name")
    {
        string dir = Dir(name);
        Write(Path.Combine(dir, "precious.txt"), "precious");
        return dir;
    }

    private void AssertRefusedAndIntact(FileOperationResult r, string protectedDir)
    {
        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(protectedDir, "precious.txt")));
        Assert.True(Directory.Exists(protectedDir));
    }

    // ---- protected-root aliases ------------------------------------------------------------

    [Fact]
    public void A_case_variant_of_a_protected_folder_is_still_protected()
    {
        string prot = ProtectedFolder();
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        foreach (var spelling in new[] { prot.ToUpperInvariant(), prot.ToLowerInvariant(), prot + @"\", prot + @"\." })
        {
            Assert.True(SafeMove.IsProtectedPath(spelling, out _), spelling);
            AssertRefusedAndIntact(Broker.Move(spelling, P("moved"), "test case variant"), prot);
            AssertRefusedAndIntact(Broker.DeleteTree(spelling, "test case variant"), prot);
        }
        Assert.False(Directory.Exists(P("moved")));
    }

    [Fact]
    public void A_dot_dot_spelling_that_resolves_to_a_protected_folder_is_still_protected()
    {
        string prot = ProtectedFolder();
        Dir("protected-folder-with-a-long-name", "sub");
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        // The broker's path policy already refuses ".." segments; the primitive must not depend on that.
        Assert.True(SafeMove.IsProtectedPath(prot + @"\sub\..", out _));
        var verdict = SafeMove.Check(prot + @"\sub\..", P("moved"), out string reason);
        Assert.Equal(SafeMove.Result.Rejected, verdict);
        Assert.Contains("refusing to move", reason);
        Assert.Equal(OperationStatus.Rejected, Broker.Move(prot + @"\sub\..", P("moved"), "test dot dot").Status);
    }

    [Fact]
    public void The_extended_length_spelling_of_a_protected_folder_is_still_protected()
    {
        string prot = ProtectedFolder();
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);
        string extended = @"\\?\" + prot;

        Assert.True(SafeMove.IsProtectedPath(extended, out _));
        AssertRefusedAndIntact(Broker.Move(extended, P("moved"), "test extended length"), prot);
        AssertRefusedAndIntact(Broker.DeleteTree(extended, "test extended length"), prot);
        AssertRefusedAndIntact(Broker.DeleteToRecycleBin(extended, "test extended length"), prot);
        Assert.False(Directory.Exists(P("moved")));
    }

    [ShortNameFact]
    public void The_8dot3_short_name_of_a_protected_folder_is_still_protected()
    {
        string prot = ProtectedFolder();
        string shortName = ShortNameFactAttribute.ShortPath(prot);
        Assert.NotEqual(prot, shortName, StringComparer.OrdinalIgnoreCase);
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        Assert.True(SafeMove.IsProtectedPath(shortName, out _));
        AssertRefusedAndIntact(Broker.Move(shortName, P("moved"), "test 8.3"), prot);
        AssertRefusedAndIntact(Broker.DeleteTree(shortName, "test 8.3"), prot);
        Assert.False(Directory.Exists(P("moved")));
    }

    [Fact]
    public void A_junction_to_a_protected_folder_and_a_chain_of_junctions_are_protected()
    {
        string prot = ProtectedFolder();
        string one = Link(prot, P("hop-one"));
        string two = Link(one, P("hop-two"));
        string three = Link(two, P("hop-three"));
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        foreach (var alias in new[] { one, two, three })
        {
            Assert.True(SafeMove.IsProtectedPath(alias, out _), alias);
            AssertRefusedAndIntact(Broker.Move(alias, P("moved"), "test junction chain"), prot);
            AssertRefusedAndIntact(Broker.DeleteTree(alias, "test junction chain"), prot);
            AssertRefusedAndIntact(Broker.DeleteToRecycleBin(alias, "test junction chain"), prot);
        }
        Assert.True(Directory.Exists(one) && Directory.Exists(two) && Directory.Exists(three), "the links themselves are untouched");
    }

    [Fact]
    public void A_folder_that_contains_a_protected_folder_is_refused_through_an_alias_too()
    {
        string prot = ProtectedFolder();
        string alias = Link(Root, Path.Combine(Path.GetTempPath(), "racks-adv-alias-" + Guid.NewGuid().ToString("N")));
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        var viaAlias = Broker.DeleteTree(alias, "test ancestor via alias");

        Assert.Equal(OperationStatus.Rejected, viaAlias.Status);
        Assert.Contains("contains", viaAlias.Reason);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(prot, "precious.txt")));
    }

    [Fact]
    public void Something_inside_a_protected_folder_is_not_itself_protected()
    {
        // Only the folder (and, for moves and deletes, its ancestors) is protected: the user's files inside it
        // are what a rack is for.
        string prot = ProtectedFolder();
        string inner = Write(Path.Combine(prot, "report.docx"), "report");
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        var r = Broker.Move(inner, P("moved.docx"), "test child of protected");

        Assert.True(r.Succeeded, r.Reason);
        Assert.Equal("report", File.ReadAllText(P("moved.docx")));
    }

    // ---- how source and destination relate -------------------------------------------------

    [Fact]
    public void A_folder_cannot_be_moved_into_itself_directly_or_through_a_junction_alias()
    {
        Write(P("src", "a.txt"), "a");
        Dir("src", "sub");
        string alias = Link(P("src"), P("alias-of-src"));
        var before = Snapshot(P("src"));

        foreach (var dest in new[] { P("src", "sub", "new"), P("src", "inside"), Path.Combine(alias, "via-alias"), P("src") })
        {
            // Through the alias the preflight cannot see the overlap (the destination does not exist yet, so its
            // path is not resolved through the junction) and it is the operating system that refuses, which the
            // broker reports as Failed. What matters is that nothing was moved.
            var r = Broker.Move(P("src"), dest, "test into itself");
            Assert.False(r.Completed, dest);
            Assert.Contains(r.Status, new[] { OperationStatus.Rejected, OperationStatus.Skipped, OperationStatus.Failed });
        }

        Assert.Equal(before, Snapshot(P("src")));
        Assert.False(Directory.Exists(P("src", "inside")));
    }

    [Fact]
    public void Moving_an_item_onto_its_own_parent_is_a_name_collision_and_changes_nothing()
    {
        Write(P("parent", "child.txt"), "child");
        var before = Snapshot(P("parent"));

        var r = Broker.Move(P("parent", "child.txt"), P("parent"), "test source inside destination");

        Assert.False(r.Completed);
        Assert.Equal(before, Snapshot(P("parent")));
    }

    [Fact]
    public void A_destination_whose_parent_is_missing_is_rejected_and_the_source_stays()
    {
        string src = Write(P("a.txt"), "keep");

        var viaBroker = Broker.Move(src, P("no-such-folder", "a.txt"), "test missing parent");
        var viaPrimitive = SafeMove.TryMove(src, P("no-such-folder", "deeper", "a.txt"), out string reason);

        Assert.Equal(OperationStatus.Rejected, viaBroker.Status);
        Assert.Contains("doesn't exist", viaBroker.Reason);
        Assert.Equal(SafeMove.Result.Rejected, viaPrimitive);
        Assert.NotEmpty(reason);
        Assert.Equal("keep", File.ReadAllText(src));
        Assert.False(Directory.Exists(P("no-such-folder")), "a move never creates the destination's folders");
    }

    [Fact]
    public void An_existing_destination_is_never_overwritten_whatever_the_kinds_and_the_case()
    {
        Write(P("dest", "File.TXT"), "dest file");
        Write(P("dest", "Folder", "inner.txt"), "dest inner");
        Write(P("src", "file.txt"), "src file");
        Write(P("src", "folder", "inner.txt"), "src inner");
        Write(P("src", "FolderAsFile.txt"), "src");
        Dir("dest", "FolderAsFile.txt");

        var fileOnFile = Broker.Move(P("src", "file.txt"), P("dest", "file.txt"), "file onto file, other case");
        var folderOnFolder = Broker.Move(P("src", "folder"), P("dest", "FOLDER"), "folder onto folder, other case");
        var fileOnFolder = Broker.Move(P("src", "FolderAsFile.txt"), P("dest", "FolderAsFile.txt"), "file onto folder");

        Assert.All(new[] { fileOnFile, folderOnFolder, fileOnFolder }, r => Assert.Equal(OperationStatus.Skipped, r.Status));
        Assert.Equal("dest file", File.ReadAllText(P("dest", "File.TXT")));
        Assert.Equal("dest inner", File.ReadAllText(P("dest", "Folder", "inner.txt")));
        Assert.Equal("src file", File.ReadAllText(P("src", "file.txt")));
        Assert.Equal("src inner", File.ReadAllText(P("src", "folder", "inner.txt")));
        Assert.True(Directory.Exists(P("dest", "FolderAsFile.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(P("dest", "FolderAsFile.txt")));
    }

    [Fact]
    public void A_destination_with_a_trailing_dot_or_space_never_overwrites_the_name_it_collapses_to()
    {
        // .NET drops trailing dots and spaces from a plain path, so "x." and "x " both mean "x".
        Write(P("dest", "x"), "existing");
        string src = Write(P("src.txt"), "mine");

        var dot = Broker.Move(src, P("dest", "x."), "test trailing dot");
        var space = Broker.Move(src, P("dest", "x "), "test trailing space");

        Assert.False(dot.Completed);
        Assert.False(space.Completed);
        Assert.Equal("existing", File.ReadAllText(P("dest", "x")));
        Assert.Equal("mine", File.ReadAllText(src));
    }

    [Fact]
    public void Known_gap_a_destination_named_NUL_swallows_the_file()
    {
        // FINDING P11-F2 (data loss, low likelihood): PathPolicy.ValidateFullPath checks the destination path
        // but not its leaf for a reserved device name, and File.Move to "dest\NUL" writes the file into the NUL
        // device: the source is gone and nothing exists at the destination. The broker does say Failed ("can't
        // be found"), but the bytes are lost. Only NUL does this on this Windows (CON, COM1... behave as plain
        // names here). A leaf comes from an existing item's own name, so it needs a file that really is called
        // NUL (made through the \\?\ spelling); Rename and typed names are already protected by
        // ValidateLeafName. Pinned so the fix is deliberate: once the broker refuses it, flip this to
        // "Rejected and the source is still there".
        string src = Write(P("src.txt"), "gone for good");
        Dir("dest");

        var r = Broker.Move(src, P("dest", "NUL"), "test NUL");

        Assert.NotEqual(OperationStatus.Succeeded, r.Status);
        Assert.False(File.Exists(src));
        Assert.False(File.Exists(P("dest", "NUL")));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    [InlineData("nul.txt")]
    public void A_reserved_device_name_as_the_destination_never_swallows_the_source(string deviceName)
    {
        string src = Write(P("src.txt"), "must not vanish");
        Dir("dest");

        var r = Broker.Move(src, P("dest", deviceName), "test device name");

        // Whatever the broker answers, the file's bytes must still be reachable: at the source (refused)
        // or under the destination name (moved), never poured into a device.
        bool atSource = File.Exists(src) && File.ReadAllText(src) == "must not vanish";
        bool atDest = File.Exists(P("dest", deviceName)) && File.ReadAllText(P("dest", deviceName)) == "must not vanish";
        Assert.True(atSource || atDest, $"status {r.Status}: {r.Reason}");
        if (r.Status == OperationStatus.Succeeded) Assert.True(atDest);
    }

    // ---- names and lengths -----------------------------------------------------------------

    [Theory]
    [InlineData("[brackets] and 'quotes'")]
    [InlineData("100% & more")]
    [InlineData("emoji \U0001F4C1 \U0001F600")]
    [InlineData("caf\u00e9 \u65e5\u672c\u8a9e")]
    [InlineData("a;b,c=d!e#f$g(h)i{j}k~l+m")]
    public void Files_and_folders_with_symbols_in_their_names_move_with_their_content(string name)
    {
        Write(P("src", name + ".txt"), "file " + name);
        Write(P("src", name, "inner.txt"), "folder " + name);
        Dir("dest");

        var file = Broker.Move(P("src", name + ".txt"), P("dest", name + ".txt"), "test symbols file");
        var folder = Broker.Move(P("src", name), P("dest", name), "test symbols folder");

        Assert.True(file.Succeeded, file.Reason);
        Assert.True(folder.Succeeded, folder.Reason);
        Assert.Equal("file " + name, File.ReadAllText(P("dest", name + ".txt")));
        Assert.Equal("folder " + name, File.ReadAllText(P("dest", name, "inner.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(P("src")));
    }

    [Fact]
    public void A_file_and_a_folder_tree_beyond_260_characters_move_completely()
    {
        string longName = new('n', 100);
        string srcDir = P("src", longName, longName, longName);
        string destParent = Dir("dest", longName, longName, longName);
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "deep.txt"), "deep");
        File.WriteAllText(Path.Combine(srcDir, longName + ".txt"), "long file name");
        Assert.True(srcDir.Length > 260 && destParent.Length > 260, "test setup: the paths must be longer than MAX_PATH");

        var file = Broker.Move(Path.Combine(srcDir, "deep.txt"), Path.Combine(destParent, "deep.txt"), "test long file");
        var folder = Broker.Move(P("src", longName), Path.Combine(destParent, "moved-tree"), "test long folder");

        Assert.True(file.Succeeded, file.Reason);
        Assert.True(folder.Succeeded, folder.Reason);
        Assert.Equal("deep", File.ReadAllText(Path.Combine(destParent, "deep.txt")));
        Assert.Equal("long file name", File.ReadAllText(Path.Combine(destParent, "moved-tree", longName, longName, longName + ".txt")));
        Assert.False(Directory.Exists(P("src", longName)));
    }

    // ---- the copy + delete fallback (the cross-volume path) ---------------------------------

    [Fact]
    public void A_failed_folder_copy_removes_what_it_created_and_leaves_the_whole_source()
    {
        Write(P("src", "a.txt"), "a");
        Write(P("src", "sub", "b.txt"), "b");
        string locked = Write(P("src", "sub", "deeper", "locked.txt"), "locked");
        var before = Snapshot(P("src"));

        SafeMove.Result result;
        string reason;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))   // a copy cannot read it
            result = SafeMove.CopyThenDelete(P("src"), P("copy"), srcIsDir: true, out reason);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Contains("failed", reason);
        Assert.False(Directory.Exists(P("copy")), "the partial copy must be removed");
        Assert.Equal(before, Snapshot(P("src")));
    }

    [Fact]
    public void A_folder_copy_whose_original_cannot_be_fully_deleted_keeps_the_complete_copy_and_says_so()
    {
        Write(P("src", "free.txt"), "free");
        string held = Write(P("src", "sub", "held.txt"), "held");

        SafeMove.Result result;
        string reason;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))   // readable (copy works), not deletable
            result = SafeMove.CopyThenDelete(P("src"), P("copy"), srcIsDir: true, out reason);

        Assert.Equal(SafeMove.Result.Moved, result);
        Assert.Contains("couldn't fully delete", reason);
        Assert.Equal("free", File.ReadAllText(P("copy", "free.txt")));
        Assert.Equal("held", File.ReadAllText(P("copy", "sub", "held.txt")));
        Assert.Equal("held", File.ReadAllText(held));
    }

    [Fact]
    public void A_link_inside_a_copied_folder_is_dropped_not_followed_and_its_target_survives()
    {
        // A cross-volume move cannot recreate a junction, so the copy skips it and the original walk unlinks it.
        // Pinned here so a change is deliberate: the pointer is lost, the data it pointed at never is.
        string target = Write(P("outside", "keep.txt"), "outside");
        Write(P("src", "own.txt"), "own");
        Link(P("outside"), P("src", "pointer"));

        var result = SafeMove.CopyThenDelete(P("src"), P("copy"), srcIsDir: true, out _);

        Assert.Equal(SafeMove.Result.Moved, result);
        Assert.Equal("own", File.ReadAllText(P("copy", "own.txt")));
        Assert.False(Directory.Exists(P("copy", "pointer")));
        Assert.False(Directory.Exists(P("src")));
        Assert.Equal("outside", File.ReadAllText(target));
    }

    [Fact]
    public void A_tree_nested_deeper_than_the_copy_backstop_is_refused_whole_and_untouched()
    {
        // CopyDirectory stops at depth 64 (a junction-loop backstop). A real tree that deep is refused by the
        // copy fallback: safe (nothing is copied or deleted) but a documented limit of cross-volume moves.
        string deep = P("src");
        for (int i = 0; i < 70; i++) deep = Path.Combine(deep, "d");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "bottom.txt"), "bottom");

        var result = SafeMove.CopyThenDelete(P("src"), P("copy"), srcIsDir: true, out string reason);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Contains("too deep", reason);
        Assert.False(Directory.Exists(P("copy")));
        Assert.Equal("bottom", File.ReadAllText(Path.Combine(deep, "bottom.txt")));
    }

    [WritableSecondDriveFact]
    public void A_real_cross_volume_file_move_arrives_complete_and_leaves_no_original()
    {
        string scratch = WritableSecondDriveFactAttribute.CreateScratchFolder()!;
        try
        {
            string file = Write(P("single [1] & 'c'.txt"), "single");

            var r = Broker.Move(file, Path.Combine(scratch, "single [1] & 'c'.txt"), "test real cross-volume file");

            Assert.True(r.Succeeded, r.Reason);
            Assert.Equal("single", File.ReadAllText(Path.Combine(scratch, "single [1] & 'c'.txt")));
            Assert.False(File.Exists(file));
        }
        finally { try { Directory.Delete(scratch, recursive: true); } catch { } }
    }

    [WritableSecondDriveFact]
    public void A_real_cross_volume_file_move_that_cannot_complete_keeps_the_source_and_leaves_no_copy()
    {
        string scratch = WritableSecondDriveFactAttribute.CreateScratchFolder()!;
        try
        {
            string readable = Write(P("held.txt"), "held");      // others may read it, nobody may delete or move it
            string exclusive = Write(P("locked.txt"), "locked");  // nobody may even read it
            FileOperationResult heldResult, lockedResult;
            using (new FileStream(readable, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (new FileStream(exclusive, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                heldResult = Broker.Move(readable, Path.Combine(scratch, "held.txt"), "test cross-volume held");
                lockedResult = Broker.Move(exclusive, Path.Combine(scratch, "locked.txt"), "test cross-volume locked");
            }

            Assert.Equal(OperationStatus.Failed, heldResult.Status);
            Assert.Equal(OperationStatus.Failed, lockedResult.Status);
            Assert.Equal("held", File.ReadAllText(readable));
            Assert.Equal("locked", File.ReadAllText(exclusive));
            Assert.Empty(Directory.GetFileSystemEntries(scratch));
        }
        finally { try { Directory.Delete(scratch, recursive: true); } catch { } }
    }

    [WritableSecondDriveFact]
    public void Known_gap_a_folder_move_to_another_volume_is_refused_whole_and_untouched()
    {
        // FINDING P11-F1 (functional, no data loss): .NET's Directory.Move throws an IOException whose HResult is
        // not ERROR_NOT_SAME_DEVICE ("Source and destination path must have identical roots"), so
        // SafeMove.IsCrossVolumeError never matches and the copy-then-delete fallback is never reached for a
        // folder. The user's drop is refused ("Failed to move"), and nothing is moved or created. File moves
        // across drives work (the OS copies). Pinned so enabling the fallback is deliberate: it needs the
        // "delete only what this call created" cleanup (P11b) first. Once enabled, this becomes a Succeeded test.
        string scratch = WritableSecondDriveFactAttribute.CreateScratchFolder()!;
        try
        {
            Write(P("src", "a.txt"), "a");
            Write(P("src", "sub", "b.txt"), "b");
            var before = Snapshot(P("src"));

            var r = Broker.Move(P("src"), Path.Combine(scratch, "src"), "test real cross-volume folder");

            Assert.Equal(OperationStatus.Failed, r.Status);
            Assert.Contains("identical roots", r.Reason);
            Assert.Equal(before, Snapshot(P("src")));
            Assert.Empty(Directory.GetFileSystemEntries(scratch));
        }
        finally { try { Directory.Delete(scratch, recursive: true); } catch { } }
    }
}
