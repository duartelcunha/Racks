using System.IO;
using Racks.Application.AutoRoute;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.AutoRoute;

// Which Desktop entries auto-route may move at all (INV-ROUTE-1). Real temp files for what the disk can do;
// the three disk probes are replaced for what it can't (a cloud placeholder cannot be created in a test).
public sealed class RoutingPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-policy-" + Guid.NewGuid().ToString("N"));

    public RoutingPolicyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private string Touch(string name, string content = "data")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("desktop.ini")]
    [InlineData("Desktop.INI")]
    [InlineData("movie.mp4.crdownload")]
    [InlineData("setup.exe.part")]
    [InlineData("file.PARTIAL")]
    [InlineData("scratch.tmp")]
    [InlineData("a.download")]
    [InlineData("b.opdownload")]
    [InlineData("~$report.docx")]
    public void Names_of_unfinished_or_system_files_are_skipped(string name)
    {
        string path = Touch(name);
        var verdict = new RoutingPolicy().Evaluate(path);
        Assert.Equal(EligibilityKind.Skip, verdict.Kind);
        Assert.NotEmpty(verdict.Reason);
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("my~$notes.txt")]          // ~$ only counts at the start
    [InlineData("partial-results.xlsx")]    // the word, not the suffix
    [InlineData("holiday photo (1).JPG")]
    public void Ordinary_names_pass_the_name_check(string name) => Assert.Null(RoutingPolicy.CheckName(name));

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("name ")]
    [InlineData("a:b")]
    public void A_name_that_is_not_a_plain_leaf_name_is_skipped(string name)
    {
        // These names can't exist as real files, so the attribute probe is replaced.
        var policy = new RoutingPolicy { GetAttributes = _ => FileAttributes.Normal, IsInUse = _ => false };
        var verdict = policy.Evaluate(@"C:\desk\" + name);
        Assert.Equal(EligibilityKind.Skip, verdict.Kind);
    }

    [Fact]
    public void A_hidden_file_is_skipped()
    {
        string path = Touch("secret.pdf");
        File.SetAttributes(path, FileAttributes.Hidden);
        Assert.Equal(EligibilityKind.Skip, new RoutingPolicy().Evaluate(path).Kind);
    }

    [Fact]
    public void A_system_file_is_skipped()
    {
        string path = Touch("sys.pdf");
        File.SetAttributes(path, FileAttributes.System);
        Assert.Equal(EligibilityKind.Skip, new RoutingPolicy().Evaluate(path).Kind);
    }

    [Fact]
    public void A_missing_entry_is_skipped()
    {
        var verdict = new RoutingPolicy().Evaluate(Path.Combine(_root, "gone.pdf"));
        Assert.Equal(EligibilityKind.Skip, verdict.Kind);
        Assert.Contains("no longer exists", verdict.Reason);
    }

    [Fact]
    public void A_plain_file_is_eligible()
    {
        string path = Touch("report.pdf");
        Assert.Equal(EligibilityKind.Eligible, new RoutingPolicy().Evaluate(path).Kind);
    }

    [Fact]
    public void A_plain_folder_is_eligible()
    {
        string dir = Path.Combine(_root, "Project X");
        Directory.CreateDirectory(dir);
        Assert.Equal(EligibilityKind.Eligible, new RoutingPolicy().Evaluate(dir).Kind);
    }

    [Fact]
    public void A_junction_is_skipped()
    {
        string target = Path.Combine(_root, "real");
        Directory.CreateDirectory(target);
        string junction = Path.Combine(_root, "link-to-real");
        Assert.True(JunctionHelper.TryCreate(target, junction), "test setup: junction");

        var verdict = new RoutingPolicy().Evaluate(junction);
        Assert.Equal(EligibilityKind.Skip, verdict.Kind);
        Assert.Contains("junction or symbolic link", verdict.Reason);
    }

    [SymlinkFact]
    public void A_file_symlink_is_skipped()
    {
        string target = Touch("target.pdf");
        string link = Path.Combine(_root, "link.pdf");
        File.CreateSymbolicLink(link, target);
        Assert.Equal(EligibilityKind.Skip, new RoutingPolicy().Evaluate(link).Kind);
    }

    [SymlinkFact]
    public void A_dangling_symlink_is_skipped()
    {
        string link = Path.Combine(_root, "dangling.pdf");
        File.CreateSymbolicLink(link, Path.Combine(_root, "nothing-here.pdf"));
        Assert.Equal(EligibilityKind.Skip, new RoutingPolicy().Evaluate(link).Kind);
    }

    // A OneDrive / cloud placeholder on a redirected Desktop is a reparse point that is NOT a link. It carries
    // a real user file and must still be routed, and must not be opened (opening could download it).
    [Fact]
    public void A_cloud_placeholder_reparse_point_is_not_skipped_and_is_never_opened()
    {
        var policy = new RoutingPolicy
        {
            GetAttributes = _ => FileAttributes.Archive | FileAttributes.ReparsePoint,
            IsLink = _ => false,
            IsInUse = _ => throw new InvalidOperationException("a placeholder must not be opened"),
        };
        Assert.Equal(EligibilityKind.Eligible, policy.Evaluate(@"C:\Users\x\OneDrive\Desktop\cloud.docx").Kind);
    }

    [Fact]
    public void A_reparse_point_that_is_a_link_is_skipped()
    {
        var policy = new RoutingPolicy
        {
            GetAttributes = _ => FileAttributes.Archive | FileAttributes.ReparsePoint,
            IsLink = _ => true,
        };
        Assert.Equal(EligibilityKind.Skip, policy.Evaluate(@"C:\desk\l.docx").Kind);
    }

    [Theory]
    [InlineData(0x00040000)]   // RECALL_ON_DATA_ACCESS
    [InlineData(0x00400000)]   // RECALL_ON_OPEN
    [InlineData(0x1000)]       // OFFLINE
    public void A_file_flagged_for_recall_is_eligible_without_being_opened(int flag)
    {
        var policy = new RoutingPolicy
        {
            GetAttributes = _ => (FileAttributes)flag,
            IsInUse = _ => throw new InvalidOperationException("must not be opened"),
        };
        Assert.Equal(EligibilityKind.Eligible, policy.Evaluate(@"C:\desk\recall.docx").Kind);
    }

    [Fact]
    public void A_file_that_is_still_open_is_in_use_and_becomes_eligible_when_released()
    {
        string path = Touch("download.bin");
        var policy = new RoutingPolicy();
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var verdict = policy.Evaluate(path);
            Assert.Equal(EligibilityKind.InUse, verdict.Kind);
        }
        Assert.Equal(EligibilityKind.Eligible, policy.Evaluate(path).Kind);
    }

    [Fact]
    public void A_file_with_a_writer_that_shares_reading_is_still_in_use()
    {
        string path = Touch("copying.bin");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        Assert.Equal(EligibilityKind.InUse, new RoutingPolicy().Evaluate(path).Kind);
    }

    [Fact]
    public void The_policy_does_not_change_the_file()
    {
        string path = Touch("report.pdf", "keep me");
        new RoutingPolicy().Evaluate(path);
        Assert.Equal("keep me", File.ReadAllText(path));
    }
}
