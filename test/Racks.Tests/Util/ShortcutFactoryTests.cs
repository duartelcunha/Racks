using System.IO;
using Racks.Util;

namespace Racks.Tests.Util;

// Ctrl+drop must leave something in the rack's folder under a name the caller can claim. The old code
// made a hardlink called "Beach.jpg" but claimed "Beach.jpg.lnk", so the dropped item never showed.
public sealed class ShortcutFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-shortcut-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _folder;

    public ShortcutFactoryTests()
    {
        _source = Path.Combine(_root, "desktop");
        _folder = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string MakeFile(string name, string content = "demo")
    {
        string path = Path.Combine(_source, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_file_gets_a_hardlink_with_the_same_name_and_the_source_stays()
    {
        string src = MakeFile("Beach.jpg", "pixels");

        string created = ShortcutFactory.Create(src, _folder);

        Assert.Equal(Path.Combine(_folder, "Beach.jpg"), created);
        Assert.Equal("pixels", File.ReadAllText(created));
        Assert.True(File.Exists(src));
        Assert.Equal(2, HardlinkHelper.GetLinkCount(src));
    }

    [Fact]
    public void The_destination_name_is_used_when_the_caller_has_already_made_it_unique()
    {
        string src = MakeFile("Beach.jpg");
        File.WriteAllText(Path.Combine(_folder, "Beach.jpg"), "already here");

        string created = ShortcutFactory.Create(src, _folder, "Beach (1).jpg");

        Assert.Equal(Path.Combine(_folder, "Beach (1).jpg"), created);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(_folder, "Beach.jpg")));
    }

    [Fact]
    public void A_name_collision_never_overwrites_and_returns_the_path_it_created()
    {
        string src = MakeFile("Beach.jpg");
        string existing = Path.Combine(_folder, "Beach.jpg");
        File.WriteAllText(existing, "already here");

        string created = ShortcutFactory.Create(src, _folder);

        Assert.NotEqual(existing, created);
        Assert.True(File.Exists(created), created);
        Assert.Equal("already here", File.ReadAllText(existing));
    }

    [Fact]
    public void A_folder_gets_a_junction_that_shows_its_contents()
    {
        string dir = Path.Combine(_source, "Projects");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "hi");

        string created = ShortcutFactory.Create(dir, _folder);

        Assert.Equal(Path.Combine(_folder, "Projects"), created);
        Assert.True(File.Exists(Path.Combine(created, "readme.txt")));
    }

    [Fact]
    public void UniquePath_numbers_like_explorer()
    {
        string a = Path.Combine(_folder, "a.txt");
        Assert.Equal(a, ShortcutFactory.UniquePath(a));
        File.WriteAllText(a, "x");
        Assert.Equal(Path.Combine(_folder, "a (1).txt"), ShortcutFactory.UniquePath(a));
        File.WriteAllText(Path.Combine(_folder, "a (1).txt"), "x");
        Assert.Equal(Path.Combine(_folder, "a (2).txt"), ShortcutFactory.UniquePath(a));
    }
}
