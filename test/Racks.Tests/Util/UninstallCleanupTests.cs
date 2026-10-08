using System.IO;
using Racks.Util;

namespace Racks.Tests.Util;

public sealed class UninstallCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-cleanup-" + Guid.NewGuid().ToString("N"));
    private readonly CleanupPaths _paths;

    public UninstallCleanupTests()
    {
        _paths = new CleanupPaths(
            Workspace: Path.Combine(_root, "RacksWorkspace"),
            VirtualFramesRoot: Path.Combine(_root, "AppData", "Racks", "VirtualFrames"),
            Desktop: Path.Combine(_root, "Desktop"),
            MirrorRoot: Path.Combine(_root, "Racks"),
            LibraryFile: Path.Combine(_root, "Libraries", "DesktopWorkspace.library-ms"));
        Directory.CreateDirectory(_paths.Desktop);
    }

    public void Dispose()
    {
        try { SafeDelete.DeleteDirectoryRecursive(_root); } catch { }
    }

    private string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Sandbox(string id = "0123abcd") => Path.Combine(_paths.VirtualFramesRoot, id);

    private void SeedWorkspaceLikeRacksDoes()
    {
        var di = Directory.CreateDirectory(_paths.Workspace);
        Write(Path.Combine(_paths.Workspace, "desktop.ini"), "[.ShellClassInfo]");
        Write(Path.Combine(_paths.Workspace, ".RacksIcon.ico"), "icon");
        di.Attributes |= FileAttributes.Hidden | FileAttributes.ReadOnly;
    }

    [Fact]
    public void Workspace_files_and_folders_go_back_to_the_desktop_and_the_workspace_is_removed()
    {
        SeedWorkspaceLikeRacksDoes();
        Write(Path.Combine(_paths.Workspace, "report.docx"), "report");
        Write(Path.Combine(_paths.Workspace, "Photos", "a.jpg"), "jpg");

        var r = UninstallCleanup.Run(_paths);

        Assert.Equal("report", File.ReadAllText(Path.Combine(_paths.Desktop, "report.docx")));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(_paths.Desktop, "Photos", "a.jpg")));
        Assert.False(File.Exists(Path.Combine(_paths.Desktop, "desktop.ini")));
        Assert.False(File.Exists(Path.Combine(_paths.Desktop, ".RacksIcon.ico")));
        Assert.False(Directory.Exists(_paths.Workspace));
        Assert.Equal(2, r.Returned);
        Assert.Equal(0, r.Kept);
        Assert.Empty(r.KeptFolders);
    }

    [Fact]
    public void Name_clashes_never_overwrite_the_desktop()
    {
        Write(Path.Combine(_paths.Desktop, "notes.txt"), "desktop original");
        Write(Path.Combine(_paths.Desktop, "notes (from Racks).txt"), "older copy");
        Write(Path.Combine(_paths.Workspace, "notes.txt"), "from workspace");
        Write(Path.Combine(Sandbox(), "notes.txt"), "from sandbox");

        var r = UninstallCleanup.Run(_paths);

        Assert.Equal("desktop original", File.ReadAllText(Path.Combine(_paths.Desktop, "notes.txt")));
        Assert.Equal("older copy", File.ReadAllText(Path.Combine(_paths.Desktop, "notes (from Racks).txt")));
        var returned = new[] { "notes (from Racks 2).txt", "notes (from Racks 3).txt" }
            .Select(n => File.ReadAllText(Path.Combine(_paths.Desktop, n))).Order().ToArray();
        Assert.Equal(new[] { "from sandbox", "from workspace" }, returned);
        Assert.Equal(2, r.Returned);
    }

    [Fact]
    public void Sandbox_files_go_back_and_empty_sandboxes_are_removed()
    {
        Write(Path.Combine(Sandbox(), "budget.xlsx"), "xlsx");

        var r = UninstallCleanup.Run(_paths);

        Assert.Equal("xlsx", File.ReadAllText(Path.Combine(_paths.Desktop, "budget.xlsx")));
        Assert.False(Directory.Exists(_paths.VirtualFramesRoot));
        Assert.Equal(1, r.Returned);
    }

    [Fact]
    public void Junctions_are_unlinked_and_their_targets_are_untouched()
    {
        string target = Path.Combine(_root, "RealFolder");
        Write(Path.Combine(target, "keep.txt"), "keep me");
        Directory.CreateDirectory(Sandbox());
        string junction = Path.Combine(Sandbox(), "RealFolder");
        Assert.True(JunctionHelper.TryCreate(target, junction), "test setup: junction");

        var r = UninstallCleanup.Run(_paths);

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(_paths.Desktop, "RealFolder")));
        Assert.Equal(1, r.Unlinked);
        Assert.Equal(0, r.Returned);
    }

    [Fact]
    public void An_extra_hard_link_is_removed_and_the_original_name_keeps_the_data()
    {
        string original = Write(Path.Combine(_paths.Desktop, "song.mp3"), "audio");
        Directory.CreateDirectory(Sandbox());
        string link = Path.Combine(Sandbox(), "song.mp3");
        Assert.True(HardlinkHelper.TryCreate(original, link), "test setup: hard link");
        Assert.Equal(2, HardlinkHelper.GetLinkCount(original));

        var r = UninstallCleanup.Run(_paths);

        Assert.Equal("audio", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(_paths.Desktop, "song (from Racks).mp3")));
        Assert.Equal(1, HardlinkHelper.GetLinkCount(original));
        Assert.Equal(1, r.Unlinked);
    }

    [Fact]
    public void Locked_files_stay_in_a_visible_folder_with_a_note()
    {
        SeedWorkspaceLikeRacksDoes();
        string lockedInWorkspace = Write(Path.Combine(_paths.Workspace, "open.pdf"), "pdf");
        string lockedInSandbox = Write(Path.Combine(Sandbox(), "open.psd"), "psd");
        Write(Path.Combine(_paths.Workspace, "free.txt"), "free");

        CleanupReport r;
        using (new FileStream(lockedInWorkspace, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(lockedInSandbox, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = UninstallCleanup.Run(_paths, new Dictionary<string, string> { [Sandbox()] = "Design work" });
        }

        Assert.Equal("free", File.ReadAllText(Path.Combine(_paths.Desktop, "free.txt")));
        Assert.Equal("pdf", File.ReadAllText(lockedInWorkspace));
        Assert.Equal(2, r.Kept);
        Assert.Contains(_paths.Workspace, r.KeptFolders);

        var attrs = new DirectoryInfo(_paths.Workspace).Attributes;
        Assert.False(attrs.HasFlag(FileAttributes.Hidden));
        Assert.False(attrs.HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.Exists(Path.Combine(_paths.Workspace, "desktop.ini")));
        Assert.True(File.Exists(Path.Combine(_paths.Workspace, UninstallCleanup.KeptNoteName)));

        // The locked sandbox file is still somewhere visible: either the sandbox was renamed into
        // the workspace, or (if Windows refused the rename while the file was open) it stayed put.
        bool inWorkspace = File.Exists(Path.Combine(_paths.Workspace, "Design work", "open.psd"));
        bool inSandbox = File.Exists(lockedInSandbox);
        Assert.True(inWorkspace || inSandbox);
        if (inSandbox) Assert.Contains(Sandbox(), r.KeptFolders);
    }

    [Fact]
    public void Mirror_junctions_and_library_are_removed_but_user_content_in_the_mirror_survives()
    {
        string target = Path.Combine(_root, "SomeSandbox");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(_paths.MirrorRoot);
        Assert.True(JunctionHelper.TryCreate(target, Path.Combine(_paths.MirrorRoot, "Work")), "test setup: junction");
        Write(Path.Combine(_paths.MirrorRoot, "MyOwnFolder", "mine.txt"), "mine");
        Write(_paths.LibraryFile, "<libraryDescription/>");

        UninstallCleanup.Run(_paths);

        Assert.False(Directory.Exists(Path.Combine(_paths.MirrorRoot, "Work")));
        Assert.True(Directory.Exists(target));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_paths.MirrorRoot, "MyOwnFolder", "mine.txt")));
        Assert.False(File.Exists(_paths.LibraryFile));
    }

    [Fact]
    public void Running_twice_changes_nothing_the_second_time()
    {
        SeedWorkspaceLikeRacksDoes();
        Write(Path.Combine(_paths.Workspace, "a.txt"), "a");
        UninstallCleanup.Run(_paths);
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();

        var second = UninstallCleanup.Run(_paths);

        Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal(0, second.Returned + second.Unlinked + second.Kept);
    }

    [Fact]
    public void Nothing_to_clean_is_fine()
    {
        var r = UninstallCleanup.Run(_paths);
        Assert.Equal(0, r.Returned + r.Unlinked + r.Kept);
        Assert.Empty(r.Errors);
    }

    [Theory]
    [InlineData("a.txt", false, 0, "a.txt")]
    [InlineData("a.txt", false, 1, "a (from Racks).txt")]
    [InlineData("a.txt", false, 3, "a (from Racks 3).txt")]
    [InlineData("My.Folder", true, 1, "My.Folder (from Racks)")]
    public void Candidate_names(string name, bool isDirectory, int attempt, string expected)
        => Assert.Equal(expected, UninstallCleanup.CandidateName(name, isDirectory, attempt));

    [Fact]
    public void Report_is_written_as_key_value_lines()
    {
        var r = new CleanupReport { };
        r.KeptFolders.Add(@"C:\x");
        r.Errors.Add("line1\nline2");
        Assert.Equal(new[] { "returned=0", "unlinked=0", "kept=0", @"keptFolder=C:\x", "error=line1 line2" }, r.Lines());
    }
}
