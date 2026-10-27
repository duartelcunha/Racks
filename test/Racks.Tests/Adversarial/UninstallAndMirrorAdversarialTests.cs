using System.IO;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Adversarial;

// P11: the uninstall / remove-rack clean-up and the mirror junction clean-up against hostile content, on real
// NTFS in a temp tree. INV-UNINSTALL-1: nothing that holds data which exists nowhere else is ever deleted.
[Trait("Category", "Adversarial")]
public sealed class UninstallCleanupAdversarialTests : AdversarialTree
{
    private CleanupPaths Paths(string folderName = "scenario")
    {
        string baseDir = Path.Combine(Root, folderName);
        var paths = new CleanupPaths(
            Workspace: Path.Combine(baseDir, "RacksWorkspace"),
            VirtualFramesRoot: Path.Combine(baseDir, "AppData", "Racks", "VirtualFrames"),
            Desktop: Path.Combine(baseDir, "Desktop"),
            MirrorRoot: Path.Combine(baseDir, "Racks"),
            LibraryFile: Path.Combine(baseDir, "Libraries", "DesktopWorkspace.library-ms"));
        Directory.CreateDirectory(paths.Desktop);
        return paths;
    }

    private static void SeedWorkspaceLikeRacksDoes(CleanupPaths paths)
    {
        var di = Directory.CreateDirectory(paths.Workspace);
        File.WriteAllText(Path.Combine(paths.Workspace, "desktop.ini"), "[.ShellClassInfo]");
        File.WriteAllText(Path.Combine(paths.Workspace, ".RacksIcon.ico"), "icon");
        di.Attributes |= FileAttributes.Hidden | FileAttributes.ReadOnly;
    }

    // ---- locked and hidden content ---------------------------------------------------------

    [Fact]
    public void A_workspace_with_only_a_locked_file_is_kept_visible_with_the_note_and_the_file_intact()
    {
        var paths = Paths();
        SeedWorkspaceLikeRacksDoes(paths);
        string locked = Write(Path.Combine(paths.Workspace, "open.xlsx"), "sheet");

        CleanupReport r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Kept);
        Assert.Equal(0, r.Returned);
        Assert.Contains(paths.Workspace, r.KeptFolders);
        Assert.Contains(r.Errors, e => e.StartsWith("open.xlsx:"));
        Assert.Equal("sheet", File.ReadAllText(locked));
        Assert.True(File.Exists(Path.Combine(paths.Workspace, UninstallCleanup.KeptNoteName)));
        Assert.False(File.Exists(Path.Combine(paths.Workspace, "desktop.ini")));
        Assert.False(File.Exists(Path.Combine(paths.Workspace, ".RacksIcon.ico")));
        var attrs = new DirectoryInfo(paths.Workspace).Attributes;
        Assert.False(attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void Running_again_after_the_lock_is_gone_returns_the_file_and_removes_the_workspace()
    {
        var paths = Paths();
        SeedWorkspaceLikeRacksDoes(paths);
        string locked = Write(Path.Combine(paths.Workspace, "open.xlsx"), "sheet");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            UninstallCleanup.Run(paths);

        var second = UninstallCleanup.Run(paths);

        // FINDING P11-F6 (cosmetic): step 1 skips only desktop.ini and the icon as Racks-owned, so the note written
        // by the first run is treated as a user file and travels to the Desktop with the file it was about. Nothing
        // is lost; the leftover note is just noise. Pinned (2 returned); the fix is to also skip KeptNoteName there.
        Assert.Equal(2, second.Returned);
        Assert.Equal("sheet", File.ReadAllText(Path.Combine(paths.Desktop, "open.xlsx")));
        Assert.True(File.Exists(Path.Combine(paths.Desktop, UninstallCleanup.KeptNoteName)));
        Assert.False(Directory.Exists(paths.Workspace));
    }

    [Fact]
    public void A_folder_with_a_locked_file_inside_is_never_split_it_comes_back_whole_or_stays_whole()
    {
        var paths = Paths();
        Write(Path.Combine(paths.Workspace, "Proj", "a.txt"), "a");
        string locked = Write(Path.Combine(paths.Workspace, "Proj", "sub", "b.txt"), "b");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            UninstallCleanup.Run(paths);

        foreach (var root in new[] { Path.Combine(paths.Desktop, "Proj"), Path.Combine(paths.Workspace, "Proj") })
        {
            if (!Directory.Exists(root)) continue;
            Assert.Equal("a", File.ReadAllText(Path.Combine(root, "a.txt")));
            Assert.Equal("b", File.ReadAllText(Path.Combine(root, "sub", "b.txt")));
            return;
        }
        Assert.Fail("the folder is in neither place");
    }

    [Fact]
    public void Nested_user_folders_with_hidden_and_system_content_come_back_whole_including_a_nested_desktop_ini()
    {
        var paths = Paths();
        SeedWorkspaceLikeRacksDoes(paths);
        string proj = Path.Combine(paths.Workspace, "Proj");
        Write(Path.Combine(proj, "visible.txt"), "visible");
        string hidden = Write(Path.Combine(proj, ".hidden.txt"), "hidden");
        string system = Write(Path.Combine(proj, "Deep", "system.dat"), "system");
        string nestedIni = Write(Path.Combine(proj, "Deep", "desktop.ini"), "[.ShellClassInfo]\r\nIconFile=x.ico");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden);
        File.SetAttributes(nestedIni, FileAttributes.System | FileAttributes.Hidden);
        var hiddenDir = Directory.CreateDirectory(Path.Combine(proj, ".git"));
        Write(Path.Combine(hiddenDir.FullName, "config"), "[core]");
        hiddenDir.Attributes |= FileAttributes.Hidden;

        var r = UninstallCleanup.Run(paths);

        string back = Path.Combine(paths.Desktop, "Proj");
        Assert.Equal(1, r.Returned);
        Assert.Equal("visible", File.ReadAllText(Path.Combine(back, "visible.txt")));
        Assert.Equal("hidden", File.ReadAllText(Path.Combine(back, ".hidden.txt")));
        Assert.Equal("system", File.ReadAllText(Path.Combine(back, "Deep", "system.dat")));
        Assert.Contains("IconFile", File.ReadAllText(Path.Combine(back, "Deep", "desktop.ini")));
        Assert.Equal("[core]", File.ReadAllText(Path.Combine(back, ".git", "config")));
        Assert.True(File.GetAttributes(Path.Combine(back, ".hidden.txt")).HasFlag(FileAttributes.Hidden));
        Assert.False(Directory.Exists(paths.Workspace));
    }

    [Fact]
    public void A_user_file_called_desktop_ini_in_a_rack_sandbox_is_returned_not_mistaken_for_racks_own_file()
    {
        var paths = Paths();
        Write(Path.Combine(paths.VirtualFramesRoot, "0123abcd", "desktop.ini"), "user's own");

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Returned);
        Assert.Equal("user's own", File.ReadAllText(Path.Combine(paths.Desktop, "desktop.ini")));
    }

    // ---- name clashes on the Desktop -------------------------------------------------------

    [Fact]
    public void A_folder_and_a_file_that_clash_with_Desktop_names_of_the_other_kind_get_free_names()
    {
        var paths = Paths();
        Write(Path.Combine(paths.Desktop, "Photos"), "a FILE called Photos");
        Directory.CreateDirectory(Path.Combine(paths.Desktop, "Notes"));
        Directory.CreateDirectory(Path.Combine(paths.Desktop, "Photos (from Racks)"));
        Write(Path.Combine(paths.Workspace, "Photos", "a.jpg"), "jpg");
        Write(Path.Combine(paths.Workspace, "Notes"), "a FILE called Notes");

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(2, r.Returned);
        Assert.Equal("a FILE called Photos", File.ReadAllText(Path.Combine(paths.Desktop, "Photos")));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(paths.Desktop, "Photos (from Racks 2)", "a.jpg")));
        Assert.Equal("a FILE called Notes", File.ReadAllText(Path.Combine(paths.Desktop, "Notes (from Racks)")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(paths.Desktop, "Photos (from Racks)")));
    }

    [Fact]
    public void When_all_1000_names_are_taken_the_item_stays_in_the_workspace_and_is_reported()
    {
        var paths = Paths();
        Write(Path.Combine(paths.Desktop, "n.txt"), "original");
        Write(Path.Combine(paths.Desktop, "n (from Racks).txt"), "c1");
        for (int i = 2; i <= 999; i++) Write(Path.Combine(paths.Desktop, $"n (from Racks {i}).txt"), "c" + i);
        string mine = Write(Path.Combine(paths.Workspace, "n.txt"), "mine");

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(0, r.Returned);
        Assert.Equal(1, r.Kept);
        Assert.Contains(r.Errors, e => e.Contains("no free name"));
        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.Equal("original", File.ReadAllText(Path.Combine(paths.Desktop, "n.txt")));
        Assert.Contains(paths.Workspace, r.KeptFolders);
    }

    // ---- links and other reparse points ----------------------------------------------------

    [Fact]
    public void Junctions_to_outside_content_are_unlinked_wherever_they_sit_and_nothing_they_point_at_is_touched()
    {
        var paths = Paths();
        string outside = Write(P("outside", "deep", "keep.txt"), "outside");
        Write(P("outside2", "keep2.txt"), "outside 2");
        Link(P("outside"), Path.Combine(paths.Workspace, "ref-to-outside"));
        string sandbox = Path.Combine(paths.VirtualFramesRoot, "0123abcd");
        Directory.CreateDirectory(sandbox);
        Link(P("outside"), Path.Combine(sandbox, "chain-one"));
        Link(Path.Combine(sandbox, "chain-one"), Path.Combine(sandbox, "chain-two"));
        string dangling = Link(Dir("gone-soon"), Path.Combine(sandbox, "dangling"));
        Directory.Delete(P("gone-soon"));

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(4, r.Unlinked);
        Assert.Equal(0, r.Returned);
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.Equal("outside 2", File.ReadAllText(P("outside2", "keep2.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(paths.Desktop));
        Assert.False(Directory.Exists(dangling));
    }

    [Fact]
    public void A_junction_to_a_protected_folder_is_unlinked_and_the_protected_folder_is_untouched()
    {
        var paths = Paths();
        string prot = Dir("protected-folder");
        Write(Path.Combine(prot, "precious.txt"), "precious");
        Link(prot, Path.Combine(paths.VirtualFramesRoot, "0123abcd", "Documents"));

        CleanupReport r;
        using (Racks.Util.SafeMove.UseProtectedFoldersForTest(prot))
            r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Unlinked);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(prot, "precious.txt")));
        Assert.False(Directory.Exists(Path.Combine(paths.VirtualFramesRoot, "0123abcd", "Documents")));
    }

    [Fact]
    public void A_junction_inside_a_nested_folder_travels_with_the_folder_and_its_target_is_untouched()
    {
        var paths = Paths();
        string outside = Write(P("outside", "keep.txt"), "outside");
        Write(Path.Combine(paths.Workspace, "Proj", "own.txt"), "own");
        Link(P("outside"), Path.Combine(paths.Workspace, "Proj", "pointer"));

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Returned);
        Assert.True(JunctionHelper.IsLink(Path.Combine(paths.Desktop, "Proj", "pointer")));
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.Equal("own", File.ReadAllText(Path.Combine(paths.Desktop, "Proj", "own.txt")));
    }

    [SymlinkFact]
    public void A_file_symlink_is_unlinked_and_its_target_file_is_untouched()
    {
        var paths = Paths();
        string target = Write(P("outside", "real.txt"), "real");
        string sandbox = Path.Combine(paths.VirtualFramesRoot, "0123abcd");
        Directory.CreateDirectory(sandbox);
        File.CreateSymbolicLink(Path.Combine(sandbox, "real.txt"), target);

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Unlinked);
        Assert.Equal("real", File.ReadAllText(target));
        Assert.Empty(Directory.GetFileSystemEntries(paths.Desktop));
    }

    [Fact]
    public void A_reparse_point_that_is_not_a_link_is_data_so_it_is_returned_and_never_unlinked_or_deleted()
    {
        // Stands in for a OneDrive placeholder: INV-UNINSTALL-1 says it is renamed back like any other item.
        var paths = Paths();
        string sandbox = Path.Combine(paths.VirtualFramesRoot, "0123abcd");
        NonLinkReparsePoint(Path.Combine(sandbox, "Cloud folder"), directory: true);
        NonLinkReparsePoint(Path.Combine(sandbox, "cloud.docx"), directory: false);

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(2, r.Returned);
        Assert.Equal(0, r.Unlinked);
        Assert.True(Directory.Exists(Path.Combine(paths.Desktop, "Cloud folder")));
        Assert.True(File.Exists(Path.Combine(paths.Desktop, "cloud.docx")));
        Assert.True(JunctionHelper.IsReparsePoint(Path.Combine(paths.Desktop, "Cloud folder")));
    }

    [Fact]
    public void A_sandbox_that_is_itself_a_junction_is_unlinked_as_a_pointer_and_not_emptied()
    {
        var paths = Paths();
        string outside = Write(P("outside", "keep.txt"), "outside");
        Directory.CreateDirectory(paths.VirtualFramesRoot);
        Link(P("outside"), Path.Combine(paths.VirtualFramesRoot, "0123abcd"));

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Unlinked);
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.Empty(Directory.GetFileSystemEntries(paths.Desktop));
    }

    // ---- names and paths -------------------------------------------------------------------

    [Fact]
    public void A_whole_scenario_under_unicode_and_symbol_paths_returns_every_file()
    {
        var paths = Paths("café 日本語 [x] 'y' 100% & \U0001F4C1");
        SeedWorkspaceLikeRacksDoes(paths);
        string[] names = ["résumé.docx", "日本語.txt", "emoji \U0001F600.png", "[a] 'b' 100% & c.txt"];
        foreach (var n in names) Write(Path.Combine(paths.Workspace, n), n);
        Write(Path.Combine(paths.VirtualFramesRoot, "école-0123", "tâche \U0001F4C1", "inner.txt"), "inner");
        Write(Path.Combine(paths.Desktop, "résumé.docx"), "already on the desktop");

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(5, r.Returned);
        Assert.Equal(0, r.Kept);
        Assert.Empty(r.Errors);
        Assert.Equal("already on the desktop", File.ReadAllText(Path.Combine(paths.Desktop, "résumé.docx")));
        Assert.Equal("résumé.docx", File.ReadAllText(Path.Combine(paths.Desktop, "résumé (from Racks).docx")));
        foreach (var n in names.Skip(1)) Assert.Equal(n, File.ReadAllText(Path.Combine(paths.Desktop, n)));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(paths.Desktop, "tâche \U0001F4C1", "inner.txt")));
        Assert.False(Directory.Exists(paths.Workspace));
        Assert.False(Directory.Exists(paths.VirtualFramesRoot));
    }

    [Fact]
    public void A_deep_rack_folder_beyond_260_characters_is_returned_as_one_rename()
    {
        var paths = Paths();
        string deep = Path.Combine(paths.Workspace, "Deep");
        for (int i = 0; i < 40; i++) deep = Path.Combine(deep, new string('x', 8));
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "bottom.txt"), "bottom");
        Assert.True(deep.Length > 260, "test setup");

        var r = UninstallCleanup.Run(paths);

        Assert.Equal(1, r.Returned);
        string back = Path.Combine(paths.Desktop, "Deep");
        for (int i = 0; i < 40; i++) back = Path.Combine(back, new string('x', 8));
        Assert.Equal("bottom", File.ReadAllText(Path.Combine(back, "bottom.txt")));
    }

    [Fact]
    public void Removing_a_rack_returns_nested_hidden_content_and_unlinks_a_junction_to_outside_content()
    {
        string folder = P("rack");
        string outside = Write(P("outside", "keep.txt"), "outside");
        string hidden = Write(Path.Combine(folder, "Proj", ".secret"), "secret");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        Link(P("outside"), Path.Combine(folder, "pointer"));
        string desktop = Dir("desktop");

        var report = UninstallCleanup.ReturnFolderToDesktop(folder, desktop);

        Assert.Equal(1, report.Returned);
        Assert.Equal(1, report.Unlinked);
        Assert.Equal("secret", File.ReadAllText(Path.Combine(desktop, "Proj", ".secret")));
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.False(Directory.Exists(folder));
        Assert.Empty(report.KeptFolders);
    }
}

[Trait("Category", "Adversarial")]
public sealed class RackMirrorJunctionAdversarialTests : AdversarialTree
{
    private string Mirror => Dir("Racks");

    [Fact]
    public void Only_links_are_removed_real_folders_files_and_hidden_content_with_the_same_look_stay()
    {
        string target = Write(P("sandbox", "keep.txt"), "keep me");
        string mirror = Mirror;
        Link(P("sandbox"), Path.Combine(mirror, "Work"));
        Link(P("sandbox"), Path.Combine(mirror, "Home"));
        Write(Path.Combine(mirror, "My own folder", "mine.txt"), "mine");
        string hiddenFile = Write(Path.Combine(mirror, ".hidden-note"), "hidden");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden | FileAttributes.System);

        int removed = RackMirror.RemoveJunctions(mirror);

        Assert.Equal(2, removed);
        Assert.Equal("keep me", File.ReadAllText(target));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(mirror, "My own folder", "mine.txt")));
        Assert.Equal("hidden", File.ReadAllText(hiddenFile));
    }

    [Fact]
    public void A_junction_inside_a_real_folder_of_the_mirror_is_not_reached()
    {
        // The clean-up looks at the mirror's own children only; a folder the user made there is theirs, links and all.
        string target = Write(P("sandbox", "keep.txt"), "keep me");
        string mirror = Mirror;
        string inner = Link(P("sandbox"), Path.Combine(mirror, "My folder", "inner-link"));
        Assert.True(Directory.Exists(inner));

        Assert.Equal(0, RackMirror.RemoveJunctions(mirror));

        Assert.True(JunctionHelper.IsLink(inner));
        Assert.Equal("keep me", File.ReadAllText(target));
    }

    [Fact]
    public void A_reparse_point_that_is_not_a_link_is_left_alone()
    {
        string mirror = Mirror;
        string placeholder = NonLinkReparsePoint(Path.Combine(mirror, "Cloud folder"), directory: true);
        string placeholderFile = NonLinkReparsePoint(Path.Combine(mirror, "cloud.docx"), directory: false);
        Link(Dir("sandbox"), Path.Combine(mirror, "Work"));

        int removed = RackMirror.RemoveJunctions(mirror);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(placeholder) && JunctionHelper.IsReparsePoint(placeholder));
        Assert.True(File.Exists(placeholderFile) && JunctionHelper.IsReparsePoint(placeholderFile));
    }

    [Fact]
    public void Junctions_to_a_chain_a_dangling_target_and_a_protected_folder_are_all_removed_and_no_target_is_touched()
    {
        string mirror = Mirror;
        string prot = Dir("protected-folder");
        Write(Path.Combine(prot, "precious.txt"), "precious");
        string target = Write(P("sandbox", "keep.txt"), "keep me");
        string one = Link(P("sandbox"), Path.Combine(mirror, "one"));
        Link(one, Path.Combine(mirror, "two"));
        Link(Dir("gone-soon"), Path.Combine(mirror, "dangling"));
        Directory.Delete(P("gone-soon"));
        Link(prot, Path.Combine(mirror, "Docs"));

        int removed;
        using (Racks.Util.SafeMove.UseProtectedFoldersForTest(prot))
            removed = RackMirror.RemoveJunctions(mirror);

        Assert.Equal(4, removed);
        Assert.Empty(Directory.GetFileSystemEntries(mirror));
        Assert.Equal("keep me", File.ReadAllText(target));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(prot, "precious.txt")));
    }

    [Fact]
    public void Unicode_and_symbol_names_and_a_crowd_of_junctions_are_all_removed()
    {
        string mirror = Mirror;
        string target = Write(P("sandbox", "keep.txt"), "keep me");
        string[] names = ["café", "日本語", "emoji \U0001F4C1", "[a] 'b' 100% & c"];
        foreach (var n in names) Link(P("sandbox"), Path.Combine(mirror, n));
        for (int i = 0; i < 40; i++) Link(P("sandbox"), Path.Combine(mirror, "Rack " + i));

        int removed = RackMirror.RemoveJunctions(mirror);

        Assert.Equal(names.Length + 40, removed);
        Assert.Empty(Directory.GetFileSystemEntries(mirror));
        Assert.Equal("keep me", File.ReadAllText(target));
    }

    [SymlinkFact]
    public void A_file_symlink_in_the_mirror_is_removed_and_its_target_file_survives()
    {
        string mirror = Mirror;
        string target = Write(P("outside", "real.txt"), "real");
        File.CreateSymbolicLink(Path.Combine(mirror, "shortcut.txt"), target);

        Assert.Equal(1, RackMirror.RemoveJunctions(mirror));

        Assert.Equal("real", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Combine(mirror, "shortcut.txt")));
    }

    [Fact]
    public void A_mirror_root_that_is_a_file_or_missing_removes_nothing_and_does_not_throw()
    {
        string file = Write(P("not-a-folder"), "file");

        Assert.Equal(0, RackMirror.RemoveJunctions(file));
        Assert.Equal(0, RackMirror.RemoveJunctions(P("missing")));
        Assert.Equal("file", File.ReadAllText(file));
    }
}
