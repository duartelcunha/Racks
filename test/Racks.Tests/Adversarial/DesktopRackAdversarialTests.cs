using System.IO;
using Racks.Rack;
using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Adversarial;

// P11: the desktop-rack bookkeeping (claims by file name, and giving a file back to the Desktop) against
// collisions, hostile names and hard links, on real NTFS. "Desktop" and "workspace" are temp folders.
[Trait("Category", "Adversarial")]
public sealed class DesktopRackClaimsAdversarialTests : AdversarialTree
{
    private static Instance Rack(string name, bool desktop = true, params string[] files)
        => new(name, false) { IsDesktopFilterRack = desktop, AssignedFiles = files.ToList() };

    // ---- Claim -----------------------------------------------------------------------------

    [Fact]
    public void Four_racks_claiming_one_name_in_turn_leave_exactly_one_owner_each_time()
    {
        var racks = new[] { Rack("A", true, "x.txt"), Rack("B", true, "x.txt"), Rack("C", true, "x.txt"), Rack("D") };

        for (int i = 0; i < racks.Length; i++)
        {
            var changed = DesktopRackClaims.Claim(racks, racks[i], "x.txt");

            Assert.Equal(1, racks.Count(r => r.AssignedFiles.Contains("x.txt")));
            Assert.Contains("x.txt", racks[i].AssignedFiles);
            Assert.DoesNotContain(racks[i], changed);
        }
        Assert.Equal(new[] { "x.txt" }, racks[3].AssignedFiles);
    }

    [Fact]
    public void Claims_for_different_names_do_not_disturb_each_other_across_many_racks()
    {
        var racks = new[] { Rack("A", true, "a.txt", "shared.txt"), Rack("B", true, "b.txt"), Rack("C", true, "c.txt", "shared.txt"), Rack("D", true, "d.txt") };

        var changed = DesktopRackClaims.Claim(racks, racks[3], "shared.txt");

        Assert.Equal(new[] { "a.txt" }, racks[0].AssignedFiles);
        Assert.Equal(new[] { "b.txt" }, racks[1].AssignedFiles);
        Assert.Equal(new[] { "c.txt" }, racks[2].AssignedFiles);
        Assert.Equal(new[] { "d.txt", "shared.txt" }, racks[3].AssignedFiles);
        Assert.Equal(new[] { racks[0], racks[2] }, changed);
    }

    [Fact]
    public void A_claim_matches_the_name_exactly_so_names_that_differ_only_in_case_are_two_claims()
    {
        // Matching everywhere (the rack's file list, the drag-out handler, the claim) is ordinal against the
        // spelling the folder reports, so "Photo.JPG" and "photo.jpg" are different claims. NTFS would call them
        // one file; they can only both exist as names through a case change. Pinned as today's behavior.
        var a = Rack("A", true, "photo.jpg");
        var b = Rack("B");

        var changed = DesktopRackClaims.Claim(new[] { a, b }, b, "Photo.JPG");

        Assert.Empty(changed);
        Assert.Equal(new[] { "photo.jpg" }, a.AssignedFiles);
        Assert.Equal(new[] { "Photo.JPG" }, b.AssignedFiles);
    }

    [Fact]
    public void A_rack_that_is_no_longer_in_the_list_keeps_its_claims_and_is_not_reported()
    {
        // A removed rack is dropped from the controller's list before anything else can claim: the claim only
        // walks the list it is given.
        var removed = Rack("Removed", true, "x.txt");
        var target = Rack("T");
        var remaining = new[] { target, Rack("Other", true, "y.txt") };

        var changed = DesktopRackClaims.Claim(remaining, target, "x.txt");

        Assert.Empty(changed);
        Assert.Equal(new[] { "x.txt" }, removed.AssignedFiles);
        Assert.Equal(new[] { "x.txt" }, target.AssignedFiles);
    }

    [Fact]
    public void A_rack_with_no_claim_list_and_an_empty_claim_list_are_both_skipped_without_error()
    {
        var noList = new Instance("NoList", false) { IsDesktopFilterRack = true, AssignedFiles = null! };
        var empty = Rack("Empty");
        var target = Rack("T");

        var changed = DesktopRackClaims.Claim(new[] { noList, empty, target }, target, "x.txt");

        Assert.Empty(changed);
        Assert.Null(noList.AssignedFiles);
        Assert.Empty(empty.AssignedFiles);
    }

    [Fact]
    public void A_target_that_is_not_in_the_list_still_gets_the_claim_and_everyone_else_loses_it()
    {
        var a = Rack("A", true, "x.txt");
        var stranger = Rack("Stranger");

        var changed = DesktopRackClaims.Claim(new[] { a }, stranger, "x.txt");

        Assert.Equal(new[] { a }, changed);
        Assert.Contains("x.txt", stranger.AssignedFiles);
        Assert.Empty(a.AssignedFiles);
    }

    [Theory]
    [InlineData("[brackets] 'quotes' 100% & more.txt")]
    [InlineData("emoji \U0001F4C1.txt")]
    [InlineData("caf\u00e9 \u65e5\u672c\u8a9e.txt")]
    public void Odd_but_legal_names_can_be_claimed_and_taken_over(string name)
    {
        var a = Rack("A", true, name);
        var b = Rack("B");

        Assert.Equal(new[] { a }, DesktopRackClaims.Claim(new[] { a, b }, b, name));
        Assert.Equal(new[] { name }, b.AssignedFiles);
    }

    // ---- IsGone ----------------------------------------------------------------------------

    [Fact]
    public void A_name_that_disappears_is_gone_but_only_while_its_folder_is_still_reachable()
    {
        string file = Write(P("ws", "Holiday.jpg"));
        Assert.False(DesktopRackClaims.IsGone(file));

        File.Delete(file);
        Assert.True(DesktopRackClaims.IsGone(file));

        SafeDelete.DeleteDirectoryRecursive(P("ws"));
        Assert.False(DesktopRackClaims.IsGone(file), "the folder is gone too: unreachable is not 'moved away'");
    }

    [Fact]
    public void Known_gap_a_folder_that_is_a_dangling_junction_counts_as_reachable_so_its_names_are_gone()
    {
        // FINDING P11-F5 (low): Directory.Exists is true for a junction whose target is missing, so IsGone's
        // "the folder is still reachable" test passes and every name under it reads as "moved away". It needs the
        // folder of a desktop rack (the workspace) to be a junction whose target disappeared, and a drag-out in
        // that state; the effect is that the claims of the dragged items are dropped. Pinned for the day the
        // reachability check also looks at the folder's contents or link target.
        Directory.CreateDirectory(P("real"));
        Write(P("real", "x.txt"));
        string link = Link(P("real"), P("ws"));
        string through = Path.Combine(link, "x.txt");
        Assert.False(DesktopRackClaims.IsGone(through));

        SafeDelete.DeleteDirectoryRecursive(P("real"));

        Assert.True(DesktopRackClaims.IsGone(through));
    }

    [Fact]
    public void A_parent_that_is_a_file_or_a_path_with_an_illegal_shape_is_never_gone()
    {
        string notAFolder = Write(P("a-file.txt"));

        Assert.False(DesktopRackClaims.IsGone(Path.Combine(notAFolder, "inside.txt")));
        Assert.False(DesktopRackClaims.IsGone("\0bad\0"));
        Assert.False(DesktopRackClaims.IsGone(@"C:"));
        Assert.False(DesktopRackClaims.IsGone(@"\\no-such-host-racks-test\share\x.txt"));
    }

    [Fact]
    public void A_folder_a_link_and_a_file_that_exist_are_never_gone_whatever_their_names()
    {
        string folder = Dir("ws", "caf\u00e9 [1] 'x' & 100%");
        string file = Write(P("ws", "emoji \U0001F4C1.txt"));
        string link = Link(folder, P("ws", "pointer"));
        string dangling = Link(Dir("temp-target"), P("ws", "dangling"));
        Directory.Delete(P("temp-target"));

        Assert.False(DesktopRackClaims.IsGone(folder));
        Assert.False(DesktopRackClaims.IsGone(file));
        Assert.False(DesktopRackClaims.IsGone(link));
        // Even a dangling junction still "exists" as a name: .NET reports a junction whose target is gone as a folder.
        Assert.False(DesktopRackClaims.IsGone(dangling));
    }
}

[Trait("Category", "Adversarial")]
public sealed class WorkspaceReturnAdversarialTests : AdversarialTree
{
    private readonly string Workspace;
    private readonly string Desktop;

    public WorkspaceReturnAdversarialTests()
    {
        Workspace = Dir("workspace");
        Desktop = Dir("desktop");
    }

    private string? Return(string name) => WorkspaceReturn.ToDesktop(Workspace, Desktop, name);

    // ---- same name, same size, different data ----------------------------------------------

    [Fact]
    public void A_file_with_the_same_name_and_the_same_length_but_other_content_is_not_treated_as_the_same_file()
    {
        string onDesktop = Write(Path.Combine(Desktop, "report.txt"), "AAAA");
        string mine = Write(Path.Combine(Workspace, "report.txt"), "BBBB");

        var back = Return("report.txt");

        Assert.Equal(Path.Combine(Desktop, "report (from Racks).txt"), back);
        Assert.Equal("AAAA", File.ReadAllText(onDesktop));
        Assert.Equal("BBBB", File.ReadAllText(back!));
        Assert.False(File.Exists(mine));
    }

    [Fact]
    public void A_hard_linked_file_with_another_length_comes_back_renamed_and_its_other_name_survives()
    {
        Write(Path.Combine(Desktop, "x.txt"), "short");
        string otherName = Write(P("elsewhere", "data.txt"), "a much longer content");
        Assert.True(HardlinkHelper.TryCreate(otherName, Path.Combine(Workspace, "x.txt")), "test setup: hard link");

        var back = Return("x.txt");

        Assert.Equal(Path.Combine(Desktop, "x (from Racks).txt"), back);
        Assert.Equal("a much longer content", File.ReadAllText(back!));
        Assert.Equal("a much longer content", File.ReadAllText(otherName));
        Assert.Equal("short", File.ReadAllText(Path.Combine(Desktop, "x.txt")));
    }

    [Fact]
    public void Known_gap_a_hard_linked_file_with_the_same_length_but_other_data_is_taken_for_the_Desktop_file()
    {
        // FINDING P11-F4 (low): the "Ctrl+drop duplicate" test is link count > 1 AND equal length, not file
        // identity. A workspace file that has another hard link elsewhere and happens to have the same length as
        // an unrelated Desktop file of the same name is therefore "dropped" instead of returned. No byte is lost
        // (its data is still reachable under the other hard-link name, and the Desktop file is untouched), but the
        // user's file does not come back to the Desktop. The fix is the file-identity check (volume serial +
        // file index) already listed as a follow-up; pinned so it is deliberate when it lands.
        string desktopFile = Write(Path.Combine(Desktop, "x.txt"), "AAAA");
        string otherName = Write(P("elsewhere", "data.txt"), "BBBB");
        string inWorkspace = Path.Combine(Workspace, "x.txt");
        Assert.True(HardlinkHelper.TryCreate(otherName, inWorkspace), "test setup: hard link");

        var back = Return("x.txt");

        Assert.Equal(desktopFile, back);
        Assert.False(File.Exists(inWorkspace));
        Assert.Equal("AAAA", File.ReadAllText(desktopFile));
        Assert.Equal("BBBB", File.ReadAllText(otherName));
        Assert.Equal(1, HardlinkHelper.GetLinkCount(otherName));
    }

    // ---- collisions ------------------------------------------------------------------------

    [Fact]
    public void Every_free_candidate_name_up_to_the_1000th_is_used_and_nothing_is_overwritten()
    {
        Write(Path.Combine(Desktop, "n.txt"), "original");
        Write(Path.Combine(Desktop, "n (from Racks).txt"), "c1");
        for (int i = 2; i <= 999; i++) Write(Path.Combine(Desktop, $"n (from Racks {i}).txt"), "c" + i);
        Write(Path.Combine(Workspace, "n.txt"), "mine");

        var back = Return("n.txt");

        Assert.Equal(Path.Combine(Desktop, "n (from Racks 1000).txt"), back);
        Assert.Equal("mine", File.ReadAllText(back!));
        Assert.Equal("original", File.ReadAllText(Path.Combine(Desktop, "n.txt")));
        Assert.Equal("c999", File.ReadAllText(Path.Combine(Desktop, "n (from Racks 999).txt")));
    }

    [Fact]
    public void When_all_1000_candidate_names_are_taken_it_gives_up_and_leaves_the_file_in_the_workspace()
    {
        Write(Path.Combine(Desktop, "n.txt"), "original");
        Write(Path.Combine(Desktop, "n (from Racks).txt"), "c1");
        for (int i = 2; i <= 1000; i++) Write(Path.Combine(Desktop, $"n (from Racks {i}).txt"), "c" + i);
        string mine = Write(Path.Combine(Workspace, "n.txt"), "mine");
        int before = Directory.GetFiles(Desktop).Length;

        var back = Return("n.txt");

        Assert.Null(back);
        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.Equal(before, Directory.GetFiles(Desktop).Length);
    }

    [Fact]
    public void A_folder_collision_skips_a_taken_candidate_that_is_a_file()
    {
        Directory.CreateDirectory(Path.Combine(Workspace, "Projects"));
        File.WriteAllText(Path.Combine(Workspace, "Projects", "plan.md"), "plan");
        Directory.CreateDirectory(Path.Combine(Desktop, "Projects"));
        File.WriteAllText(Path.Combine(Desktop, "Projects (from Racks)"), "a file in the way");

        var back = Return("Projects");

        Assert.Equal(Path.Combine(Desktop, "Projects (from Racks 2)"), back);
        Assert.Equal("plan", File.ReadAllText(Path.Combine(back!, "plan.md")));
        Assert.Equal("a file in the way", File.ReadAllText(Path.Combine(Desktop, "Projects (from Racks)")));
    }

    [Fact]
    public void A_name_with_symbols_returns_under_a_candidate_name_that_keeps_its_extension()
    {
        string name = "[a] 'b' 100% & c \U0001F4C1.tar.gz";
        Write(Path.Combine(Desktop, name), "desktop");
        Write(Path.Combine(Workspace, name), "workspace");

        var back = Return(name);

        Assert.Equal(Path.Combine(Desktop, "[a] 'b' 100% & c \U0001F4C1.tar (from Racks).gz"), back);
        Assert.Equal("workspace", File.ReadAllText(back!));
    }

    // ---- hostile names ---------------------------------------------------------------------

    [Theory]
    [InlineData("a/b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("../evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("\\\\server\\share\\evil.txt")]
    [InlineData("evil.txt:stream")]
    [InlineData("")]
    public void A_name_that_is_a_path_returns_nothing_and_moves_nothing(string name)
    {
        // The target exists at the place the path would reach, so a regression would move it.
        Write(P("evil.txt"), "outside");
        Write(Path.Combine(Workspace, "b.txt"), "inside");
        Write(Path.Combine(Workspace, "evil.txt"), "inside evil");
        var before = Snapshot(Root);

        Assert.Null(Return(name));

        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public void The_dot_dot_name_does_not_move_the_workspace_parent()
    {
        // Path.GetFileName("..") is ".." and Path.Combine(workspace, "..") exists, so the caller's "is it a plain
        // name" check lets it through: only the broker (which refuses ".." segments) stands in the way.
        Write(Path.Combine(Workspace, "keep.txt"), "keep");
        Write(P("sibling", "other.txt"), "other");
        var before = Snapshot(Root);

        Assert.Null(Return(".."));

        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public void Known_gap_the_single_dot_name_moves_the_whole_workspace_folder_to_the_Desktop()
    {
        // FINDING P11-F3 (medium, needs a hostile or hand-edited AssignedFiles value): the caller's check is
        // Path.GetFileName(name) == name, which "." passes, and neither the registry loader nor the layout import
        // validates AssignedFiles items as names. Path.Combine(workspace, ".") is the workspace itself, the
        // Desktop already has a "." (the Desktop), so the candidate ". (from Racks)" is free and the broker moves
        // the WHOLE WORKSPACE (every desktop rack's files) onto the Desktop under that name. Nothing is lost and
        // it is reversible by hand, but every desktop rack then shows its files as missing. Fix, not done here:
        // WorkspaceReturn (and the call site) should require PathPolicy.ValidateLeafName(name).Ok. Pinned so the
        // fix is deliberate: it must flip to "Null and the workspace is untouched".
        Write(Path.Combine(Workspace, "keep.txt"), "keep");

        var back = Return(".");

        Assert.Equal(Path.Combine(Desktop, ". (from Racks)"), back);
        Assert.False(Directory.Exists(Workspace));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(back!, "keep.txt")));
    }

    [Fact]
    public void A_name_longer_than_255_characters_returns_nothing_without_throwing()
    {
        string name = new string('n', 300) + ".txt";
        Write(Path.Combine(Workspace, "ok.txt"), "ok");

        string? back = "unset";
        var ex = Record.Exception(() => back = Return(name));

        Assert.Null(ex);
        Assert.Null(back);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(Workspace, "ok.txt")));
    }

    // ---- state the file can be in ----------------------------------------------------------

    [Fact]
    public void A_file_that_is_open_stays_in_the_workspace_and_the_answer_is_null()
    {
        string locked = Write(Path.Combine(Workspace, "open.pdf"), "pdf");

        string? back;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            back = Return("open.pdf");

        Assert.Null(back);
        Assert.Equal("pdf", File.ReadAllText(locked));
        Assert.False(File.Exists(Path.Combine(Desktop, "open.pdf")));

        // Once it is closed the same call works.
        Assert.Equal(Path.Combine(Desktop, "open.pdf"), Return("open.pdf"));
    }

    [Fact]
    public void A_folder_that_holds_a_junction_comes_back_as_one_piece_and_the_junction_target_is_untouched()
    {
        string target = Write(P("outside", "keep.txt"), "outside");
        string folder = Path.Combine(Workspace, "Mixed");
        Write(Path.Combine(folder, "own.txt"), "own");
        Link(P("outside"), Path.Combine(folder, "pointer"));

        var back = Return("Mixed");

        Assert.Equal(Path.Combine(Desktop, "Mixed"), back);
        Assert.Equal("own", File.ReadAllText(Path.Combine(back!, "own.txt")));
        Assert.True(JunctionHelper.IsLink(Path.Combine(back!, "pointer")));
        Assert.Equal("outside", File.ReadAllText(target));
    }

    [Fact]
    public void A_workspace_link_to_a_protected_folder_is_not_moved_and_the_protected_folder_is_untouched()
    {
        // The broker judges a link by its target (a follow-up in the roadmap), so a Ctrl+dropped reference to a
        // protected folder cannot be returned. It stays in the workspace instead of dragging the real folder along.
        string prot = Dir("protected-folder");
        Write(Path.Combine(prot, "precious.txt"), "precious");
        string link = Link(prot, Path.Combine(Workspace, "Docs"));

        string? back;
        using (SafeMove.UseProtectedFoldersForTest(prot))
            back = Return("Docs");

        Assert.Null(back);
        Assert.True(Directory.Exists(link));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(prot, "precious.txt")));
        Assert.False(Directory.Exists(Path.Combine(Desktop, "Docs")));
    }

    [Fact]
    public void A_missing_workspace_or_a_missing_desktop_returns_nothing_and_never_creates_either()
    {
        Assert.Null(WorkspaceReturn.ToDesktop(P("no-workspace"), Desktop, "a.txt"));

        Write(Path.Combine(Workspace, "a.txt"), "a");
        Assert.Null(WorkspaceReturn.ToDesktop(Workspace, P("no-desktop"), "a.txt"));

        Assert.False(Directory.Exists(P("no-workspace")));
        Assert.False(Directory.Exists(P("no-desktop")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Workspace, "a.txt")));
    }
}
