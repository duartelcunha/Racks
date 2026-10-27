using System.IO;
using Racks.Util;

namespace Racks.Tests.Util;

// RackMirror.RemoveJunctions removes only the junctions Racks made. Real files and folders a user put there
// by hand survive, and a junction's target is never touched.
public sealed class RackMirrorJunctionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-mirror-" + Guid.NewGuid().ToString("N"));
    private readonly string _mirror;

    public RackMirrorJunctionTests()
    {
        _mirror = Path.Combine(_root, "Racks");
        Directory.CreateDirectory(_mirror);
    }

    public void Dispose()
    {
        try
        {
            foreach (var e in Directory.EnumerateFileSystemEntries(_mirror))
                if (JunctionHelper.IsReparsePoint(e)) { try { Directory.Delete(e); } catch { } }
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Junctions_are_removed_and_their_targets_and_hand_made_items_survive()
    {
        string target = Path.Combine(_root, "sandbox");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep me");
        Assert.True(JunctionHelper.TryCreate(target, Path.Combine(_mirror, "Work")), "test setup: junction");
        Assert.True(JunctionHelper.TryCreate(target, Path.Combine(_mirror, "Home")), "test setup: junction");
        Directory.CreateDirectory(Path.Combine(_mirror, "My own folder"));
        File.WriteAllText(Path.Combine(_mirror, "note.txt"), "by hand");

        int removed = RackMirror.RemoveJunctions(_mirror);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(Path.Combine(_mirror, "Work")));
        Assert.False(Directory.Exists(Path.Combine(_mirror, "Home")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.True(Directory.Exists(Path.Combine(_mirror, "My own folder")));
        Assert.Equal("by hand", File.ReadAllText(Path.Combine(_mirror, "note.txt")));
    }

    [Fact]
    public void A_junction_whose_target_is_gone_is_still_removed()
    {
        string target = Path.Combine(_root, "gone-soon");
        Directory.CreateDirectory(target);
        Assert.True(JunctionHelper.TryCreate(target, Path.Combine(_mirror, "Stale")), "test setup: junction");
        Directory.Delete(target);

        Assert.Equal(1, RackMirror.RemoveJunctions(_mirror));
        Assert.False(JunctionHelper.IsReparsePoint(Path.Combine(_mirror, "Stale")));
    }

    [Fact]
    public void A_missing_mirror_folder_removes_nothing()
        => Assert.Equal(0, RackMirror.RemoveJunctions(Path.Combine(_root, "does-not-exist")));
}
