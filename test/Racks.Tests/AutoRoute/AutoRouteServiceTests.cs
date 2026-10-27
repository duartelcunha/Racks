using System.IO;
using Racks.UseCases.AutoRoute;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Shell;
using Racks.Tests.Fakes;

namespace Racks.Tests.AutoRoute;

// The use case end to end on real temp folders: the "Desktop", the racks' folders and the "workspace" are all
// directories under one temp root; the shell, the audit log and time are fakes. Never touches the real Desktop.
public sealed class AutoRouteServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-route-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop, _workspace;
    private readonly ManualScheduler _clock = new();
    private readonly RecordingAuditSink _audit = new();
    private readonly List<string> _shell = [];
    private readonly List<Instance> _racks = [];
    private readonly List<Instance> _persisted = [];
    private readonly AutoRouteService _service;

    public AutoRouteServiceTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _workspace = Path.Combine(_root, "Workspace");
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_workspace);

        var shell = new ShellOperationGateway(new ShellBackend
        {
            NotifyItemMoved = (a, b, d) => _shell.Add($"moved|{a}|{b}|{d}"),
            NotifyDirectoryChanged = p => _shell.Add($"dir|{p}"),
        });
        _service = new AutoRouteService(
            new AutoRouteHost(() => _racks.ToArray(), r => _persisted.Add(r), () => _workspace),
            _clock,
            broker: new FileOperationBroker(_audit),
            shell: shell,
            audit: _audit);
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private Instance AddRack(string name, string regex, string? folder = null, bool desktopRack = false)
    {
        if (folder == null && !desktopRack)
        {
            folder = Path.Combine(_root, "rack-" + name);
            Directory.CreateDirectory(folder);
        }
        var rack = new Instance(name, false);
        using (rack.SuspendPersistence())
        {
            rack.IsDesktopFilterRack = desktopRack;
            rack.Folder = desktopRack ? _desktop : folder!;
            rack.AutoRouteRegex = regex;
        }
        _racks.Add(rack);
        return rack;
    }

    // The setter keeps any text (a saved rule is never lost), so an unusable rule can simply be assigned.
    private static void SetRule(Instance rack, string pattern)
    {
        using (rack.SuspendPersistence()) rack.AutoRouteRegex = pattern;
    }

    private string Drop(string name, string content = "data")
    {
        string path = Path.Combine(_desktop, name);
        File.WriteAllText(path, content);
        return path;
    }

    private IEnumerable<AuditEvent> Records(string kind) => _audit.Events.Where(e => e.Kind == kind);

    // ---- matching --------------------------------------------------------------------------

    [Fact]
    public void The_first_rack_with_a_matching_rule_gets_the_file()
    {
        var first = AddRack("First", @"\.pdf$");
        var second = AddRack("Second", @".*");
        string src = Drop("invoice.pdf", "pdf bytes");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.Equal("First", d.TargetRack);
        string moved = Path.Combine(first.Folder, "invoice.pdf");
        Assert.Equal(moved, d.Destination);
        Assert.Equal("pdf bytes", File.ReadAllText(moved));
        Assert.False(File.Exists(src));
        Assert.Empty(Directory.EnumerateFiles(second.Folder));
    }

    [Fact]
    public void Matching_ignores_case_as_before()
    {
        var rack = AddRack("Pics", @"\.png$");
        var d = _service.Process(Drop("SHOT.PNG"));
        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.True(File.Exists(Path.Combine(rack.Folder, "SHOT.PNG")));
    }

    [Fact]
    public void A_file_no_rack_wants_stays_and_leaves_no_record()
    {
        AddRack("Pdfs", @"\.pdf$");
        string src = Drop("notes.txt");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.NoMatch, d.Outcome);
        Assert.True(File.Exists(src));
        Assert.Empty(_audit.Events);
        Assert.Empty(_shell);
    }

    [Fact]
    public void A_rack_with_an_empty_rule_is_ignored()
    {
        AddRack("Plain", "");
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(Drop("a.pdf")).Outcome);
    }

    [Fact]
    public void A_rack_whose_folder_is_missing_is_passed_over()
    {
        var gone = AddRack("Gone", @".*", folder: Path.Combine(_root, "does-not-exist"));
        var next = AddRack("Next", @"\.pdf$");
        string src = Drop("a.pdf");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.Equal("Next", d.TargetRack);
        Assert.False(Directory.Exists(gone.Folder));
        Assert.True(File.Exists(Path.Combine(next.Folder, "a.pdf")));
    }

    [Fact]
    public void An_invalid_rule_skips_its_rack_and_is_recorded_once_for_the_rack_not_once_per_file()
    {
        var bad = AddRack("Bad", @"\.pdf$");
        SetRule(bad, "([unclosed");
        var good = AddRack("Good", @"\.pdf$");

        for (int i = 0; i < 3; i++)
            Assert.Equal(RoutingOutcome.Routed, _service.Process(Drop($"f{i}.pdf")).Outcome);

        Assert.Equal(3, Directory.GetFiles(good.Folder).Length);
        var ruleRecord = Assert.Single(Records(AutoRouteService.AuditKind));
        Assert.Equal("rack:Bad", ruleRecord.Source);
        Assert.Equal("Skipped", ruleRecord.Status);
        Assert.Equal(3, Records("Move").Count());   // the moves are the broker's own records
        Assert.Equal("([unclosed", bad.AutoRouteRegex);   // the stored text is never touched
    }

    [Fact]
    public void A_catastrophic_rule_makes_its_rack_inactive_is_bounded_and_recorded_once()
    {
        var evil = AddRack("Evil", @"x");
        SetRule(evil, "(a+)+$");
        var good = AddRack("Good", @"!\.txt$");
        string p1 = Drop(new string('a', 45) + "!.txt"), p2 = Drop(new string('a', 46) + "!.txt");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var d1 = _service.Process(p1);
        var d2 = _service.Process(p2);
        sw.Stop();

        Assert.Equal("Good", d1.TargetRack);   // the slow rack is skipped, the next rule still works
        Assert.Equal("Good", d2.TargetRack);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "each probe and match is bounded by the regex timeout");
        var record = Assert.Single(Records(AutoRouteService.AuditKind));
        Assert.Equal("rack:Evil", record.Source);
        Assert.Contains("slow", record.Detail);
        Assert.Equal("(a+)+$", evil.AutoRouteRegex);   // the stored text is never touched
        Assert.NotNull(good);
    }

    // Passes the canary inputs but blows up on one particular name: the rack goes inactive at the first timeout.
    [Fact]
    public void A_rule_that_times_out_on_a_real_name_goes_inactive_with_one_record()
    {
        var sneaky = AddRack("Sneaky", @"x");
        SetRule(sneaky, "^(x+x+)+y$");
        string slowName = new string('x', 45) + "!";
        string p1 = Drop(slowName + ".txt");
        string p2 = Drop(new string('x', 44) + "!.txt");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(p1).Outcome);
        long afterFirst = sw.ElapsedMilliseconds;
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(p2).Outcome);   // inactive now: no second 250 ms wait
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds - afterFirst < 200, "the second file doesn't run the slow rule again");
        var record = Assert.Single(Records(AutoRouteService.AuditKind));
        Assert.Equal("rack:Sneaky", record.Source);
        Assert.True(File.Exists(p1) && File.Exists(p2));
    }

    [Fact]
    public void Editing_an_inactive_rule_activates_the_rack_again()
    {
        var rack = AddRack("R", @"x");
        SetRule(rack, "([unclosed");
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(Drop("a.pdf")).Outcome);

        SetRule(rack, @"\.pdf$");
        Assert.Equal(RoutingOutcome.Routed, _service.Process(Drop("b.pdf")).Outcome);
    }

    [Fact]
    public void Changing_a_rule_takes_effect_for_the_next_file()
    {
        var rack = AddRack("R", @"\.pdf$");
        Assert.Equal(RoutingOutcome.Routed, _service.Process(Drop("a.pdf")).Outcome);

        using (rack.SuspendPersistence()) rack.AutoRouteRegex = @"\.png$";

        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(Drop("b.pdf")).Outcome);
        Assert.Equal(RoutingOutcome.Routed, _service.Process(Drop("c.png")).Outcome);
    }

    // ---- eligibility -----------------------------------------------------------------------

    [Fact]
    public void A_matching_partial_download_is_skipped_with_one_record_and_stays()
    {
        AddRack("All", @".*");
        string src = Drop("movie.mp4.crdownload");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.Skipped, d.Outcome);
        Assert.True(File.Exists(src));
        var record = Assert.Single(_audit.Events);
        Assert.Equal(AutoRouteService.AuditKind, record.Kind);
        Assert.Equal("Skipped", record.Status);
        Assert.Equal(src, record.Source);
        Assert.Contains("crdownload", record.Detail);
    }

    [Fact]
    public void A_file_renamed_to_its_final_name_is_routed_under_that_name()
    {
        var rack = AddRack("Pdfs", @"\.pdf$");
        string partial = Drop("report.pdf.crdownload");
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(partial).Outcome);   // does not match \.pdf$ at all

        string final = Path.Combine(_desktop, "report.pdf");
        File.Move(partial, final);
        var d = _service.Process(final);

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.True(File.Exists(Path.Combine(rack.Folder, "report.pdf")));
    }

    [Theory]
    [InlineData("ghost.pdf")]
    [InlineData("ghost.pdf.crdownload")]
    public void An_entry_that_vanished_before_it_was_judged_is_skipped_without_a_record(string name)
    {
        AddRack("All", @".*");
        var d = _service.Process(Path.Combine(_desktop, name));
        Assert.Equal(RoutingOutcome.Skipped, d.Outcome);
        Assert.Contains("no longer exists", d.Reason);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public void A_file_with_a_partner_part_file_is_held_back_until_the_partner_is_gone()
    {
        var rack = AddRack("All", @"\.mp4$");
        string src = Drop("movie.mp4", "growing");
        string partner = Drop("movie.mp4.part", "...");

        Assert.Equal(RoutingOutcome.Deferred, _service.Process(src).Outcome);
        Assert.True(File.Exists(src));

        File.Delete(partner);
        _clock.Advance(AutoRouteService.RetryDelays[0]);

        Assert.True(File.Exists(Path.Combine(rack.Folder, "movie.mp4")));
    }

    [Fact]
    public void A_matching_folder_is_moved_with_its_content()
    {
        var rack = AddRack("All", @"^Project");
        string dir = Path.Combine(_desktop, "Project A");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inside.txt"), "x");

        var d = _service.Process(dir);

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.True(File.Exists(Path.Combine(rack.Folder, "Project A", "inside.txt")));
        Assert.False(Directory.Exists(dir));
    }

    // ---- moving ----------------------------------------------------------------------------

    [Fact]
    public void A_name_collision_is_skipped_and_both_files_are_untouched()
    {
        var rack = AddRack("Pdfs", @"\.pdf$");
        string existing = Path.Combine(rack.Folder, "same.pdf");
        File.WriteAllText(existing, "old");
        string src = Drop("same.pdf", "new");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.Skipped, d.Outcome);
        Assert.Equal("new", File.ReadAllText(src));
        Assert.Equal("old", File.ReadAllText(existing));
        var record = Assert.Single(_audit.Events);   // the broker's record is the one record
        Assert.Equal("Move", record.Kind);
        Assert.Equal("auto-route", record.Reason);
        Assert.Equal("Skipped", record.Status);
        Assert.Empty(_persisted);
    }

    [Fact]
    public void A_routed_file_is_one_broker_record_and_tells_the_shell()
    {
        var rack = AddRack("Pdfs", @"\.pdf$");
        string src = Drop("a.pdf");

        _service.Process(src);

        var record = Assert.Single(_audit.Events);
        Assert.Equal("Move", record.Kind);
        Assert.Equal("auto-route", record.Reason);
        Assert.Equal("Succeeded", record.Status);
        Assert.Equal(src, record.Source);
        Assert.Equal(Path.Combine(rack.Folder, "a.pdf"), record.Destination);
        Assert.Equal([$"moved|{src}|{Path.Combine(rack.Folder, "a.pdf")}|False", $"dir|{_desktop}"], _shell);
        Assert.Empty(_persisted);   // a folder rack has nothing to claim
    }

    [Fact]
    public void A_desktop_rack_receives_the_file_in_the_workspace_and_claims_it()
    {
        var rack = AddRack("Plain", @"\.pdf$", desktopRack: true);
        string src = Drop("a.pdf");

        var d = _service.Process(src);

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.Equal(Path.Combine(_workspace, "a.pdf"), d.Destination);
        Assert.True(File.Exists(Path.Combine(_workspace, "a.pdf")));
        Assert.Contains("a.pdf", rack.AssignedFiles);
        Assert.Equal([rack], _persisted);
    }

    [Fact]
    public void The_claim_moves_from_the_other_desktop_rack_and_both_are_saved()
    {
        var other = AddRack("Other", @"\.nothing$", desktopRack: true);
        other.AssignedFiles = ["a.pdf", "keep.txt"];
        var target = AddRack("Target", @"\.pdf$", desktopRack: true);

        _service.Process(Drop("a.pdf"));

        Assert.Equal(["keep.txt"], other.AssignedFiles);
        Assert.Equal(["a.pdf"], target.AssignedFiles);
        Assert.Equal([other, target], _persisted);
    }

    [Fact]
    public void A_failure_while_saving_the_rack_does_not_undo_the_move()
    {
        var rack = AddRack("Plain", @"\.pdf$", desktopRack: true);
        var service = new AutoRouteService(
            new AutoRouteHost(() => _racks.ToArray(), _ => throw new IOException("registry is busy"), () => _workspace),
            _clock, broker: new FileOperationBroker(_audit), shell: new ShellOperationGateway(new ShellBackend()), audit: _audit);

        var d = service.Process(Drop("a.pdf"));

        Assert.Equal(RoutingOutcome.Routed, d.Outcome);
        Assert.Contains("saving the rack failed", d.Reason);
        Assert.True(File.Exists(Path.Combine(_workspace, "a.pdf")));
        var failure = Assert.Single(Records(AutoRouteService.AuditKind));   // its own record, besides the broker's Move
        Assert.Equal("Failed", failure.Status);
        Assert.Contains("saving the rack failed", failure.Detail);
        Assert.Single(Records("Move"));
        Assert.NotNull(rack);
    }

    [Fact]
    public void A_desktop_rack_without_a_workspace_skips_instead_of_moving_onto_itself()
    {
        AddRack("Plain", @"\.pdf$", desktopRack: true);
        var service = new AutoRouteService(
            new AutoRouteHost(() => _racks.ToArray(), r => _persisted.Add(r), () => ""),
            _clock, broker: new FileOperationBroker(_audit), shell: new ShellOperationGateway(new ShellBackend()), audit: _audit);
        string src = Drop("a.pdf");

        var d = service.Process(src);

        Assert.Equal(RoutingOutcome.Skipped, d.Outcome);
        Assert.True(File.Exists(src));
        Assert.Single(Records(AutoRouteService.AuditKind));
    }

    [Fact]
    public void A_batch_routes_every_file_and_the_folder_check_does_not_outlive_it()
    {
        var rack = AddRack("Pdfs", @"\.pdf$");
        _service.ProcessBatch([Drop("a.pdf"), Drop("b.pdf"), Drop("c.pdf")]);
        Assert.Equal(3, Directory.GetFiles(rack.Folder).Length);

        Directory.Delete(rack.Folder, recursive: true);   // gone after the batch: the next file must notice
        Assert.Equal(RoutingOutcome.NoMatch, _service.Process(Drop("d.pdf")).Outcome);
    }

    [Fact]
    public void DestinationFor_is_the_workspace_for_a_desktop_rack_and_the_folder_otherwise()
    {
        var desktopRack = AddRack("D", "x", desktopRack: true);
        var folderRack = AddRack("F", "x");
        Assert.Equal(@"C:\ws", AutoRouteService.DestinationFor(desktopRack, @"C:\ws"));
        Assert.Equal(folderRack.Folder, AutoRouteService.DestinationFor(folderRack, @"C:\ws"));
    }

    [Fact]
    public void The_old_entry_point_still_forwards()
    {
        var rack = AddRack("D", "x", desktopRack: true);
        Racks.Util.DesktopRouter.DestinationFor(rack, @"C:\ws");
        Assert.Equal(@"C:\ws", Racks.Util.DesktopRouter.DestinationFor(rack, @"C:\ws"));
    }

    // ---- files still being written ---------------------------------------------------------

    [Fact]
    public void A_locked_file_is_retried_with_waits_then_given_up_with_one_record_and_never_lost()
    {
        AddRack("All", @"\.bin$");
        string src = Drop("big.bin", "partial");
        using var holder = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var first = _service.Process(src);
        Assert.Equal(RoutingOutcome.Deferred, first.Outcome);
        Assert.Equal(1, _clock.PendingCount);
        Assert.Empty(_audit.Events);   // not final yet

        // Advance through every retry. The total wait is the sum of RetryDelays and then nothing more is scheduled.
        for (int i = 0; i < AutoRouteService.RetryDelays.Length; i++)
        {
            Assert.Equal(1, _clock.PendingCount);
            _clock.Advance(AutoRouteService.RetryDelays[i]);
        }

        Assert.Equal(0, _clock.PendingCount);
        var record = Assert.Single(_audit.Events);
        Assert.Equal(AutoRouteService.AuditKind, record.Kind);
        Assert.Equal("Skipped", record.Status);
        Assert.Contains("still open", record.Detail);
        Assert.True(File.Exists(src));
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Single(_audit.Events);   // it gave up for good; no loop
    }

    [Fact]
    public void A_file_released_during_the_retries_is_routed()
    {
        var rack = AddRack("All", @"\.bin$");
        string src = Drop("big.bin", "whole");
        var holder = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(RoutingOutcome.Deferred, _service.Process(src).Outcome);
        _clock.Advance(AutoRouteService.RetryDelays[0]);   // still locked: schedules the second wait
        Assert.Equal(1, _clock.PendingCount);
        holder.Dispose();
        _clock.Advance(AutoRouteService.RetryDelays[1]);

        Assert.Equal(0, _clock.PendingCount);
        Assert.Equal("whole", File.ReadAllText(Path.Combine(rack.Folder, "big.bin")));
        Assert.False(File.Exists(src));
        var record = Assert.Single(_audit.Events);
        Assert.Equal("Move", record.Kind);
        Assert.Equal("Succeeded", record.Status);
    }

    [Fact]
    public void The_same_locked_file_reported_again_does_not_stack_retries()
    {
        AddRack("All", @"\.bin$");
        string src = Drop("big.bin");
        using var holder = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        _service.Process(src);
        var again = _service.Process(src);

        Assert.Equal(RoutingOutcome.Deferred, again.Outcome);
        Assert.Equal(1, _clock.PendingCount);
    }

    [Fact]
    public void Disposing_the_service_cancels_pending_retries()
    {
        AddRack("All", @"\.bin$");
        string src = Drop("big.bin");
        using var holder = new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _service.Process(src);

        _service.Dispose();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(_audit.Events);
    }
}
