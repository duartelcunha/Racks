using System.Diagnostics;
using System.IO;
using Racks.Core;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Registry;
using Racks.Infrastructure.Shell;
using Racks.Rack;

namespace Racks.UseCases.MagicOrganize;

/// <summary>The primary monitor's work area in physical pixels plus the DPI scale (1.5 at 150%).</summary>
public readonly record struct MagicOrganizeWorkArea(double Left, double Top, double Width, double Height, double DpiScale);

/// <summary>
/// Everything Magic Organize needs from the app that is a window, a dialog or the live list of racks, so the service itself
/// has no WPF window types. <c>Racks.Views.MagicOrganizeHost</c> is the real one.
/// </summary>
public interface IMagicOrganizeHost
{
    /// <summary>The live list of racks (the controller's own list: a new rack is added to it).</summary>
    IList<Instance> Racks { get; }

    /// <summary>Saves one rack to the registry (may throw; the service reports it and carries on).</summary>
    void SaveRack(Instance rack);

    /// <summary>Shows the preview dialog. Null when the user cancelled.</summary>
    MagicOrganizeUndo.Mode? ChoosePlan(List<ClusterGroup> clusters);

    MagicOrganizeWorkArea GetWorkArea();

    /// <summary>
    /// Creates the rack's window, registers it with the controller, waits for the staggered entry animation (100 ms) and
    /// shows it on top. The service awaits it between two racks.
    /// </summary>
    Task ShowRackAsync(Instance rack);

    /// <summary>Closes the rack's window (if it has one) and removes the rack from the controller.</summary>
    void CloseRack(Instance rack);

    void Alert(string message, string title);

    /// <summary>A two-button question; true when the user picked <paramref name="yes"/>.</summary>
    bool Confirm(string message, string title, string yes, string no);
}

/// <summary>
/// "Magic Organize" from the tray menu: look at the Desktop, group the entries (<see cref="AutoOrganizer"/>), show a preview,
/// then either turn each group into a desktop rack (Racks mode: the files move into the Racks workspace and the rack claims
/// them) or into a real folder on the Desktop (Folders mode). Every move goes through <see cref="FileOperationBroker"/> and is
/// recorded in <see cref="MagicOrganizeUndo"/>, so one click can put everything back (<see cref="Undo"/>). Moved out of
/// <c>MainWindow</c> with the same sequence, the same messages and the same awaits.
///
/// Threading: runs on the UI thread (an <c>async void</c> menu handler awaits <see cref="RunAsync"/>); the only await is
/// <see cref="IMagicOrganizeHost.ShowRackAsync"/> between racks.
/// </summary>
public sealed class MagicOrganizeService
{
    private readonly IMagicOrganizeHost _host;
    private readonly FileOperationBroker _broker;
    private readonly ShellOperationGateway _shell;
    private readonly Func<string> _desktopPath;
    private readonly Func<string, List<ClusterGroup>> _analyze;
    private readonly IRackSettingsStore? _store;

    public MagicOrganizeService(
        IMagicOrganizeHost host,
        FileOperationBroker? broker = null,
        ShellOperationGateway? shell = null,
        Func<string>? desktopPath = null,
        Func<string, List<ClusterGroup>>? analyze = null,
        IRackSettingsStore? store = null)
    {
        _host = host;
        _broker = broker ?? FileOperationBroker.Default;
        _shell = shell ?? ShellOperationGateway.Default;
        _desktopPath = desktopPath ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        _analyze = analyze ?? AutoOrganizer.AnalyzeDesktop;
        _store = store;
    }

    // Looked up at use, so a test that replaces Instance.Store is honored.
    private IRackSettingsStore Store => _store ?? Instance.Store;

    /// <summary>The whole flow, including its error dialog. Never throws.</summary>
    public async Task RunAsync()
    {
        try
        {
            string desktopPath = _desktopPath();
            var clusters = _analyze(desktopPath);

            if (clusters.Count == 0)
            {
                _host.Alert("Your desktop is already empty or too clean to organize!", "Magic Organizer");
                return;
            }

            var choice = _host.ChoosePlan(clusters);
            if (choice == MagicOrganizeUndo.Mode.Racks)
            {
                await OrganizeIntoRacksAsync(clusters);
            }
            else if (choice == MagicOrganizeUndo.Mode.Folders)
            {
                OrganizeIntoFolders(clusters, desktopPath);
            }
        }
        catch (Exception ex)
        {
            _host.Alert($"An error occurred during Magic Organize:\n{ex.Message}\n{ex.StackTrace}", "Fatal Error");
        }
    }

    private async Task OrganizeIntoRacksAsync(List<ClusterGroup> clusters)
    {
        var undo = MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Racks);
        // User approved, create racks and move files!
        var area = _host.GetWorkArea();
        // Screen sizes are physical pixels; PosX/PosY/Width/Height are WPF units.
        var slots = MagicOrganizeLayout.Grid(clusters.Count, area.Left, area.Top, area.Width, area.Height, area.DpiScale);
        double rackWidth = MagicOrganizeLayout.RackWidth;
        double rackHeight = MagicOrganizeLayout.RackHeight;

        int movedFilesCount = 0;

        for (int i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            if (cluster.FilePaths.Count == 0) continue;

            string safeName = PathPolicy.SanitizeLeafName(cluster.Name, "Cluster_" + Guid.NewGuid().ToString().Substring(0, 4));

            // The name is the registry key: a second run must not reuse (and overwrite) the first run's racks.
            safeName = RackNames.Unique(safeName, _host.Racks.Select(r => r.Name));
            var inst = new Instance(safeName, false);
            inst.TitleText = safeName;
            inst.Folder = _desktopPath();
            inst.IsDesktopFilterRack = true;
            inst.PosX = slots[i].X;
            inst.PosY = slots[i].Y;
            inst.Width = rackWidth;
            inst.Height = rackHeight;

            inst.AssignedFiles = new List<string>();

            _host.Racks.Add(inst);

            // Ensure workspace exists before moving
            _shell.InitializeWorkspace();

            // 2. Move files to RacksWorkspace and assign them
            foreach (var fp in cluster.FilePaths)
            {
                string fileName = Path.GetFileName(fp);
                string destPath = Path.Combine(_shell.WorkspacePath, fileName);

                var moveResult = _broker.Move(fp, destPath, "magic organize: into rack");
                if (moveResult.Completed)
                {
                    // (Directory.Exists(fp) is asked after the move, so it is false for a folder too; unchanged from before.)
                    _shell.NotifyItemMoved(fp, destPath, Directory.Exists(fp));
                    // One rack owns the name: take it from any other rack that still claims it.
                    foreach (var lost in DesktopRackClaims.Claim(_host.Racks, inst, fileName))
                        _host.SaveRack(lost);
                    undo.RecordMove(fp, destPath); // fp = original desktop path
                    movedFilesCount++;
                }
            }
            undo.CreatedRackNames.Add(inst.Name);

            try
            {
                _host.SaveRack(inst);
            }
            catch (Exception ex)
            {
                _host.Alert($"Failed to save Rack '{safeName}' to registry: {ex.Message}", "Error");
            }

            await _host.ShowRackAsync(inst);
        }

        bool keep = _host.Confirm(
            $"Moved {movedFilesCount} item(s) into {clusters.Count} rack(s).\n\nKeep this, or undo and send everything back to your desktop?",
            "Magic Organize complete", "Keep", "Undo");
        if (!keep) Undo();
    }

    private void OrganizeIntoFolders(List<ClusterGroup> clusters, string desktopPath)
    {
        var undo = MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Folders);
        // Just move them to physical folders on the Desktop
        int movedFilesCount = 0;
        foreach (var cluster in clusters)
        {
            if (cluster.FilePaths.Count == 0) continue;

            // The cluster name comes from file names: make it a plain folder name before it
            // reaches Path.Combine (a name with a slash or ".." would create folders elsewhere).
            string folderName = PathPolicy.SanitizeLeafName(cluster.Name, "Cluster_" + Guid.NewGuid().ToString().Substring(0, 4));
            string folderPath = Path.Combine(desktopPath, folderName);
            if (!Directory.Exists(folderPath))
            {
                var created = _broker.CreateDirectory(folderPath, "magic organize: new folder");
                if (!created.Succeeded)
                {
                    Debug.WriteLine($"Magic Organize folder {created.Status}: {created.Reason}");
                    continue;
                }
                undo.CreatedFolders.Add(folderPath);
            }

            foreach (var fp in cluster.FilePaths)
            {
                string fileName = Path.GetFileName(fp);
                string destPath = Path.Combine(folderPath, fileName);

                var moveResult = _broker.Move(fp, destPath, "magic organize: into folder");
                if (moveResult.Completed)
                {
                    _shell.NotifyItemMoved(fp, destPath, Directory.Exists(fp));
                    undo.RecordMove(fp, destPath);
                    movedFilesCount++;
                }
            }
        }
        bool keep = _host.Confirm(
            $"Moved {movedFilesCount} item(s) into {clusters.Count} folder(s) on your desktop.\n\nKeep this, or undo and send everything back?",
            "Magic Organize complete", "Keep", "Undo");
        if (!keep) Undo();
    }

    /// <summary>
    /// Undoes the last Magic Organize: tears down the racks/folders it created and moves every file back to its original
    /// desktop spot, then tidies the desktop into a grid. Nothing to undo = nothing happens. Never throws.
    /// </summary>
    public void Undo()
    {
        var undo = MagicOrganizeUndo.Last;
        if (undo == null || !undo.HasAnything) return;
        try
        {
            if (undo.OrganizeMode == MagicOrganizeUndo.Mode.Racks)
            {
                // Close and delete the racks this run created (before moving files back, so
                // their file-watchers/claims are gone).
                foreach (var name in undo.CreatedRackNames)
                {
                    var inst = _host.Racks.FirstOrDefault(i => i.Name == name);
                    if (inst == null) continue;
                    inst.isWindowClosing = true;
                    Store.Delete(inst.Name);
                    _host.CloseRack(inst);
                }
            }

            int restored = undo.RestoreFiles(_broker, _shell);
            undo.RemoveCreatedFolders(_broker);

            // Lay the returned files out in a clean grid on the desktop.
            var back = new List<string>();
            foreach (var m in undo.Moved)
                if (File.Exists(m.OriginalPath) || Directory.Exists(m.OriginalPath))
                    back.Add(m.OriginalPath);
            if (back.Count > 0) _shell.ArrangeDesktopIcons(back);

            MagicOrganizeUndo.Clear();
            _host.Alert($"Undone. {restored} item(s) are back on your desktop.", "Magic Organize");
        }
        catch (Exception ex)
        {
            _host.Alert($"Couldn't fully undo:\n{ex.Message}", "Magic Organize");
        }
    }
}
