using System.IO;
using Racks.Rack;
using Racks.Util;

namespace Racks.Tests.Rack;

// Removing a desktop rack returns its files to the Desktop. A file whose name was already taken there
// used to stay behind in the hidden workspace.
public sealed class WorkspaceReturnTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-return-" + Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly string _desktop;

    public WorkspaceReturnTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _desktop = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_desktop);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_file_comes_back_under_its_own_name()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "one");

        var back = WorkspaceReturn.ToDesktop(_workspace, _desktop, "a.txt");

        Assert.Equal(Path.Combine(_desktop, "a.txt"), back);
        Assert.Equal("one", File.ReadAllText(back!));
        Assert.False(File.Exists(Path.Combine(_workspace, "a.txt")));
    }

    [Fact]
    public void A_different_file_with_the_same_name_comes_back_renamed_and_nothing_is_overwritten()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "mine");
        File.WriteAllText(Path.Combine(_desktop, "a.txt"), "already here");

        var back = WorkspaceReturn.ToDesktop(_workspace, _desktop, "a.txt");

        Assert.Equal(Path.Combine(_desktop, "a (from Racks).txt"), back);
        Assert.Equal("mine", File.ReadAllText(back!));
        Assert.Equal("already here", File.ReadAllText(Path.Combine(_desktop, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_workspace, "a.txt")), "must not stay hidden in the workspace");
    }

    [Fact]
    public void A_ctrl_drop_link_of_a_file_still_on_the_desktop_is_just_dropped()
    {
        string onDesktop = Path.Combine(_desktop, "b.jpg");
        File.WriteAllText(onDesktop, "pixels");
        Assert.True(HardlinkHelper.TryCreate(onDesktop, Path.Combine(_workspace, "b.jpg")), "test setup: hard link");

        var back = WorkspaceReturn.ToDesktop(_workspace, _desktop, "b.jpg");

        Assert.Equal(onDesktop, back);
        Assert.Equal("pixels", File.ReadAllText(onDesktop));
        Assert.False(File.Exists(Path.Combine(_workspace, "b.jpg")));
        Assert.False(File.Exists(Path.Combine(_desktop, "b (from Racks).jpg")), "no duplicate for a link");
    }

    [Fact]
    public void A_folder_with_a_taken_name_comes_back_renamed()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "Projects"));
        Directory.CreateDirectory(Path.Combine(_desktop, "Projects"));

        var back = WorkspaceReturn.ToDesktop(_workspace, _desktop, "Projects");

        Assert.Equal(Path.Combine(_desktop, "Projects (from Racks)"), back);
        Assert.True(Directory.Exists(back));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\evil.txt")]
    [InlineData("missing.txt")]
    public void Odd_or_missing_names_return_nothing(string name)
    {
        Assert.Null(WorkspaceReturn.ToDesktop(_workspace, _desktop, name));
    }
}
