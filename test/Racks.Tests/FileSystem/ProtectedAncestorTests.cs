using System.IO;
using Racks.Infrastructure.FileSystem;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.FileSystem;

// INV-FS-1: moving or deleting a folder that CONTAINS a protected folder is refused, not only the protected folder
// itself. The prefix rule is tested on synthetic roots (pure); the real special folders are only ever checked or
// reached through a junction, so a regression can at worst remove a link.
public sealed class ProtectedAncestorTests : IDisposable
{
    private static readonly IReadOnlyList<(string Path, string Kind)> Roots =
    [
        (@"C:\Users\a\Desktop", "protected system folder (Desktop)"),
        (@"C:\Users\a\Documents", "protected system folder (MyDocuments)"),
        (@"D:\OneDrive\Pictures", "protected system folder (MyPictures)"),
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-ancestor-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingAuditSink _audit = new();
    private readonly FileOperationBroker _broker;
    private readonly List<string> _links = [];

    public ProtectedAncestorTests()
    {
        Directory.CreateDirectory(_root);
        _broker = new FileOperationBroker(_audit);
    }

    public void Dispose()
    {
        foreach (var link in _links) { try { Directory.Delete(link); } catch { } }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // ---- the prefix rule (pure) -------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Users\a", true)]
    [InlineData(@"C:\Users\a\", true)]               // trailing separator
    [InlineData(@"c:\users\A", true)]                // case-insensitive
    [InlineData(@"C:\Users", true)]
    [InlineData(@"D:\OneDrive", true)]
    [InlineData(@"C:\Users\a\Desktop", true)]        // equality is still refused
    [InlineData(@"C:\Users\ab", false)]              // shared name prefix is NOT an ancestor
    [InlineData(@"C:\Users\a-old", false)]
    [InlineData(@"C:\Use", false)]
    [InlineData(@"D:\OneDrive2", false)]
    [InlineData(@"C:\Users\a\Desktop\Folder", false)]    // beneath a protected folder: allowed
    [InlineData(@"C:\Users\a\Documents\Projects", false)]
    [InlineData(@"C:\Users\a\Downloads", false)]
    [InlineData(@"E:\Anything", false)]
    public void Ancestor_rule_uses_a_trailing_separator_prefix_test(string path, bool refused)
        => Assert.Equal(refused, SafeMove.MatchProtected(path, Roots, includeAncestors: true, out _));

    [Fact]
    public void Without_the_ancestor_rule_only_equality_matches()
    {
        Assert.False(SafeMove.MatchProtected(@"C:\Users\a", Roots, includeAncestors: false, out _));
        Assert.True(SafeMove.MatchProtected(@"C:\Users\a\Desktop", Roots, includeAncestors: false, out _));
    }

    [Fact]
    public void An_ancestor_reason_names_the_protected_folder_it_contains()
    {
        Assert.True(SafeMove.MatchProtected(@"C:\Users\a", Roots, includeAncestors: true, out string kind));
        Assert.Contains("contains", kind);
        Assert.Contains("Desktop", kind);
        Assert.True(SafeMove.MatchProtected(@"C:\", Roots, includeAncestors: true, out string drive));
        Assert.Equal("drive root", drive);
    }

    // ---- real folders -----------------------------------------------------------------------

    [Fact]
    public void AppData_and_the_Users_folder_are_refused_by_the_move_preflight()
    {
        string appData = Path.Combine(Profile, "AppData");
        string users = Path.GetDirectoryName(Profile)!;

        foreach (var src in new[] { appData, users })
        {
            if (!Directory.Exists(src)) continue;
            var verdict = SafeMove.Check(src, Path.Combine(_root, "moved"), out string reason);
            Assert.Equal(SafeMove.Result.Rejected, verdict);
            Assert.Contains("contains", reason);
            Assert.Contains("refusing to move", reason);
        }
        Assert.True(SafeMove.IsProtectedOrAncestorPath(appData, out _));
        Assert.True(SafeMove.IsProtectedOrAncestorPath(users, out _));
    }

    [Fact]
    public void The_Roaming_folder_which_contains_nothing_protected_is_still_protected_by_equality_only()
    {
        // ApplicationData IS on the list (equality); its parent AppData is an ancestor; a folder inside it is neither.
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.True(SafeMove.IsProtectedPath(roaming, out _));
        Assert.False(SafeMove.IsProtectedOrAncestorPath(Path.Combine(roaming, "Racks", "VirtualFrames", "abc123"), out _));
        Assert.False(SafeMove.IsProtectedOrAncestorPath(Path.Combine(roaming, "SomeOtherApp"), out _));
    }

    [Fact]
    public void Normal_flows_stay_allowed()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] allowed =
        [
            Path.Combine(desktop, "report.docx"),                       // a Desktop file
            Path.Combine(desktop, "My folder"),                         // a folder on the Desktop
            Path.Combine(docs, "Projects"),                             // a folder rack path
            Path.Combine(Profile, "RacksWorkspace", "photo.jpg"),       // a workspace item
            Path.Combine(Profile, "RacksWorkspace"),                    // the workspace folder itself
            Path.Combine(Profile, "Racks", "Work"),                     // a mirror junction
            Path.Combine(Path.GetTempPath(), "anything", "x.txt"),
            Profile + "-sibling",                                       // shares the profile's name as a prefix
        ];
        foreach (var path in allowed)
            Assert.False(SafeMove.IsProtectedOrAncestorPath(path, out string kind), $"{path} was refused: {kind}");
    }

    [Fact]
    public void A_file_and_a_folder_in_a_temp_tree_move_normally_through_the_broker()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "x");
        Directory.CreateDirectory(Path.Combine(_root, "dir"));
        File.WriteAllText(Path.Combine(_root, "dir", "inner.txt"), "y");

        Assert.True(_broker.Move(file, Path.Combine(_root, "b.txt"), "test file").Succeeded);
        Assert.True(_broker.Move(Path.Combine(_root, "dir"), Path.Combine(_root, "dir2"), "test folder").Succeeded);
        Assert.True(File.Exists(Path.Combine(_root, "dir2", "inner.txt")));
    }

    // ---- through the broker (junctions only) ------------------------------------------------

    private string LinkTo(string target, string name)
    {
        Assert.True(Directory.Exists(target), "test setup: " + target + " is missing");
        string link = Path.Combine(_root, name);
        Assert.True(JunctionHelper.TryCreate(target, link), "test setup: junction");
        _links.Add(link);
        return link;
    }

    [Fact]
    public void Broker_Move_DeleteTree_and_Recycle_reject_a_junction_to_AppData()
    {
        string link = LinkTo(Path.Combine(Profile, "AppData"), "appdata-link");

        var move = _broker.Move(link, Path.Combine(_root, "moved"), "test ancestor move");
        var tree = _broker.DeleteTree(link, "test ancestor delete");
        var bin = _broker.DeleteToRecycleBin(link, "test ancestor recycle");

        foreach (var r in new[] { move, tree, bin })
        {
            Assert.Equal(OperationStatus.Rejected, r.Status);
            Assert.Contains("contains", r.Reason);
        }
        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(Path.Combine(Profile, "AppData")));
        Assert.Equal(3, _audit.Events.Count);
    }

    [Fact]
    public void Broker_rejects_a_junction_to_the_Users_folder()
    {
        string users = Path.GetDirectoryName(Profile)!;
        string link = LinkTo(users, "users-link");

        var tree = _broker.DeleteTree(link, "test users delete");
        var move = _broker.Move(link, Path.Combine(_root, "moved"), "test users move");

        Assert.Equal(OperationStatus.Rejected, tree.Status);
        Assert.Equal(OperationStatus.Rejected, move.Status);
        Assert.True(Directory.Exists(users));
    }

    [Fact]
    public void Renaming_the_link_itself_is_still_allowed_but_renaming_the_real_ancestor_is_not()
    {
        string appData = Path.Combine(Profile, "AppData");
        string link = LinkTo(appData, "appdata-link");

        // The link is only a name inside a rack: renaming it does not touch AppData.
        var viaLink = _broker.Rename(link, "renamed-link", "test link rename");
        Assert.True(viaLink.Succeeded, viaLink.Reason);
        _links.Add(Path.Combine(_root, "renamed-link"));

        // Renaming AppData itself would take the protected folders inside it with it. The rejection happens
        // before any rename; the assertion is on the verdict only.
        Assert.True(SafeMove.IsProtectedOrAncestorLink(appData, out _));
    }

    [Fact]
    public void A_sibling_with_a_shared_name_prefix_is_not_refused_by_the_broker()
    {
        // _root and _root + "x" share a prefix; neither is an ancestor of a protected folder.
        string sibling = _root + "x";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllText(Path.Combine(sibling, "f.txt"), "f");
            var r = _broker.DeleteTree(sibling, "test sibling prefix");
            Assert.True(r.Succeeded, r.Reason);
        }
        finally { try { Directory.Delete(sibling, true); } catch { } }
    }
}
