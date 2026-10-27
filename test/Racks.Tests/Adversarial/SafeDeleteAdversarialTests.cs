using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Adversarial;

// P11 "Windows regression" pass for SafeDelete: the tree walker and the Recycle Bin call, on real NTFS.
// The promises under test: a link inside a tree is removed AS A LINK (its target survives), whatever is
// read-only/hidden/system is still deleted, a file that cannot be deleted is reported (never swallowed as
// success), and hostile names (trailing dot or space, unicode, deep nesting) do not make the walker touch
// anything outside the tree it was given.
[Trait("Category", "Adversarial")]
public sealed class SafeDeleteAdversarialTests : AdversarialTree
{
    private readonly RecordingAuditSink _audit = new();
    private FileOperationBroker Broker => new(_audit);

    // ---- links inside a tree ---------------------------------------------------------------

    [Fact]
    public void A_junction_inside_the_tree_is_removed_as_a_link_and_its_target_survives()
    {
        string target = Write(P("outside", "keep.txt"), "must survive");
        Write(P("tree", "own.txt"), "own");
        Link(P("outside"), P("tree", "pointer"));

        SafeDelete.DeleteDirectoryRecursive(P("tree"));

        Assert.False(Directory.Exists(P("tree")));
        Assert.Equal("must survive", File.ReadAllText(target));
    }

    [Fact]
    public void A_chain_of_junctions_inside_the_tree_is_not_followed_either()
    {
        string target = Write(P("outside", "keep.txt"), "must survive");
        string hop = Link(P("outside"), P("hop"));
        Write(P("tree", "own.txt"), "own");
        Link(hop, P("tree", "pointer"));

        var r = Broker.DeleteTree(P("tree"), "test junction chain");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(P("tree")));
        Assert.Equal("must survive", File.ReadAllText(target));
        Assert.True(Directory.Exists(hop), "the first hop is outside the tree and is not ours to remove");
    }

    [Fact]
    public void A_dangling_junction_inside_the_tree_does_not_stop_the_delete()
    {
        Directory.CreateDirectory(P("gone-soon"));
        Link(P("gone-soon"), P("tree", "stale"));
        Directory.Delete(P("gone-soon"));
        Write(P("tree", "own.txt"));

        Assert.True(SafeDelete.TryDeleteTree(P("tree")));
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void A_junction_root_is_removed_as_a_link_and_never_emptied_through_the_broker()
    {
        // The broker judges a junction by its target for the protected check, but the walker itself never
        // descends into a root that is a reparse point: only the link goes.
        string target = Write(P("outside", "keep.txt"), "must survive");
        string link = Link(P("outside"), P("root-link"));

        var r = Broker.DeleteTree(link, "test reparse root");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(link));
        Assert.Equal("must survive", File.ReadAllText(target));
    }

    [Fact]
    public void A_junction_root_to_a_protected_folder_is_refused_before_anything_is_touched()
    {
        string protectedDir = Dir("protected");
        Write(P("protected", "precious.txt"), "precious");
        string link = Link(protectedDir, P("to-protected"));

        using (SafeMove.UseProtectedFoldersForTest(protectedDir))
        {
            var r = Broker.DeleteTree(link, "test protected root");

            Assert.Equal(OperationStatus.Rejected, r.Status);
        }
        Assert.True(Directory.Exists(link));
        Assert.Equal("precious", File.ReadAllText(P("protected", "precious.txt")));
    }

    // ---- attributes ------------------------------------------------------------------------

    [Fact]
    public void Read_only_hidden_and_system_files_are_deleted()
    {
        string ro = Write(P("tree", "ro.txt"));
        string hidden = Write(P("tree", "sub", "hidden.txt"));
        string system = Write(P("tree", "sub", "system.sys"));
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReadOnly);

        Assert.True(SafeDelete.TryDeleteTree(P("tree")));
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void A_read_only_subfolder_is_either_deleted_or_reported_never_reported_as_success_while_present()
    {
        Write(P("tree", "ro-dir", "inner.txt"));
        File.SetAttributes(P("tree", "ro-dir"), FileAttributes.Directory | FileAttributes.ReadOnly);

        var r = Broker.DeleteTree(P("tree"), "test read-only folder");

        if (r.Status == OperationStatus.Succeeded) Assert.False(Directory.Exists(P("tree")));
        else
        {
            Assert.Equal(OperationStatus.PartiallyCompleted, r.Status);
            Assert.NotEmpty(r.Reason);
            Assert.True(Directory.Exists(P("tree")));
        }
    }

    // ---- depth and names -------------------------------------------------------------------

    [Fact]
    public void A_deep_tree_beyond_260_characters_is_deleted_completely()
    {
        string deep = P("tree");
        for (int i = 0; i < 150; i++) deep = Path.Combine(deep, "d");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "bottom.txt"), "x");
        Assert.True(deep.Length > 260, "test setup: the path must be longer than MAX_PATH");

        var r = Broker.DeleteTree(P("tree"), "test deep tree");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void Unicode_and_symbol_names_are_deleted()
    {
        string[] names = ["caf\u00e9 \u00e7\u00e3o.txt", "\u65e5\u672c\u8a9e.txt", "emoji \U0001F4C1.txt", "[brackets] 'quote' 100% & more.txt"];
        foreach (var n in names) Write(P("tree", n), n);
        Write(P("tree", "sub \U0001F600", "x.txt"));

        var r = Broker.DeleteTree(P("tree"), "test unicode");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void Entries_with_a_trailing_dot_or_space_never_make_the_walker_leave_its_tree()
    {
        // NTFS can hold "x." and "y " only when created in the \\?\ spelling; plain Win32 turns those names
        // into "x" and "y". The walker must stay inside the tree it was given and must not claim success
        // while something is left.
        Write(P("outside", "x", "keep.txt"), "outside lookalike");
        string tree = P("tree");
        Directory.CreateDirectory(@"\\?\" + tree + @"\x.");
        Directory.CreateDirectory(@"\\?\" + tree + @"\y ");
        File.WriteAllText(@"\\?\" + tree + @"\x.\inner.txt", "odd");
        File.WriteAllText(@"\\?\" + tree + @"\y \inner.txt", "odd");
        Write(P("tree", "x", "plain.txt"), "plain sibling");
        CleanLater(tree);

        bool done = SafeDelete.TryDeleteTree(tree);

        Assert.Equal(done, !Directory.Exists(tree));
        Assert.Equal("outside lookalike", File.ReadAllText(P("outside", "x", "keep.txt")));
    }

    // ---- a file that cannot be deleted -----------------------------------------------------

    [Fact]
    public void A_locked_file_is_reported_as_a_partial_delete_and_everything_else_goes()
    {
        string locked = Write(P("tree", "sub", "locked.txt"), "in use");
        Write(P("tree", "sub", "free.txt"));
        Write(P("tree", "free-top.txt"));

        FileOperationResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = Broker.DeleteTree(P("tree"), "test locked file");
            Assert.Equal(OperationStatus.PartiallyCompleted, r.Status);
            Assert.NotEmpty(r.Reason);
        }

        Assert.True(File.Exists(locked), "the locked file must still be there");
        Assert.False(File.Exists(P("tree", "sub", "free.txt")));
        Assert.False(File.Exists(P("tree", "free-top.txt")));
        Assert.Equal("in use", File.ReadAllText(locked));

        // Once the lock is gone, the same call finishes the job.
        Assert.True(Broker.DeleteTree(P("tree"), "test retry after unlock").Succeeded);
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void TryDeleteTree_says_false_while_a_file_is_locked_and_never_throws()
    {
        string locked = Write(P("tree", "locked.txt"));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        bool done = true;
        var ex = Record.Exception(() => done = SafeDelete.TryDeleteTree(P("tree")));

        Assert.Null(ex);
        Assert.False(done);
    }

    [Fact]
    public void A_missing_or_empty_path_is_a_quiet_no_op()
    {
        Assert.Null(Record.Exception(() => SafeDelete.DeleteDirectoryRecursive(P("never-existed"))));
        Assert.True(SafeDelete.TryDeleteTree(P("never-existed")));
        Assert.False(SafeDelete.ToRecycleBin(P("never-existed")));
        Assert.False(SafeDelete.ToRecycleBin(""));
    }

    // ---- Recycle Bin -----------------------------------------------------------------------

    [Fact]
    public void Recycling_a_folder_that_holds_a_junction_never_empties_the_junction_target()
    {
        string target = Write(P("outside", "keep.txt"), "must survive");
        Write(P("tree", "own.txt"));
        Link(P("outside"), P("tree", "pointer"));

        bool sent = SafeDelete.ToRecycleBin(P("tree"));

        // On a machine with a usable bin the tree is gone, otherwise it is still here intact; either way the
        // link target is never touched.
        if (sent) Assert.False(Directory.Exists(P("tree")));
        Assert.Equal("must survive", File.ReadAllText(target));
    }

    [Fact]
    public void Recycling_a_junction_takes_the_link_and_leaves_its_target()
    {
        string target = Write(P("outside", "keep.txt"), "must survive");
        string link = Link(P("outside"), P("just-a-link"));

        SafeDelete.ToRecycleBin(link);

        Assert.Equal("must survive", File.ReadAllText(target));
    }

    [Fact]
    public void Symbols_in_a_name_do_not_confuse_the_Recycle_Bin_call()
    {
        string a = Write(P("[a] 'b' 100% & c \U0001F4C1.txt"), "bye");
        string sibling = Write(P("a.txt"), "sibling");   // what "[a]" would match if it were a wildcard pattern

        var r = Broker.DeleteToRecycleBin(a, "test odd characters");

        Assert.Equal("sibling", File.ReadAllText(sibling));
        if (r.Status == OperationStatus.Succeeded) Assert.False(File.Exists(a));
        else
        {
            Assert.Equal(OperationStatus.Failed, r.Status);
            Assert.True(File.Exists(a), "a failed recycle must not lose the file");
        }
    }

    [Theory]
    [InlineData(@"\\server\share\a.txt")]
    [InlineData(@"\\?\UNC\server\share\a.txt")]
    public void A_network_path_is_never_handed_to_the_Recycle_Bin(string path)
    {
        Assert.False(SafeDelete.CanRecycle(path));
        Assert.False(SafeDelete.IsRecyclableLocation(path, DriveType.Fixed));
    }
}
