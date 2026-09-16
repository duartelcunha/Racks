using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Racks.Core;
using Racks.Desktop.Platform;

namespace Racks.Desktop;

public sealed class Session : INotifyPropertyChanged, IDisposable
{
    public AppPaths Paths { get; }
    public AppSettings Settings { get; private set; }
    public SettingsStore Store { get; }
    public PlatformServices Platform { get; } = new();
    public FileOperations Operations { get; }
    public StartupRecovery Startup { get; }
    public bool SafeMode { get; private set; }
    public bool ReadOnly { get; private set; }
    public IReadOnlyList<CatalogItem> Items { get; private set; } = Array.Empty<CatalogItem>();
    public ObservableCollection<string> Problems { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RacksChanged;
    public event Action? SettingsChanged;
    private string status = "Ready";
    public string Status { get => status; set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public CancellationTokenSource? CurrentOperation { get; private set; }
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? catalogCancellation;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private bool disposed;
    public long RefreshCount { get; private set; }
    public long SaveCount { get; private set; }
    public RoutingService Router { get; }
    public UpdateService Updates { get; }

    public Session(string? profile, bool safe)
    {
        Paths = new(profile); Paths.Initialize(); Store = new(Paths); Startup = new(Paths);
        SafeMode = safe || Startup.Begin();
        try { Settings = Store.Load(); }
        catch (Exception ex) { Settings = new(); ReadOnly = true; Problems.Add("Settings could not be loaded. Restore the last-good backup in Recovery. " + ex.Message); }
        Operations = new(Paths, Platform);
        if (!ReadOnly)
        {
            if (LegacyImporter.Import(Paths, Settings)) Problems.Add("Your existing racks were imported. Their files stayed in their original folders. Routing rules were imported paused.");
            Store.Save(Settings);
        }
        var records = Operations.ReadRecords(out var errors);
        if (!ReadOnly)
        {
            foreach (var record in records.Where(x => x.SettingsPending).OrderBy(x => x.StartedUtc))
            {
                // Reconcile definitions and name filters from confirmed results only.
                // This never moves or deletes a file and leaves ambiguous items unresolved.
                foreach (var created in record.CreatedRacks)
                    if (!Settings.Racks.Any(x => x.Id == created.Id)) Settings.Racks.Add(created);
                if (record.RemovedRack is { } removed && record.Items.Any(x => x.Outcome == ItemOutcome.Undone) && !Settings.Racks.Any(x => x.Id == removed.Id)) Settings.Racks.Add(removed);
                ApplyMembership(record, false); ApplyMembership(record, true); Store.Save(Settings);
                record.SettingsPending = false; Operations.Persist(record);
            }
        }
        foreach (var record in records)
            if (record.NeedsAttention) Problems.Add($"Interrupted operation: {record.Label}. Open Recovery to inspect it.");
        foreach (var error in errors) Problems.Add(error);
        if (SafeMode) Problems.Add("Safe mode is active. Routing, physics, and desktop attachment are paused for this session.");
        refreshTimer.Tick += async (_, _) => { refreshTimer.Stop(); await RefreshAsync(); };
        Router = new(this);
        Updates = new(this);
    }

    public void RequireWritable()
    {
        if (ReadOnly) throw new InvalidOperationException("Restore settings before changing racks or moving files.");
        if (Updates?.Installing == true) throw new InvalidOperationException("Racks is restarting to install an update.");
    }

    public void Save()
    {
        SaveCount++;
        RequireWritable(); Store.Save(Settings); SettingsChanged?.Invoke();
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync(); RebuildWatchers(); Router.Rebuild();
        if (!Paths.IsIsolated) Updates.Start();
    }

    public async Task RefreshAsync()
    {
        await refreshGate.WaitAsync();
        try { await RefreshCoreAsync(); }
        finally { refreshGate.Release(); }
    }

    private async Task RefreshCoreAsync()
    {
        RefreshCount++;
        if (disposed) return;
        catalogCancellation?.Cancel(); catalogCancellation?.Dispose();
        catalogCancellation = new(); var token = catalogCancellation.Token;
        var racks = Settings.Racks.ToArray();
        try
        {
            var result = await Task.Run(() => { var items = FileCatalog.Read(racks, out var issues, token); return (items, issues); }, token);
            if (token.IsCancellationRequested || disposed) return;
            // Publish one snapshot, keeping equal item instances stable. Thousands of
            // CollectionChanged notifications otherwise stall layout and reset selection.
            var previous = Items.ToDictionary(x => (x.RackId, x.Path));
            var next = result.items.Select(x => previous.TryGetValue((x.RackId, x.Path), out var old) && old == x ? old : x).ToArray();
            var changed = !Items.SequenceEqual(next);
            if (changed) Items = next;
            if (result.issues.Count > 0) Status = string.Join(" · ", result.issues);
            else if (!Operations.IsBusy) Status = $"{Items.Count} items · {Settings.Racks.Count} racks";
            if (changed) PropertyChanged?.Invoke(this, new(nameof(Items)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "Refresh failed: " + ex.Message; }
    }

    public RackDefinition CreateRack(string title, RackKind kind, string? folder = null)
    {
        RequireWritable();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new IOException("Enter a rack name between 1 and 200 characters.");
        var rack = new RackDefinition { Title = title.Trim(), Kind = kind, X = 80 + Settings.Racks.Count % 4 * 40, Y = 80 + Settings.Racks.Count % 4 * 40 };
        rack.Folder = kind == RackKind.Owned ? Path.Combine(Paths.Workspace, rack.Id.ToString("N")) : Path.GetFullPath(folder ?? "");
        if (kind == RackKind.Folder && !Directory.Exists(rack.Folder)) throw new IOException("Select an available folder.");
        if (kind == RackKind.Owned) Directory.CreateDirectory(rack.Folder);
        Settings.Racks.Add(rack);
        try { Save(); } catch { Settings.Racks.Remove(rack); throw; }
        RebuildWatchers(); RacksChanged?.Invoke(); return rack;
    }

    public async Task<OperationRecord> MoveIntoAsync(RackDefinition rack, IEnumerable<string> sources, bool shortcut, CollisionChoice collision)
    {
        RequireWritable();
        var operation = new OperationRecord { Label = shortcut ? "Create shortcuts" : "Move into " + rack.Title, Kind = shortcut ? OperationKind.Shortcut : OperationKind.Move };
        foreach (var source in sources.Distinct())
        {
            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
            if (shortcut) name += Platform.ShortcutExtension;
            operation.Items.Add(new() { Source = source, Destination = Path.Combine(rack.Folder, name), DestinationRackId = rack.Id,
                SourceRackId = Items.FirstOrDefault(x => x.Path.Equals(source, SafeFiles.PathComparison))?.RackId });
        }
        return await RunOperationAsync(operation, collision);
    }

    public async Task<OperationRecord> ReturnAsync(IEnumerable<CatalogItem> items, CollisionChoice collision)
    {
        var operation = new OperationRecord { Label = "Return to Desktop" };
        foreach (var item in items.DistinctBy(x => x.Path)) operation.Items.Add(new() { Source = item.Path, Destination = Path.Combine(Paths.Desktop, item.Name), SourceRackId = item.RackId });
        return await RunOperationAsync(operation, collision);
    }

    public async Task RenameAsync(CatalogItem item, string newName)
    {
        if (!SafeFiles.IsLeafName(newName)) throw new IOException("Enter a plain filename without path separators.");
        await RunOperationAsync(new OperationRecord { Label = "Rename " + item.Name, Kind = OperationKind.Rename,
            Items = { new OperationItem { Source = item.Path, Destination = Path.Combine(Path.GetDirectoryName(item.Path)!, newName), SourceRackId = item.RackId, DestinationRackId = item.RackId } } }, CollisionChoice.Skip);
    }

    public async Task<bool> RemoveRackAsync(RackDefinition rack, CollisionChoice collision)
    {
        RequireWritable();
        if (rack.Kind == RackKind.Owned)
        {
            if (!Directory.Exists(rack.Folder)) throw new IOException("The rack folder is unavailable. Reconnect it before returning its files.");
            var sources = Directory.EnumerateFileSystemEntries(rack.Folder)
                .Where(x => rack.IncludedNames == null || rack.IncludedNames.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)).ToList();
            var operation = new OperationRecord { Label = "Remove " + rack.Title, RemovedRack = rack };
            foreach (var source in sources) operation.Items.Add(new() { Source = source, Destination = Path.Combine(Paths.Desktop, Path.GetFileName(source)), SourceRackId = rack.Id });
            var result = await RunOperationAsync(operation, collision);
            if (result.Items.Any(x => x.Outcome != ItemOutcome.Completed)) { Status = "The rack was kept because some files could not be returned. Open Recovery."; return false; }
        }
        Settings.Racks.Remove(rack);
        try { Save(); } catch { Settings.Racks.Add(rack); throw; }
        RebuildWatchers(); Router.Rebuild(); RacksChanged?.Invoke(); await RefreshAsync(); return true;
    }

    public async Task<OperationRecord> RunOperationAsync(OperationRecord operation, CollisionChoice collision)
    {
        RequireWritable();
        if (CurrentOperation != null) throw new InvalidOperationException("Wait for the active operation, or cancel it first.");
        CurrentOperation = new();
        PropertyChanged?.Invoke(this, new(nameof(CurrentOperation)));
        try
        {
            operation.SettingsPending = true;
            var result = await Operations.ExecuteAsync(operation, collision, new Progress<string>(x => Status = x), CurrentOperation.Token);
            ApplyMembership(result, false); Save(); result.SettingsPending = false; Operations.Persist(result); Operations.PruneCompletedRecords(); await RefreshAsync();
            Status = $"{result.Completed} completed · {result.Items.Count(x => x.Outcome == ItemOutcome.Skipped)} skipped · {result.Items.Count(x => x.Outcome == ItemOutcome.Failed)} need attention";
            if (result.NeedsAttention) Problems.Add(Status + " — " + result.Label);
            return result;
        }
        finally { CurrentOperation.Dispose(); CurrentOperation = null; PropertyChanged?.Invoke(this, new(nameof(CurrentOperation))); }
    }

    public async Task OrganizeAsync(IEnumerable<OrganizeGroup> groups, CollisionChoice collision)
    {
        RequireWritable();
        if (CurrentOperation != null) throw new InvalidOperationException("Finish the active operation first.");
        var operation = new OperationRecord { Label = "Organize by file type" };
        foreach (var group in groups.Where(x => x.Paths.Count > 0))
        {
            if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 200) throw new IOException("Give each group a name between 1 and 200 characters.");
            var rack = new RackDefinition { Title = group.Name.Trim(), Kind = RackKind.Owned, X = 80 + operation.CreatedRacks.Count * 50, Y = 100 + operation.CreatedRacks.Count * 30 };
            rack.Folder = Path.Combine(Paths.Workspace, rack.Id.ToString("N"));
            operation.CreatedRacks.Add(rack);
            foreach (var path in group.Paths) operation.Items.Add(new OperationItem { Source = path, Destination = Path.Combine(rack.Folder, Path.GetFileName(path)), DestinationRackId = rack.Id });
        }
        if (operation.Items.Count == 0) return;
        Operations.Persist(operation);
        foreach (var rack in operation.CreatedRacks) Directory.CreateDirectory(rack.Folder);
        Settings.Racks.AddRange(operation.CreatedRacks); Save(); RacksChanged?.Invoke(); RebuildWatchers();
        await RunOperationAsync(operation, collision);
    }

    private void ApplyMembership(OperationRecord operation, bool undo)
    {
        foreach (var item in operation.Items.Where(x => x.Outcome == (undo ? ItemOutcome.Undone : ItemOutcome.Completed)))
        {
            var fromId = undo ? item.DestinationRackId : item.SourceRackId;
            var toId = undo ? item.SourceRackId : item.DestinationRackId;
            if (operation.Kind != OperationKind.Shortcut || undo)
                Settings.Racks.FirstOrDefault(x => x.Id == fromId)?.IncludedNames?.Remove(Path.GetFileName(undo ? item.Destination : item.Source));
            if (operation.Kind == OperationKind.Shortcut && undo) continue;
            var names = Settings.Racks.FirstOrDefault(x => x.Id == toId)?.IncludedNames;
            var name = Path.GetFileName(undo ? item.Source : item.Destination);
            if (names != null && !names.Contains(name)) names.Add(name);
        }
    }

    public async Task UndoAsync()
    {
        RequireWritable();
        if (CurrentOperation != null) throw new InvalidOperationException("Wait for the active operation.");
        var operation = Operations.LastUndo() ?? throw new IOException("There is no completed operation to undo.");
        CurrentOperation = new();
        PropertyChanged?.Invoke(this, new(nameof(CurrentOperation)));
        try
        {
            operation.SettingsPending = true; Operations.Persist(operation);
            var result = await Operations.UndoAsync(operation, CurrentOperation.Token);
            if (operation.RemovedRack is { } rack && !Settings.Racks.Any(x => x.Id == rack.Id) && result.Items.Any(x => x.Outcome == ItemOutcome.Undone)) Settings.Racks.Add(rack);
            ApplyMembership(result, true); Save(); RacksChanged?.Invoke(); RebuildWatchers(); await RefreshAsync();
            foreach (var created in result.CreatedRacks)
                if (Directory.Exists(created.Folder) && !Directory.EnumerateFileSystemEntries(created.Folder).Any() && !result.Items.Any(x => x.DestinationRackId == created.Id && x.Outcome is ItemOutcome.Completed or ItemOutcome.UndoPending or ItemOutcome.Failed or ItemOutcome.Pending))
                    Settings.Racks.RemoveAll(x => x.Id == created.Id);
            Save(); result.SettingsPending = false; Operations.Persist(result); RacksChanged?.Invoke(); RebuildWatchers();
            Status = result.NeedsAttention || result.Items.Any(x => x.Outcome == ItemOutcome.Completed) ? "Undo needs attention. Inspect the recorded locations in Recovery." : "Operation undone.";
        }
        finally { CurrentOperation.Dispose(); CurrentOperation = null; PropertyChanged?.Invoke(this, new(nameof(CurrentOperation))); }
    }

    public async Task RestoreBackupAsync()
    {
        if (Operations.IsBusy) throw new InvalidOperationException("Wait for file operations to finish.");
        Settings = Store.RestoreBackup(); ReadOnly = false; SafeMode = true;
        RebuildWatchers(); Router.Rebuild(); RacksChanged?.Invoke(); SettingsChanged?.Invoke(); await RefreshAsync();
        Status = "Settings restored. Safe mode stays active until the next launch.";
    }

    public async Task ReloadSettingsAsync()
    {
        if (CurrentOperation != null) throw new InvalidOperationException("Finish the active operation first.");
        Settings = Store.Load(); RebuildWatchers(); Router.Rebuild(); RacksChanged?.Invoke(); SettingsChanged?.Invoke(); await RefreshAsync();
    }

    public void RebuildWatchers()
    {
        foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
        foreach (var folder in Settings.Racks.Select(x => x.Folder).Distinct().Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                void Refresh(object? s, FileSystemEventArgs e) => ScheduleRefresh();
                watcher.Created += Refresh; watcher.Deleted += Refresh; watcher.Changed += Refresh; watcher.Renamed += (_, _) => ScheduleRefresh();
                watcher.Error += (_, _) => ScheduleRefresh(); watcher.EnableRaisingEvents = true; watchers.Add(watcher);
            }
            catch (Exception ex) { Status = "Live refresh unavailable: " + ex.Message; }
        }
    }

    private void ScheduleRefresh() => Dispatcher.UIThread.Post(() => { if (disposed) return; refreshTimer.Stop(); refreshTimer.Start(); });
    public void Dispose()
    {
        disposed = true; Router.Dispose(); Updates.Dispose(); refreshTimer.Stop(); catalogCancellation?.Cancel();
        foreach (var watcher in watchers) watcher.Dispose();
    }
}
