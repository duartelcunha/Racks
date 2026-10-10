using System.IO;
using Racks.Util;

namespace Racks.Tests.Util;

// A move between drives is copy + delete. If the delete fails, the file used to be reported as moved
// while the original stayed behind, so it existed twice and the rack claimed one of them.
public sealed class SafeMoveCopyFallbackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-copymove-" + Guid.NewGuid().ToString("N"));

    public SafeMoveCopyFallbackTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_file_that_can_be_deleted_is_moved_and_leaves_no_original()
    {
        string src = Path.Combine(_root, "a.txt"), dest = Path.Combine(_root, "b.txt");
        File.WriteAllText(src, "data");

        var result = SafeMove.CopyThenDelete(src, dest, srcIsDir: false, out string reason);

        Assert.Equal(SafeMove.Result.Moved, result);
        Assert.Equal("", reason);
        Assert.False(File.Exists(src));
        Assert.Equal("data", File.ReadAllText(dest));
    }

    [Fact]
    public void A_file_whose_original_cannot_be_deleted_is_rejected_and_the_copy_is_removed()
    {
        string src = Path.Combine(_root, "locked.txt"), dest = Path.Combine(_root, "copy.txt");
        File.WriteAllText(src, "data");

        using var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read); // blocks File.Delete
        var result = SafeMove.CopyThenDelete(src, dest, srcIsDir: false, out string reason);

        Assert.Equal(SafeMove.Result.Rejected, result);
        Assert.Contains("Nothing was changed", reason);
        Assert.True(File.Exists(src), "the original must be untouched");
        Assert.False(File.Exists(dest), "no second copy may be left behind");
    }

    [Fact]
    public void A_folder_is_moved_and_its_content_is_complete_at_the_destination()
    {
        string src = Path.Combine(_root, "dir"), dest = Path.Combine(_root, "dir2");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "x.txt"), "x");

        var result = SafeMove.CopyThenDelete(src, dest, srcIsDir: true, out _);

        Assert.Equal(SafeMove.Result.Moved, result);
        Assert.False(Directory.Exists(src));
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "sub", "x.txt")));
    }
}
