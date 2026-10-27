using System.IO;
using Racks.Infrastructure.Logging;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Util;

// P2: every move, unlink and folder removal the uninstall / remove-rack path does on user data goes through the
// broker and leaves an audit record. Other tests may use the default broker at the same time, so each test looks
// only at records whose paths are under its own temp folder.
[Collection("AuditLog")]   // AuditLog.Use swaps a process-wide sink: classes that use it must not run in parallel
public sealed class UninstallCleanupAuditTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-cleanup-audit-" + Guid.NewGuid().ToString("N"));
    private readonly CleanupPaths _paths;
    private readonly RecordingAuditSink _audit = new();
    private readonly IDisposable _scope;

    public UninstallCleanupAuditTests()
    {
        _paths = new CleanupPaths(
            Workspace: Path.Combine(_root, "RacksWorkspace"),
            VirtualFramesRoot: Path.Combine(_root, "AppData", "Racks", "VirtualFrames"),
            Desktop: Path.Combine(_root, "Desktop"),
            MirrorRoot: Path.Combine(_root, "Racks"),
            LibraryFile: Path.Combine(_root, "Libraries", "DesktopWorkspace.library-ms"));
        Directory.CreateDirectory(_paths.Desktop);
        _scope = AuditLog.Use(_audit);
    }

    public void Dispose()
    {
        _scope.Dispose();
        try { SafeDelete.DeleteDirectoryRecursive(_root); } catch { }
    }

    private List<AuditEvent> Mine() =>
        _audit.Events.Where(e => e.Source.StartsWith(_root, StringComparison.OrdinalIgnoreCase)).ToList();

    private string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Uninstall_audits_the_move_the_unlink_the_hard_link_and_the_folder_removal()
    {
        string sandbox = Path.Combine(_paths.VirtualFramesRoot, "0123abcd");
        Write(Path.Combine(sandbox, "doc.txt"), "doc");
        string target = Path.Combine(_root, "RealFolder");
        Write(Path.Combine(target, "keep.txt"), "keep");
        Assert.True(JunctionHelper.TryCreate(target, Path.Combine(sandbox, "RealFolder")), "test setup: junction");
        string original = Write(Path.Combine(_paths.Desktop, "song.mp3"), "audio");
        Assert.True(HardlinkHelper.TryCreate(original, Path.Combine(sandbox, "song.mp3")), "test setup: hard link");

        var report = UninstallCleanup.Run(_paths);

        Assert.Equal(1, report.Returned);
        Assert.Equal(2, report.Unlinked);
        var records = Mine();
        Assert.Contains(records, e => e.Kind == "Move" && e.Status == "Succeeded" && e.Reason.StartsWith("uninstall:"));
        Assert.Equal(2, records.Count(e => e.Kind == "Unlink" && e.Status == "Succeeded" && e.Reason.StartsWith("uninstall:")));
        Assert.Contains(records, e => e.Kind == "DeleteEmptyDirectory" && e.Status == "Succeeded" && e.Source == sandbox);
        Assert.All(records, e => Assert.False(string.IsNullOrWhiteSpace(e.OperationId)));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.Equal("audio", File.ReadAllText(original));
    }

    [Fact]
    public void Removing_a_rack_audits_with_its_own_reason_and_keeps_a_folder_that_could_not_be_emptied()
    {
        string folder = Path.Combine(_root, "rack");
        Write(Path.Combine(folder, "a.txt"), "a");
        var locked = new FileStream(Write(Path.Combine(folder, "locked.txt"), "locked"), FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var report = UninstallCleanup.ReturnFolderToDesktop(folder, _paths.Desktop);

            Assert.Equal(1, report.Returned);
            Assert.Equal(1, report.Kept);
            Assert.Contains(folder, report.KeptFolders);
            Assert.True(File.Exists(Path.Combine(folder, "locked.txt")));
            var records = Mine();
            Assert.All(records.Where(e => e.Kind is "Move" or "Unlink"), e => Assert.StartsWith("remove rack:", e.Reason));
            Assert.DoesNotContain(records, e => e.Kind == "DeleteEmptyDirectory" && e.Status == "Succeeded");
        }
        finally { locked.Dispose(); }
    }

    [Fact]
    public void A_name_that_is_taken_on_the_desktop_gets_the_next_free_name_and_is_never_overwritten()
    {
        Write(Path.Combine(_paths.Desktop, "x.txt"), "desktop");
        string folder = Path.Combine(_root, "rack");
        Write(Path.Combine(folder, "x.txt"), "rack");

        var report = UninstallCleanup.ReturnFolderToDesktop(folder, _paths.Desktop);

        Assert.Equal(1, report.Returned);
        Assert.Equal("desktop", File.ReadAllText(Path.Combine(_paths.Desktop, "x.txt")));
        Assert.Equal("rack", File.ReadAllText(Path.Combine(_paths.Desktop, "x (from Racks).txt")));
        Assert.False(Directory.Exists(folder));
    }
}
