using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Adversarial;

// P11b: when the copy half of a cross-volume move fails, it removes ONLY what that call created. The destination
// can appear between the preflight and the copy (another process, a sync client); before this change a file copy
// failed on "exists" and its cleanup then deleted that other file, and a folder copy merged into the other folder
// and wiped all of it on error. The "other process" is the SafeMove.UseBeforeCopyHookForTest seam.
[Trait("Category", "Adversarial")]
public sealed class CopyFallbackCleanupTests : AdversarialTree
{
    [Fact]
    public void A_file_that_appears_at_the_destination_before_the_copy_is_left_alone_and_the_source_is_untouched()
    {
        string src = Write(P("a.txt"), "mine");
        string dest = P("b.txt");

        SafeMove.Result result;
        string reason;
        using (SafeMove.UseBeforeCopyHookForTest((_, d) => File.WriteAllText(d, "theirs")))
            result = SafeMove.CopyThenDelete(src, dest, srcIsDir: false, out reason);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Contains("failed", reason);
        Assert.Equal("theirs", File.ReadAllText(dest));
        Assert.Equal("mine", File.ReadAllText(src));
    }

    [Fact]
    public void A_folder_that_appears_at_the_destination_before_the_copy_is_not_merged_into_and_not_wiped()
    {
        Write(P("src", "a.txt"), "a");
        Write(P("src", "sub", "b.txt"), "b");
        string dest = P("dest");

        SafeMove.Result result;
        using (SafeMove.UseBeforeCopyHookForTest((_, d) =>
        {
            Directory.CreateDirectory(Path.Combine(d, "theirs-sub"));
            File.WriteAllText(Path.Combine(d, "theirs.txt"), "theirs");
            File.WriteAllText(Path.Combine(d, "theirs-sub", "deep.txt"), "theirs deep");
        }))
            result = SafeMove.CopyThenDelete(P("src"), dest, srcIsDir: true, out _);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(dest, "theirs.txt")));
        Assert.Equal("theirs deep", File.ReadAllText(Path.Combine(dest, "theirs-sub", "deep.txt")));
        Assert.Equal(2, Directory.GetFileSystemEntries(dest).Length);   // nothing of the source was merged in
        Assert.Equal("a", File.ReadAllText(P("src", "a.txt")));
        Assert.Equal("b", File.ReadAllText(P("src", "sub", "b.txt")));
    }

    [Fact]
    public void A_destination_that_already_existed_is_never_touched_by_a_failed_copy_either_kind()
    {
        Write(P("src", "a.txt"), "a");
        Write(P("file-src.txt"), "mine");
        Write(P("dest-dir", "theirs.txt"), "theirs");
        Write(P("dest-file.txt"), "theirs file");

        var folder = SafeMove.CopyThenDelete(P("src"), P("dest-dir"), srcIsDir: true, out _);
        var file = SafeMove.CopyThenDelete(P("file-src.txt"), P("dest-file.txt"), srcIsDir: false, out _);

        Assert.Equal(SafeMove.Result.Rejected, folder);
        Assert.Equal(SafeMove.Result.Rejected, file);
        Assert.Equal(new[] { "theirs.txt" }, Directory.GetFileSystemEntries(P("dest-dir")).Select(Path.GetFileName).ToArray());
        Assert.Equal("theirs", File.ReadAllText(P("dest-dir", "theirs.txt")));
        Assert.Equal("theirs file", File.ReadAllText(P("dest-file.txt")));
        Assert.Equal("a", File.ReadAllText(P("src", "a.txt")));
        Assert.Equal("mine", File.ReadAllText(P("file-src.txt")));
    }

    [Fact]
    public void A_copy_that_fails_half_way_removes_exactly_what_it_made_including_read_only_copies()
    {
        string readOnly = Write(P("src", "ro.txt"), "read only");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        Write(P("src", "free.txt"), "free");
        Directory.CreateDirectory(P("src", "empty-sub"));
        string locked = Write(P("src", "sub", "locked.txt"), "locked");
        Directory.CreateDirectory(P("dest-parent"));

        SafeMove.Result result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = SafeMove.CopyThenDelete(P("src"), P("dest-parent", "dest"), srcIsDir: true, out _);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Empty(Directory.GetFileSystemEntries(P("dest-parent")));
        Assert.Equal("read only", File.ReadAllText(readOnly));
        Assert.Equal("locked", File.ReadAllText(locked));
    }

    [Fact]
    public void RemoveCreated_takes_back_what_it_is_given_and_leaves_what_someone_else_added()
    {
        string ours = Dir("ours");
        string oursFile = Write(Path.Combine(ours, "ours.txt"), "ours");
        string foreignFile = Write(Path.Combine(ours, "foreign.txt"), "foreign");
        string ourSub = Dir("ours", "sub");
        string foreignSub = Dir("ours", "foreign-sub");
        Write(Path.Combine(foreignSub, "inside.txt"), "inside");

        SafeMove.RemoveCreated([(ours, true), (oursFile, false), (ourSub, true)]);

        Assert.False(File.Exists(oursFile));
        Assert.False(Directory.Exists(ourSub));
        Assert.Equal("foreign", File.ReadAllText(foreignFile));
        Assert.Equal("inside", File.ReadAllText(Path.Combine(foreignSub, "inside.txt")));
        Assert.True(Directory.Exists(ours), "a folder that gained somebody else's content stays with it");
    }

    [Fact]
    public void A_destination_whose_parent_is_missing_fails_cleanly_and_creates_nothing()
    {
        Write(P("src", "a.txt"), "a");
        Write(P("file.txt"), "f");

        var folder = SafeMove.CopyThenDelete(P("src"), P("no-parent", "dest"), srcIsDir: true, out string r1);
        var file = SafeMove.CopyThenDelete(P("file.txt"), P("no-parent", "file.txt"), srcIsDir: false, out string r2);

        Assert.Equal(SafeMove.Result.Rejected, folder);
        Assert.Equal(SafeMove.Result.Rejected, file);
        Assert.NotEmpty(r1);
        Assert.NotEmpty(r2);
        Assert.False(Directory.Exists(P("no-parent")));
        Assert.Equal("a", File.ReadAllText(P("src", "a.txt")));
        Assert.Equal("f", File.ReadAllText(P("file.txt")));
    }

    [Fact]
    public void A_successful_copy_keeps_empty_subfolders_attributes_and_symbol_names_and_a_long_destination_path()
    {
        string name = new string('n', 90);
        string readOnly = Write(P("src", "ro [1] & 'x'.txt"), "read only");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        Directory.CreateDirectory(P("src", "empty \U0001F4C1"));
        Write(P("src", "sub", "b.txt"), "b");
        string destParent = Dir("dest", name, name, name);
        Assert.True(destParent.Length > 260, "test setup");
        string dest = Path.Combine(destParent, "copy");

        var result = SafeMove.CopyThenDelete(P("src"), dest, srcIsDir: true, out string reason);

        Assert.Equal(SafeMove.Result.Moved, result);
        Assert.Equal("", reason);
        Assert.Equal("read only", File.ReadAllText(Path.Combine(dest, "ro [1] & 'x'.txt")));
        Assert.True(File.GetAttributes(Path.Combine(dest, "ro [1] & 'x'.txt")).HasFlag(FileAttributes.ReadOnly));
        Assert.True(Directory.Exists(Path.Combine(dest, "empty \U0001F4C1")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(dest, "sub", "b.txt")));
        Assert.False(Directory.Exists(P("src")));
    }
}

// P11b: the broker runs the (expensive, ~50 CreateFile calls) preflight once per move, whatever the outcome.
[Trait("Category", "Adversarial")]
public sealed class BrokerSinglePreflightTests : AdversarialTree
{
    private FileOperationBroker Broker => new(new RecordingAuditSink());

    private int ChecksDuring(Func<FileOperationResult> move, out FileOperationResult result)
    {
        int before = SafeMove.CheckCallsOnThisThread;
        result = move();
        return SafeMove.CheckCallsOnThisThread - before;
    }

    [Fact]
    public void A_file_move_a_folder_move_and_a_same_volume_move_each_run_the_preflight_once()
    {
        Write(P("a.txt"), "a");
        Write(P("dir", "x.txt"), "x");
        Write(P("c.txt"), "c");
        Dir("dest");

        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("a.txt"), P("dest", "a.txt"), "test"), out var file));
        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("dir"), P("dest", "dir"), "test"), out var folder));
        Assert.Equal(1, ChecksDuring(() => Broker.MoveSameVolume(P("c.txt"), P("dest", "c.txt"), "test"), out var same));

        Assert.True(file.Succeeded && folder.Succeeded && same.Succeeded);
    }

    [Fact]
    public void Every_refusal_also_runs_it_exactly_once_with_the_same_reasons_as_before()
    {
        Write(P("a.txt"), "a");
        Write(P("dest", "a.txt"), "theirs");
        string prot = Dir("protected-folder");
        using var scope = SafeMove.UseProtectedFoldersForTest(prot);

        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("a.txt"), P("dest", "a.txt"), "test"), out var collision));
        Assert.Equal(1, ChecksDuring(() => Broker.Move(prot, P("elsewhere"), "test"), out var protectedMove));
        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("missing.txt"), P("dest", "m.txt"), "test"), out var missing));
        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("a.txt"), P("no-parent", "a.txt"), "test"), out var noParent));
        Assert.Equal(1, ChecksDuring(() => Broker.Move(P("a.txt"), P("a.txt"), "test"), out var intoItself));

        Assert.Equal(OperationStatus.Skipped, collision.Status);
        Assert.Equal("\"a.txt\" already exists in the rack — skipping.", collision.Reason);
        Assert.Equal(OperationStatus.Rejected, protectedMove.Status);
        Assert.Contains("refusing to move", protectedMove.Reason);
        Assert.Equal(OperationStatus.Rejected, missing.Status);
        Assert.Contains("no longer exists", missing.Reason);
        Assert.Equal(OperationStatus.Rejected, noParent.Status);
        Assert.Equal("The destination folder doesn't exist.", noParent.Reason);
        Assert.Equal(OperationStatus.Rejected, intoItself.Status);
        Assert.Equal("Can't move \"a.txt\" into itself.", intoItself.Reason);
        Assert.Equal("a", File.ReadAllText(P("a.txt")));
        Assert.Equal("theirs", File.ReadAllText(P("dest", "a.txt")));
    }

    [Fact]
    public void A_name_that_appears_after_the_preflight_is_still_never_overwritten()
    {
        // The second check used to turn this race into a "skipped"; now the move itself refuses an existing name.
        Write(P("a.txt"), "mine");
        Write(P("dest", "other.txt"), "x");
        string src = P("a.txt"), dest = P("dest", "late.txt");
        Assert.Null(SafeMove.Check(src, dest, out _));
        File.WriteAllText(dest, "theirs");

        var result = SafeMove.MoveAfterCheck(src, dest, out string reason);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.NotEmpty(reason);
        Assert.Equal("theirs", File.ReadAllText(dest));
        Assert.Equal("mine", File.ReadAllText(src));
    }
}
