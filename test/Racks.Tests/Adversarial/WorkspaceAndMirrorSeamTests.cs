using System.IO;
using System.Xml.Linq;
using Racks.Core;
using Racks.Util;

namespace Racks.Tests.Adversarial;

// P11b: DesktopIconManager.Setup (the workspace folder, its icon, desktop.ini and the library file) and
// RackMirror.Rebuild (one junction per virtual rack) run against temp folders through their internal seams,
// the same code Initialize() and the public Rebuild() call with the real locations.
[Trait("Category", "Adversarial")]
public sealed class DesktopIconManagerSetupTests : AdversarialTree
{
    private string Workspace => P("home", "RacksWorkspace");
    private string Libraries => P("appdata", "Microsoft", "Windows", "Libraries");
    private string Desktop => P("home", "Desktop");

    private string IconSource(string content = "icon v1")
    {
        string icon = Write(P("app", "Icon", "WorkspaceFolder.ico"), content);
        return icon;
    }

    private void Run(string workspace, string icon, string libraries, string desktop)
        => DesktopIconManager.Setup(workspace, icon, libraries, desktop);

    [Fact]
    public void A_first_run_makes_a_hidden_workspace_with_a_hidden_icon_a_desktop_ini_and_the_library()
    {
        Directory.CreateDirectory(Libraries);

        Run(Workspace, IconSource(), Libraries, Desktop);

        var attrs = new DirectoryInfo(Workspace).Attributes;
        Assert.True(attrs.HasFlag(FileAttributes.Hidden));
        Assert.True(attrs.HasFlag(FileAttributes.ReadOnly), "the shell only honors desktop.ini on a read-only folder");
        string icon = Path.Combine(Workspace, ".RacksIcon.ico");
        Assert.Equal("icon v1", File.ReadAllText(icon));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(icon) & (FileAttributes.Hidden | FileAttributes.System));
        string ini = Path.Combine(Workspace, "desktop.ini");
        Assert.Contains("IconResource=.RacksIcon.ico,0", File.ReadAllText(ini));
        Assert.True(File.GetAttributes(ini).HasFlag(FileAttributes.Hidden) && File.GetAttributes(ini).HasFlag(FileAttributes.System));

        var library = XDocument.Load(Path.Combine(Libraries, "DesktopWorkspace.library-ms"));
        var urls = library.Descendants().Where(e => e.Name.LocalName == "url").Select(e => e.Value).ToList();
        Assert.Equal(new[] { Desktop, Workspace }, urls);
    }

    [Fact]
    public void Running_it_again_changes_nothing_that_matters_and_never_throws()
    {
        Directory.CreateDirectory(Libraries);
        string icon = IconSource();
        Run(Workspace, icon, Libraries, Desktop);
        string iniBefore = File.ReadAllText(Path.Combine(Workspace, "desktop.ini"));

        var ex = Record.Exception(() => Run(Workspace, icon, Libraries, Desktop));

        Assert.Null(ex);
        Assert.Equal("icon v1", File.ReadAllText(Path.Combine(Workspace, ".RacksIcon.ico")));
        Assert.Equal(iniBefore, File.ReadAllText(Path.Combine(Workspace, "desktop.ini")));
        var attrs = new DirectoryInfo(Workspace).Attributes;
        Assert.True(attrs.HasFlag(FileAttributes.Hidden) && attrs.HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.Exists(Path.Combine(Libraries, "DesktopWorkspace.library-ms")));
    }

    [Fact]
    public void A_newer_icon_replaces_the_hidden_system_copy_and_keeps_it_hidden()
    {
        Directory.CreateDirectory(Libraries);
        string source = IconSource("icon v1");
        Run(Workspace, source, Libraries, Desktop);

        File.WriteAllText(source, "icon v2");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(5));
        Run(Workspace, source, Libraries, Desktop);

        string copy = Path.Combine(Workspace, ".RacksIcon.ico");
        Assert.Equal("icon v2", File.ReadAllText(copy));
        Assert.True(File.GetAttributes(copy).HasFlag(FileAttributes.Hidden) && File.GetAttributes(copy).HasFlag(FileAttributes.System));
    }

    [Fact]
    public void Files_the_user_has_in_the_workspace_are_never_touched()
    {
        Directory.CreateDirectory(Libraries);
        string mine = Write(Path.Combine(Workspace, "report.docx"), "mine");
        string hidden = Write(Path.Combine(Workspace, "Proj", ".hidden"), "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Run(Workspace, IconSource(), Libraries, Desktop);
        Run(Workspace, IconSource(), Libraries, Desktop);

        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.Equal("hidden", File.ReadAllText(hidden));
    }

    [Fact]
    public void Without_the_icon_file_the_workspace_still_exists_and_there_is_no_desktop_ini()
    {
        Directory.CreateDirectory(Libraries);

        Run(Workspace, P("app", "Icon", "missing.ico"), Libraries, Desktop);

        Assert.True(Directory.Exists(Workspace));
        Assert.False(File.Exists(Path.Combine(Workspace, "desktop.ini")));
        Assert.False(File.Exists(Path.Combine(Workspace, ".RacksIcon.ico")));
        Assert.True(File.Exists(Path.Combine(Libraries, "DesktopWorkspace.library-ms")));
    }

    [Fact]
    public void Without_a_Libraries_folder_no_library_is_written_and_the_folder_is_not_created()
    {
        Run(Workspace, IconSource(), Libraries, Desktop);

        Assert.True(Directory.Exists(Workspace));
        Assert.False(Directory.Exists(Libraries));
    }

    [Fact]
    public void A_file_where_the_workspace_should_be_is_left_alone_and_nothing_throws()
    {
        string squatter = Write(Workspace, "I am a file called RacksWorkspace");
        Directory.CreateDirectory(Libraries);

        var ex = Record.Exception(() => Run(Workspace, IconSource(), Libraries, Desktop));

        Assert.Null(ex);
        Assert.Equal("I am a file called RacksWorkspace", File.ReadAllText(squatter));
    }

    [Fact]
    public void An_unwritable_libraries_target_does_not_stop_the_rest_or_throw()
    {
        string libraries = Dir("appdata", "Libraries");
        Directory.CreateDirectory(Path.Combine(libraries, "DesktopWorkspace.library-ms")); // a folder with the library's name

        var ex = Record.Exception(() => Run(Workspace, IconSource(), libraries, Desktop));

        Assert.Null(ex);
        Assert.True(File.Exists(Path.Combine(Workspace, "desktop.ini")));
        Assert.True(Directory.Exists(Path.Combine(libraries, "DesktopWorkspace.library-ms")));
    }

    [Fact]
    public void A_workspace_under_unicode_and_symbol_folders_gets_the_same_treatment()
    {
        string workspace = P("café 日本語 [x] 'y' 100% \U0001F4C1", "RacksWorkspace");
        Directory.CreateDirectory(Libraries);

        Run(workspace, IconSource(), Libraries, Desktop);

        Assert.True(File.Exists(Path.Combine(workspace, "desktop.ini")));
        var urls = XDocument.Load(Path.Combine(Libraries, "DesktopWorkspace.library-ms")).Descendants()
            .Where(e => e.Name.LocalName == "url").Select(e => e.Value).ToList();
        Assert.Equal(new[] { Desktop, workspace }, urls);
    }

    [Fact]
    public void A_path_with_an_ampersand_or_angle_bracket_still_gives_a_valid_library_file()
    {
        // P11 finding F7: the two paths are XML-escaped when the library file is built (a user called "Tom & Jerry").
        Directory.CreateDirectory(Libraries);
        string desktop = P("Tom & Jerry <x>", "Desktop");

        Run(Workspace, IconSource(), Libraries, desktop);

        string text = File.ReadAllText(Path.Combine(Libraries, "DesktopWorkspace.library-ms"));
        Assert.Contains("Tom &amp; Jerry &lt;x&gt;", text);
        var urls = XDocument.Parse(text).Descendants().Where(e => e.Name.LocalName == "url").Select(e => e.Value).ToList();
        Assert.Equal(new[] { desktop, Workspace }, urls);
        Assert.True(File.Exists(Path.Combine(Workspace, "desktop.ini")));
    }
}

[Trait("Category", "Adversarial")]
public sealed class RackMirrorRebuildTests : AdversarialTree
{
    private string Mirror => P("home", "Racks");

    private string Sandbox(string id, string content = "x")
    {
        string dir = Dir("sandboxes", id);
        Write(Path.Combine(dir, "item.txt"), content);
        return dir;
    }

    private static List<string> Names(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Select(e => Path.GetFileName(e)!).Order(StringComparer.OrdinalIgnoreCase).ToList();

    [Fact]
    public void Each_rack_gets_a_junction_named_after_its_title_pointing_at_its_sandbox()
    {
        string a = Sandbox("a", "from a");
        string b = Sandbox("b", "from b");

        RackMirror.Rebuild([("Work", a), ("Home", b)], Mirror);

        Assert.Equal(new[] { "Home", "Work" }, Names(Mirror));
        Assert.True(JunctionHelper.IsLink(Path.Combine(Mirror, "Work")));
        Assert.Equal("from a", File.ReadAllText(Path.Combine(Mirror, "Work", "item.txt")));
        Assert.Equal("from b", File.ReadAllText(Path.Combine(Mirror, "Home", "item.txt")));
    }

    [Fact]
    public void Racks_with_the_same_title_get_numbered_names_and_no_junction_overwrites_another()
    {
        RackMirror.Rebuild([("Work", Sandbox("a", "A")), ("work", Sandbox("b", "B")), ("Work", Sandbox("c", "C"))], Mirror);

        Assert.Equal(new[] { "Work", "work (2)", "Work (3)" }.Order(StringComparer.OrdinalIgnoreCase), Names(Mirror));
        Assert.Equal("A", File.ReadAllText(Path.Combine(Mirror, "Work", "item.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(Mirror, "work (2)", "item.txt")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(Mirror, "Work (3)", "item.txt")));
    }

    [Fact]
    public void A_stale_junction_is_replaced_and_its_old_target_survives()
    {
        string oldTarget = Sandbox("old", "old content");
        string newTarget = Sandbox("new", "new content");
        RackMirror.Rebuild([("Work", oldTarget)], Mirror);
        Assert.Equal("old content", File.ReadAllText(Path.Combine(Mirror, "Work", "item.txt")));

        RackMirror.Rebuild([("Work", newTarget)], Mirror);

        Assert.Equal(new[] { "Work" }, Names(Mirror));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(Mirror, "Work", "item.txt")));
        Assert.Equal("old content", File.ReadAllText(Path.Combine(oldTarget, "item.txt")));
    }

    [Fact]
    public void A_junction_for_a_rack_that_is_gone_is_removed_and_a_missing_sandbox_gets_none()
    {
        string a = Sandbox("a");
        RackMirror.Rebuild([("Gone", a), ("Kept", Sandbox("k"))], Mirror);

        RackMirror.Rebuild([("Kept", Sandbox("k")), ("No sandbox", P("sandboxes", "never-created")), ("Empty path", "")], Mirror);

        Assert.Equal(new[] { "Kept" }, Names(Mirror));
        Assert.True(Directory.Exists(a), "the sandbox of a rack that left the mirror is not touched");
    }

    [Fact]
    public void A_real_folder_or_file_with_the_same_name_is_left_alone_and_the_rack_takes_the_next_free_name()
    {
        Write(Path.Combine(Mirror, "Work", "mine.txt"), "mine");
        Write(Path.Combine(Mirror, "Home"), "a file called Home");

        RackMirror.Rebuild([("Work", Sandbox("a", "A")), ("Home", Sandbox("b", "B"))], Mirror);

        Assert.Equal("mine", File.ReadAllText(Path.Combine(Mirror, "Work", "mine.txt")));
        Assert.Equal("a file called Home", File.ReadAllText(Path.Combine(Mirror, "Home")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(Mirror, "Work (2)", "item.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(Mirror, "Home (2)", "item.txt")));
    }

    [Fact]
    public void A_reparse_point_that_is_not_a_link_is_left_alone_and_its_name_counts_as_taken()
    {
        string placeholder = NonLinkReparsePoint(Path.Combine(Mirror, "Work"), directory: true);

        RackMirror.Rebuild([("Work", Sandbox("a", "A"))], Mirror);

        Assert.True(Directory.Exists(placeholder) && JunctionHelper.IsReparsePoint(placeholder) && !JunctionHelper.IsLink(placeholder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(Mirror, "Work (2)", "item.txt")));
    }

    [Fact]
    public void Titles_with_forbidden_characters_dots_emoji_or_great_length_become_safe_names()
    {
        string longTitle = new string('t', 200);
        RackMirror.Rebuild([
            ("a/b:c*d?e", Sandbox("1")),
            ("trailing dot...", Sandbox("2")),
            ("  padded  ", Sandbox("3")),
            ("\U0001F4C1 projects", Sandbox("4")),
            ("", Sandbox("5")),
            (longTitle, Sandbox("6")),
        ], Mirror);

        var names = Names(Mirror);
        Assert.Contains("abcde", names);
        Assert.Contains("trailing dot", names);
        Assert.Contains("padded", names);
        Assert.Contains("\U0001F4C1 projects", names);
        Assert.Contains("Rack", names);
        Assert.Contains(new string('t', 80), names);
        Assert.Equal(6, names.Count);
    }

    [Fact]
    public void When_every_numbered_name_is_taken_the_rack_gets_no_junction_and_nothing_is_removed()
    {
        Directory.CreateDirectory(Path.Combine(Mirror, "T"));
        for (int i = 2; i <= 999; i++) Directory.CreateDirectory(Path.Combine(Mirror, $"T ({i})"));
        var before = Names(Mirror);

        var ex = Record.Exception(() => RackMirror.Rebuild([("T", Sandbox("a"))], Mirror));

        Assert.Null(ex);
        Assert.Equal(before, Names(Mirror));
        Assert.All(Directory.EnumerateFileSystemEntries(Mirror), e => Assert.False(JunctionHelper.IsReparsePoint(e)));
    }

    [Fact]
    public void Rebuilding_twice_gives_the_same_set_and_the_mirror_root_is_created_when_missing()
    {
        var racks = new[] { ("Work", Sandbox("a")), ("Home", Sandbox("b")), ("Work", Sandbox("c")) };

        RackMirror.Rebuild(racks, Mirror);
        var first = Names(Mirror);
        RackMirror.Rebuild(racks, Mirror);

        Assert.Equal(first, Names(Mirror));
        Assert.Equal(3, first.Count);
    }

    [Fact]
    public void A_mirror_root_that_is_a_file_does_not_throw_and_the_file_survives()
    {
        string file = Write(Mirror, "I am a file called Racks");

        var ex = Record.Exception(() => RackMirror.Rebuild([("Work", Sandbox("a"))], Mirror));

        Assert.Null(ex);
        Assert.Equal("I am a file called Racks", File.ReadAllText(file));
    }

    [Fact]
    public void A_sandbox_that_is_a_file_gets_no_junction()
    {
        string notAFolder = Write(P("sandboxes", "file.txt"), "f");

        RackMirror.Rebuild([("Work", notAFolder)], Mirror);

        Assert.Empty(Names(Mirror));
        Assert.Equal("f", File.ReadAllText(notAFolder));
    }
}
