using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.FileSystem;

// When Desktop, Documents or AppData were relocated with a junction (mklink /J to another drive), the protected
// folder's own path is a junction and its resolved path is the junction's target. An item-itself check (Unlink,
// Rename) has to recognise the junction path too, or it would remove the user's real Desktop junction.
// A temp junction stands in for the relocated folder, declared protected through the per-thread test seam, so the
// developer's real folders are never involved.
public sealed class ProtectedLinkRootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-linkroot-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingAuditSink _audit = new();
    private readonly FileOperationBroker _broker;
    private readonly List<string> _links = [];

    public ProtectedLinkRootTests()
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

    // "target" holds the data; "Desktop" is a junction to it: the relocated protected folder.
    private (string Target, string Junction) Relocated()
    {
        string target = P("target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep me");
        string junction = P("Desktop");
        Assert.True(JunctionHelper.TryCreate(target, junction), "test setup: junction");
        _links.Add(junction);
        return (target, junction);
    }

    [Fact]
    public void A_relocated_protected_folder_is_protected_under_its_junction_path_too()
    {
        var (target, junction) = Relocated();
        using (SafeMove.UseProtectedFoldersForTest(junction))
        {
            Assert.True(SafeMove.IsProtectedLink(junction, out string kind), "the junction path itself");
            Assert.Contains("protected", kind);
            Assert.True(SafeMove.IsProtectedOrAncestorLink(junction, out _));
            Assert.True(SafeMove.IsProtectedPath(junction, out _), "resolved: the target");
            Assert.True(SafeMove.IsProtectedPath(target, out _), "the target is the same folder");
            Assert.False(SafeMove.IsProtectedLink(Path.Combine(junction, "keep.txt"), out _), "an item inside is fine");
        }
        Assert.False(SafeMove.IsProtectedLink(junction, out _), "the seam is gone after the scope");
    }

    [Fact]
    public void Unlink_and_Rename_refuse_the_relocated_protected_junction_and_leave_everything_in_place()
    {
        var (target, junction) = Relocated();
        FileOperationResult unlink, rename;
        using (SafeMove.UseProtectedFoldersForTest(junction))
        {
            unlink = _broker.Unlink(junction, "test relocated desktop");
            rename = _broker.Rename(junction, "Renamed", "test relocated desktop rename");
        }

        Assert.Equal(OperationStatus.Rejected, unlink.Status);
        Assert.Contains("protected", unlink.Reason);
        Assert.Equal(OperationStatus.Rejected, rename.Status);
        Assert.True(JunctionHelper.IsLink(junction));
        Assert.False(Directory.Exists(P("Renamed")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void Move_DeleteTree_and_DeleteEmptyDirectory_also_refuse_the_relocated_protected_folder()
    {
        var (target, junction) = Relocated();
        using (SafeMove.UseProtectedFoldersForTest(junction))
        {
            Assert.Equal(OperationStatus.Rejected, _broker.Move(junction, P("moved"), "test move").Status);
            Assert.Equal(OperationStatus.Rejected, _broker.DeleteTree(junction, "test delete").Status);
            Assert.Equal(OperationStatus.Rejected, _broker.DeleteTree(target, "test delete target").Status);
        }
        Assert.True(JunctionHelper.IsLink(junction));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void An_ordinary_link_to_the_protected_folder_is_still_just_a_link()
    {
        var (target, junction) = Relocated();
        string other = P("other-link");
        Assert.True(JunctionHelper.TryCreate(target, other), "test setup: junction");
        _links.Add(other);

        FileOperationResult r;
        using (SafeMove.UseProtectedFoldersForTest(junction)) r = _broker.Unlink(other, "test ordinary link");

        Assert.True(r.Succeeded, r.Reason);
        Assert.False(Directory.Exists(other));
        Assert.True(JunctionHelper.IsLink(junction));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [ShortNameFact]
    public void The_8_3_spelling_of_the_relocated_junction_is_protected_too()
    {
        var (target, junction) = Relocated();
        string shortForm = ShortNameFactAttribute.ShortPath(junction);
        Assert.False(string.Equals(junction, shortForm, StringComparison.OrdinalIgnoreCase));   // the fact attribute probed that 8.3 names exist; this entry should have one

        FileOperationResult r;
        using (SafeMove.UseProtectedFoldersForTest(junction))
        {
            Assert.True(SafeMove.IsProtectedLink(shortForm, out _));
            r = _broker.Unlink(shortForm, "test 8.3 spelling");
        }

        Assert.Equal(OperationStatus.Rejected, r.Status);
        Assert.True(JunctionHelper.IsLink(junction));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void The_extra_roots_seam_is_per_thread()
    {
        var (_, junction) = Relocated();
        using (SafeMove.UseProtectedFoldersForTest(junction))
        {
            bool seenElsewhere = true;
            var t = new Thread(() => seenElsewhere = SafeMove.IsProtectedLink(junction, out _));
            t.Start();
            t.Join();
            Assert.False(seenElsewhere);
        }
    }
}
