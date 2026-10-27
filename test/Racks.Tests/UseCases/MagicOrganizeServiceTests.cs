using System.IO;
using Racks.Core;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Shell;
using Racks.Tests.Fakes;
using Racks.Tests.Persistence;
using Racks.UseCases.MagicOrganize;

namespace Racks.Tests.UseCases;

// Magic Organize against temp folders: a fake Desktop, a fake workspace, the real broker (recording audit) and a recording
// "UI" that answers the preview and the Keep/Undo question. Nothing touches the real Desktop, shell or HKCU\SOFTWARE\Racks
// (racks persist into the fixture's throwaway key). The class is in the Registry collection because it uses that fixture and
// MagicOrganizeUndo.Last is process-wide.
[Collection("Registry")]
public sealed class MagicOrganizeServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-mo-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly string _workspace;
    private readonly RackRegistryFixture _reg = new();
    private readonly RecordingAuditSink _audit = new();
    private readonly List<string> _shellCalls = new();
    private readonly FakeHost _host;
    private readonly MagicOrganizeService _service;
    private List<ClusterGroup> _clusters = new();

    public MagicOrganizeServiceTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _workspace = Path.Combine(_root, "Workspace");
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_workspace);
        MagicOrganizeUndo.Clear();

        _host = new FakeHost(_reg);
        var shell = new ShellOperationGateway(new ShellBackend
        {
            GetWorkspacePath = () => _workspace,
            InitializeWorkspace = () => { _shellCalls.Add("init"); Directory.CreateDirectory(_workspace); },
            NotifyItemMoved = (a, b, d) => _shellCalls.Add($"moved|{Path.GetFileName(a)}|{Path.GetDirectoryName(b)}"),
            ArrangeDesktopIcons = paths => _shellCalls.Add("arrange|" + string.Join(",", paths.Select(Path.GetFileName).Order())),
        });
        _service = new MagicOrganizeService(_host, new FileOperationBroker(_audit), shell, () => _desktop, _ => _clusters);
    }

    public void Dispose()
    {
        MagicOrganizeUndo.Clear();
        _reg.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private string Put(string name, string content = "x")
    {
        string path = Path.Combine(_desktop, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ClusterGroup Cluster(string name, params string[] files) => new() { Name = name, FilePaths = files.ToList() };

    private string[] DesktopNames() => Directory.GetFileSystemEntries(_desktop).Select(Path.GetFileName).Order().ToArray()!;

    private sealed class FakeHost : IMagicOrganizeHost
    {
        private readonly RackRegistryFixture _reg;
        public FakeHost(RackRegistryFixture reg) => _reg = reg;

        public IList<Instance> Racks => _reg.Controller.Instances;
        public List<string> Log { get; } = new();
        public List<(string Message, string Title)> Alerts { get; } = new();
        public List<(string Message, string Title, string Yes, string No)> Confirms { get; } = new();
        public List<List<ClusterGroup>> Previews { get; } = new();
        public MagicOrganizeUndo.Mode? Plan { get; set; } = MagicOrganizeUndo.Mode.Racks;
        public bool Keep { get; set; } = true;
        public Func<Instance, Exception?>? SaveFails { get; set; }
        public int ShowRunning, MaxShowRunning;
        public MagicOrganizeWorkArea WorkArea { get; set; } = new(0, 0, 1920, 1040, 1.0);

        public void SaveRack(Instance rack)
        {
            Log.Add("save " + rack.Name);
            var fail = SaveFails?.Invoke(rack);
            if (fail != null) throw fail;
            _reg.Controller.WriteInstanceToKey(rack);
        }

        public MagicOrganizeUndo.Mode? ChoosePlan(List<ClusterGroup> clusters)
        {
            Previews.Add(clusters);
            return Plan;
        }

        public MagicOrganizeWorkArea GetWorkArea() => WorkArea;

        public async Task ShowRackAsync(Instance rack)
        {
            Log.Add("show " + rack.Name);
            MaxShowRunning = Math.Max(MaxShowRunning, ++ShowRunning);
            await Task.Delay(5);
            ShowRunning--;
            Log.Add("shown " + rack.Name);
        }

        public void CloseRack(Instance rack)
        {
            Log.Add("close " + rack.Name);
            Racks.Remove(rack);
        }

        public void Alert(string message, string title) => Alerts.Add((message, title));

        public bool Confirm(string message, string title, string yes, string no)
        {
            Confirms.Add((message, title, yes, no));
            return Keep;
        }
    }

    // ---- nothing to do / cancelled --------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_desktop_says_so_and_stops()
    {
        _clusters = new();
        await _service.RunAsync();

        var alert = Assert.Single(_host.Alerts);
        Assert.Equal(("Your desktop is already empty or too clean to organize!", "Magic Organizer"), alert);
        Assert.Empty(_host.Previews);
        Assert.Null(MagicOrganizeUndo.Last);
    }

    [Fact]
    public async Task Cancelling_the_preview_moves_nothing_and_creates_nothing()
    {
        string a = Put("a.txt"), b = Put("b.png");
        _clusters = new() { Cluster("Docs", a), Cluster("Images", b) };
        _host.Plan = null;

        await _service.RunAsync();

        Assert.Single(_host.Previews);
        Assert.Equal(new[] { "a.txt", "b.png" }, DesktopNames());
        Assert.Empty(Directory.GetFileSystemEntries(_workspace));
        Assert.Empty(_host.Racks);
        Assert.Empty(_host.Confirms);
        Assert.Empty(_host.Alerts);
        Assert.Empty(_shellCalls);
        Assert.Null(MagicOrganizeUndo.Last);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task A_failure_anywhere_is_reported_as_a_fatal_error_not_thrown()
    {
        var service = new MagicOrganizeService(_host, new FileOperationBroker(_audit), null, () => _desktop,
            _ => throw new InvalidOperationException("ml exploded"));
        await service.RunAsync();

        var alert = Assert.Single(_host.Alerts);
        Assert.Equal("Fatal Error", alert.Title);
        Assert.StartsWith("An error occurred during Magic Organize:\nml exploded\n", alert.Message);
    }

    // ---- Racks mode -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Racks_mode_moves_each_group_into_the_workspace_and_gives_it_a_desktop_rack()
    {
        string a = Put("a.txt"), b = Put("b.txt"), c = Put("c.png");
        _clusters = new() { Cluster("Docs", a, b), Cluster("Images", c) };

        await _service.RunAsync();

        // Files moved through the broker (one audit record each), nothing left on the desktop.
        Assert.Empty(DesktopNames());
        Assert.Equal(new[] { "a.txt", "b.txt", "c.png" }, Directory.GetFiles(_workspace).Select(Path.GetFileName).Order().ToArray());
        Assert.Equal(3, _audit.Events.Count(e => e.Reason == "magic organize: into rack"));

        // Two new racks, in order, shaped as before.
        Assert.Equal(new[] { "Docs", "Images" }, _host.Racks.Select(r => r.Name).ToArray());
        var docs = _host.Racks[0];
        Assert.True(docs.IsDesktopFilterRack);
        Assert.Equal(_desktop, docs.Folder);
        Assert.Equal("Docs", docs.TitleText);
        Assert.Equal(new[] { "a.txt", "b.txt" }, docs.AssignedFiles);
        Assert.Equal(MagicOrganizeLayout.RackWidth, docs.Width);
        Assert.Equal(MagicOrganizeLayout.RackHeight, docs.Height);
        var slots = MagicOrganizeLayout.Grid(2, 0, 0, 1920, 1040, 1.0);
        Assert.Equal((slots[0].X, slots[0].Y), (docs.PosX, docs.PosY));
        Assert.Equal((slots[1].X, slots[1].Y), (_host.Racks[1].PosX, _host.Racks[1].PosY));

        // Saved to the (throwaway) registry, shown one after the other, workspace created before each rack's moves.
        Assert.True(_reg.RackKeyExists("Docs"));
        Assert.True(_reg.RackKeyExists("Images"));
        Assert.Equal(new[] { "save Docs", "show Docs", "shown Docs", "save Images", "show Images", "shown Images" }, _host.Log.ToArray());
        Assert.Equal(1, _host.MaxShowRunning);
        Assert.Equal("init", _shellCalls[0]);
        Assert.Equal(2, _shellCalls.Count(s => s == "init"));
        Assert.Equal(3, _shellCalls.Count(s => s.StartsWith("moved|")));

        // The undo record.
        var undo = MagicOrganizeUndo.Last!;
        Assert.Equal(MagicOrganizeUndo.Mode.Racks, undo.OrganizeMode);
        Assert.Equal(new[] { "Docs", "Images" }, undo.CreatedRackNames.ToArray());
        Assert.Equal(new[] { a, b, c }, undo.Moved.Select(m => m.OriginalPath).ToArray());
        Assert.Equal(new[] { Path.Combine(_workspace, "a.txt"), Path.Combine(_workspace, "b.txt"), Path.Combine(_workspace, "c.png") },
            undo.Moved.Select(m => m.NewPath).ToArray());

        // The Keep/Undo question, and Keep leaves everything in place.
        var confirm = Assert.Single(_host.Confirms);
        Assert.Equal("Moved 3 item(s) into 2 rack(s).\n\nKeep this, or undo and send everything back to your desktop?", confirm.Message);
        Assert.Equal(("Magic Organize complete", "Keep", "Undo"), (confirm.Title, confirm.Yes, confirm.No));
        Assert.Empty(_host.Alerts);
        Assert.Equal(2, _host.Racks.Count);
    }

    [Fact]
    public async Task A_second_run_never_reuses_a_rack_name_not_even_in_another_case()
    {
        var existing = new Instance("docs", false);
        using (existing.SuspendPersistence()) _host.Racks.Add(existing);
        string a = Put("a.txt"), b = Put("b.png");
        _clusters = new() { Cluster("Docs", a), Cluster("Images", b) };

        await _service.RunAsync();

        Assert.Equal(new[] { "docs", "Docs 2", "Images" }, _host.Racks.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { "Docs 2", "Images" }, MagicOrganizeUndo.Last!.CreatedRackNames.ToArray());
    }

    [Fact]
    public async Task A_moved_name_is_taken_from_any_other_desktop_rack_that_claimed_it_and_that_rack_is_saved()
    {
        var old = new Instance("Old", false);
        using (old.SuspendPersistence())
        {
            old.IsDesktopFilterRack = true;
            old.AssignedFiles = new List<string> { "a.txt", "keep.txt" };
            _host.Racks.Add(old);
        }
        string a = Put("a.txt");
        _clusters = new() { Cluster("Docs", a) };

        await _service.RunAsync();

        Assert.Equal(new[] { "keep.txt" }, old.AssignedFiles);
        Assert.Equal(new[] { "a.txt" }, _host.Racks.Single(r => r.Name == "Docs").AssignedFiles);
        // The loser is saved right after the move, before the new rack is.
        Assert.Equal(new[] { "save Old", "save Docs" }, _host.Log.Where(l => l.StartsWith("save")).ToArray());
    }

    [Fact]
    public async Task A_group_without_files_is_skipped_but_keeps_its_slot_in_the_grid()
    {
        string c = Put("c.png");
        _clusters = new() { Cluster("Empty"), Cluster("Images", c) };

        await _service.RunAsync();

        var rack = Assert.Single(_host.Racks);
        var slots = MagicOrganizeLayout.Grid(2, 0, 0, 1920, 1040, 1.0);
        Assert.Equal((slots[1].X, slots[1].Y), (rack.PosX, rack.PosY));
        Assert.Equal("Moved 1 item(s) into 2 rack(s).\n\nKeep this, or undo and send everything back to your desktop?", _host.Confirms[0].Message);
    }

    [Fact]
    public async Task The_work_area_is_converted_with_the_dpi_scale()
    {
        _host.WorkArea = new MagicOrganizeWorkArea(0, 0, 3840, 2080, 2.0);
        string c = Put("c.png");
        _clusters = new() { Cluster("Images", c) };

        await _service.RunAsync();

        var slot = MagicOrganizeLayout.Grid(1, 0, 0, 3840, 2080, 2.0)[0];
        Assert.Equal((slot.X, slot.Y), (_host.Racks[0].PosX, _host.Racks[0].PosY));
    }

    [Fact]
    public async Task A_name_that_is_not_a_plain_file_name_is_sanitised_for_the_rack_too()
    {
        string c = Put("c.png");
        _clusters = new() { Cluster(@"..\..\evil: *?", c) };

        await _service.RunAsync();

        var name = Assert.Single(_host.Racks).Name;
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('?', name);
        Assert.True(PathPolicy.ValidateLeafName(name).Ok);
    }

    [Fact]
    public async Task A_file_that_cannot_move_is_not_counted_claimed_or_recorded()
    {
        string a = Put("a.txt");
        string ghost = Path.Combine(_desktop, "ghost.txt");   // vanished between the scan and the move
        _clusters = new() { Cluster("Docs", a, ghost) };

        await _service.RunAsync();

        var docs = Assert.Single(_host.Racks);
        Assert.Equal(new[] { "a.txt" }, docs.AssignedFiles);
        Assert.Equal(new[] { a }, MagicOrganizeUndo.Last!.Moved.Select(m => m.OriginalPath).ToArray());
        Assert.StartsWith("Moved 1 item(s) into 1 rack(s)", _host.Confirms[0].Message);
    }

    [Fact]
    public async Task A_rack_that_cannot_be_saved_is_reported_and_still_shown()
    {
        string a = Put("a.txt"), c = Put("c.png");
        _clusters = new() { Cluster("Docs", a), Cluster("Images", c) };
        _host.SaveFails = rack => rack.Name == "Docs" ? new InvalidOperationException("registry says no") : null;

        await _service.RunAsync();

        var alert = Assert.Single(_host.Alerts);
        Assert.Equal(("Failed to save Rack 'Docs' to registry: registry says no", "Error"), alert);
        Assert.Contains("show Docs", _host.Log);
        Assert.Contains("show Images", _host.Log);
        Assert.Equal(new[] { "Docs", "Images" }, MagicOrganizeUndo.Last!.CreatedRackNames.ToArray());
    }

    [Fact]
    public async Task Choosing_Undo_closes_the_racks_deletes_their_keys_and_puts_every_file_back()
    {
        string a = Put("a.txt", "A"), b = Put("b.txt", "B"), c = Put("c.png", "C");
        _clusters = new() { Cluster("Docs", a, b), Cluster("Images", c) };
        _host.Keep = false;

        await _service.RunAsync();

        // Racks gone from the list, the windows closed in creation order, the registry keys deleted.
        Assert.Empty(_host.Racks);
        Assert.Equal(new[] { "close Docs", "close Images" }, _host.Log.Where(l => l.StartsWith("close")).ToArray());
        Assert.False(_reg.RackKeyExists("Docs"));
        Assert.False(_reg.RackKeyExists("Images"));

        // Files are back, with their content, and the workspace is empty.
        Assert.Equal(new[] { "a.txt", "b.txt", "c.png" }, DesktopNames());
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Empty(Directory.GetFileSystemEntries(_workspace));
        Assert.Equal(3, _audit.Events.Count(e => e.Reason == "magic organize: undo"));

        // The desktop is tidied, the record is spent, the user is told.
        Assert.Contains("arrange|a.txt,b.txt,c.png", _shellCalls);
        Assert.Null(MagicOrganizeUndo.Last);
        Assert.Equal(("Undone. 3 item(s) are back on your desktop.", "Magic Organize"), Assert.Single(_host.Alerts));
    }

    [Fact]
    public async Task Undo_does_not_overwrite_something_that_now_sits_at_the_original_spot()
    {
        string a = Put("a.txt", "mine");
        _clusters = new() { Cluster("Docs", a) };
        await _service.RunAsync();                 // Keep: the file now lives in the workspace
        Put("a.txt", "newcomer");                  // the user drops another a.txt on the desktop

        _service.Undo();

        Assert.Equal("newcomer", File.ReadAllText(a));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_workspace, "a.txt")));   // the moved file is not lost
        Assert.Empty(_host.Racks);                 // the rack itself is still torn down
    }

    [Fact]
    public void Undo_with_nothing_recorded_does_nothing()
    {
        MagicOrganizeUndo.Clear();
        _service.Undo();
        Assert.Empty(_host.Alerts);
        Assert.Empty(_host.Log);

        MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Racks);   // started but empty
        _service.Undo();
        Assert.Empty(_host.Alerts);
    }

    // ---- Folders mode -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Folders_mode_moves_each_group_into_a_folder_on_the_desktop()
    {
        string a = Put("a.txt"), c = Put("c.png");
        _clusters = new() { Cluster("Docs", a), Cluster("Images", c) };
        _host.Plan = MagicOrganizeUndo.Mode.Folders;

        await _service.RunAsync();

        Assert.Equal(new[] { "Docs", "Images" }, DesktopNames());
        Assert.True(File.Exists(Path.Combine(_desktop, "Docs", "a.txt")));
        Assert.True(File.Exists(Path.Combine(_desktop, "Images", "c.png")));
        Assert.Empty(_host.Racks);
        Assert.Empty(_host.Log);                     // no rack windows, no registry saves
        Assert.Empty(Directory.GetFileSystemEntries(_workspace));
        Assert.Equal(2, _audit.Events.Count(e => e.Reason == "magic organize: new folder"));
        Assert.Equal(2, _audit.Events.Count(e => e.Reason == "magic organize: into folder"));

        var undo = MagicOrganizeUndo.Last!;
        Assert.Equal(MagicOrganizeUndo.Mode.Folders, undo.OrganizeMode);
        Assert.Equal(new[] { Path.Combine(_desktop, "Docs"), Path.Combine(_desktop, "Images") }, undo.CreatedFolders.ToArray());
        Assert.Equal("Moved 2 item(s) into 2 folder(s) on your desktop.\n\nKeep this, or undo and send everything back?", Assert.Single(_host.Confirms).Message);
    }

    [Fact]
    public async Task Folder_names_come_from_the_sanitiser_so_nothing_is_created_outside_the_desktop()
    {
        string a = Put("a.txt"), c = Put("c.png"), d = Put("d.doc");
        _clusters = new() { Cluster(@"..\..\escape", a), Cluster("a/b\\c", c), Cluster("", d) };
        _host.Plan = MagicOrganizeUndo.Mode.Folders;

        await _service.RunAsync();

        // Every entry that exists next to or above the desktop is the desktop and the workspace only.
        Assert.Equal(new[] { "Desktop", "Workspace" }, Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).Order().ToArray());
        var folders = Directory.GetDirectories(_desktop);
        Assert.Equal(3, folders.Length);
        Assert.All(folders, f => Assert.Equal(_desktop, Path.GetDirectoryName(f)));
        Assert.Equal(3, Directory.GetFiles(_desktop, "*", SearchOption.AllDirectories).Length);
        Assert.Matches("^Cluster_[0-9a-f]{4}$", Path.GetFileName(folders.Single(f => Path.GetFileName(f).StartsWith("Cluster_"))));
    }

    [Fact]
    public async Task A_folder_that_already_exists_is_reused_and_is_not_removed_by_undo()
    {
        Directory.CreateDirectory(Path.Combine(_desktop, "Docs"));
        string a = Put("a.txt");
        _clusters = new() { Cluster("Docs", a) };
        _host.Plan = MagicOrganizeUndo.Mode.Folders;
        _host.Keep = false;

        await _service.RunAsync();

        Assert.DoesNotContain(_audit.Events, e => e.Reason == "magic organize: new folder");
        Assert.True(File.Exists(Path.Combine(_desktop, "a.txt")));        // undone: back on the desktop
        Assert.True(Directory.Exists(Path.Combine(_desktop, "Docs")));    // it was not ours to remove
        Assert.Null(MagicOrganizeUndo.Last);
    }

    [Fact]
    public async Task Choosing_Undo_in_folders_mode_restores_the_files_and_removes_only_the_empty_folders_it_made()
    {
        string a = Put("a.txt", "A"), c = Put("c.png", "C");
        _clusters = new() { Cluster("Docs", a), Cluster("Images", c) };
        _host.Plan = MagicOrganizeUndo.Mode.Folders;
        _host.Keep = false;

        await _service.RunAsync();

        Assert.Equal(new[] { "a.txt", "c.png" }, DesktopNames());
        Assert.Equal("C", File.ReadAllText(c));
        Assert.Contains("arrange|a.txt,c.png", _shellCalls);
        Assert.Equal(("Undone. 2 item(s) are back on your desktop.", "Magic Organize"), Assert.Single(_host.Alerts));
        Assert.Null(MagicOrganizeUndo.Last);
    }

    [Fact]
    public async Task The_undo_question_in_both_modes_is_asked_exactly_once_with_the_Keep_Undo_buttons()
    {
        string a = Put("a.txt");
        _clusters = new() { Cluster("Docs", a) };
        _host.Plan = MagicOrganizeUndo.Mode.Folders;

        await _service.RunAsync();

        var confirm = Assert.Single(_host.Confirms);
        Assert.Equal(("Magic Organize complete", "Keep", "Undo"), (confirm.Title, confirm.Yes, confirm.No));
    }
}
