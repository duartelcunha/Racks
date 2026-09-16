namespace Racks.Core;

public interface IFileActions
{
    void Move(string source, string destination);
    void CreateShortcut(string source, string destination);
    string ShortcutExtension { get; }
}

public sealed class FileOperations(AppPaths paths, IFileActions actions)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsBusy => gate.CurrentCount == 0;
    public event Action? Changed;
    private string RecordPath(Guid id) => Path.Combine(paths.Operations, id + ".json");
    public void Persist(OperationRecord record) => JsonStore.Write(RecordPath(record.Id), record, backup: false);
    private bool IsApplicationFile(string path)
    {
        if (SafeFiles.IsWithin(path, paths.Operations) || SafeFiles.IsWithin(path, paths.Updates) || SafeFiles.IsWithin(path, AppContext.BaseDirectory)) return true;
        var canonical = SafeFiles.CanonicalPath(path);
        return new[] { paths.Settings, paths.LastUndo, paths.Startup }.Any(reserved =>
        {
            var location = SafeFiles.CanonicalPath(reserved);
            return canonical.Equals(location, SafeFiles.PathComparison) || canonical.StartsWith(location + ".", SafeFiles.PathComparison);
        });
    }

    public IReadOnlyList<OperationRecord> ReadRecords(out List<string> errors)
    {
        errors = new();
        var records = new List<OperationRecord>();
        foreach (var path in Directory.EnumerateFiles(paths.Operations, "*.json"))
        {
            try
            {
                var record = JsonStore.Read<OperationRecord>(path);
                ValidateRecord(record);
                if (!Path.GetFileNameWithoutExtension(path).Equals(record.Id.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Operation identifier does not match its record file.");
                records.Add(record);
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        return records.OrderByDescending(x => x.StartedUtc).ToList();
    }

    public OperationRecord? LastUndo()
    {
        if (!File.Exists(paths.LastUndo)) return null;
        var id = JsonStore.Read<Guid>(paths.LastUndo);
        if (!File.Exists(RecordPath(id))) return null;
        var record = JsonStore.Read<OperationRecord>(RecordPath(id)); ValidateRecord(record); return record;
    }

    private static void ValidateRecord(OperationRecord record)
    {
        if (record.Version != 1 || record.Id == Guid.Empty || record.Items is null || record.CreatedRacks is null || !Enum.IsDefined(record.Kind) || record.Label is null ||
            record.Items.Any(item => item == null || item.Error is null || !Enum.IsDefined(item.Outcome) || !Path.IsPathFullyQualified(item.Source) || !Path.IsPathFullyQualified(item.Destination)))
            throw new InvalidDataException("Invalid or unsupported operation record. Inspect the JSON file in the settings folder.");
        var definitions = record.CreatedRacks.ToList();
        if (record.RemovedRack != null) definitions.Add(record.RemovedRack);
        SettingsStore.Validate(new AppSettings { Racks = definitions });
        if (record.Items.Any(item => item.UndoDestination != null && !Path.IsPathFullyQualified(item.UndoDestination)))
            throw new InvalidDataException("Invalid undo destination in operation record.");
    }

    public void PruneCompletedRecords()
    {
        if (IsBusy) return;
        var last = LastUndo()?.Id;
        foreach (var record in ReadRecords(out _))
            if (record.Id != last && !record.NeedsAttention)
                File.Delete(RecordPath(record.Id)); // Only this app's completed metadata, never user files.
    }

    public async Task<OperationRecord> ExecuteAsync(OperationRecord operation, CollisionChoice collision,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        Changed?.Invoke();
        try
        {
            return await Task.Run(() =>
            {
                // The full intent is durable before any file can change.
                Persist(operation);
                for (var index = 0; index < operation.Items.Count; index++)
                {
                    var item = operation.Items[index];
                    if (cancellation.IsCancellationRequested)
                    {
                        foreach (var remaining in operation.Items.Skip(index)) { remaining.Outcome = ItemOutcome.Skipped; remaining.Error = "Cancelled before this item started."; }
                        break;
                    }
                    progress?.Report($"{index + 1} of {operation.Items.Count} · {Path.GetFileName(item.Source)}");
                    try
                    {
                        if (!Path.IsPathFullyQualified(item.Source) || !Path.IsPathFullyQualified(item.Destination)) throw new IOException("Invalid file location.");
                        if (IsApplicationFile(item.Source) || IsApplicationFile(item.Destination))
                            throw new IOException("Application files and recovery locations cannot be used for rack transfers.");
                        if (SafeFiles.Exists(item.Destination))
                        {
                            if (collision == CollisionChoice.Cancel)
                            {
                                foreach (var remaining in operation.Items.Skip(index)) { remaining.Outcome = ItemOutcome.Skipped; remaining.Error = "Cancelled at a name collision."; }
                                break;
                            }
                            if (collision == CollisionChoice.Skip) { item.Outcome = ItemOutcome.Skipped; item.Error = "Destination already exists."; Persist(operation); continue; }
                            item.Destination = SafeFiles.UniqueDestination(Path.GetDirectoryName(item.Destination)!, Path.GetFileName(item.Destination));
                        }
                        if (operation.Kind == OperationKind.Shortcut)
                        {
                            if (!SafeFiles.Exists(item.Source)) throw new FileNotFoundException("The source is unavailable.");
                            SafeFiles.RejectLinkAncestors(Path.GetDirectoryName(item.Destination)!);
                        }
                        else SafeFiles.ValidateMove(item.Source, item.Destination, new[] { paths.Data, paths.Desktop, paths.Workspace, AppContext.BaseDirectory });
                        // Destination may change on a collision; persist the final intent too.
                        Persist(operation);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        item.Outcome = ItemOutcome.Failed;
                        item.Error = ex.Message;
                        Persist(operation);
                        continue;
                    }
                    try
                    {
                        if (operation.Kind == OperationKind.Shortcut) actions.CreateShortcut(item.Source, item.Destination);
                        else actions.Move(item.Source, item.Destination);
                    }
                    catch (Exception ex)
                    {
                        // Even a platform API can fail after making a change. Retain the intent;
                        // recovery must inspect both paths instead of retrying automatically.
                        item.Outcome = ItemOutcome.Failed;
                        item.Error = ex.Message;
                        Persist(operation);
                        continue;
                    }
                    item.Outcome = ItemOutcome.Completed;
                    item.IsDirectory = Directory.Exists(item.Destination);
                    item.LastWriteUtc = File.GetLastWriteTimeUtc(item.Destination);
                    item.Length = item.IsDirectory ? 0 : new FileInfo(item.Destination).Length;
                    // A journal write failure propagates and stops the batch. Never label the
                    // filesystem operation a failure or retry it just because persistence failed.
                    Persist(operation);
                }
                operation.Finished = true;
                Persist(operation);
                if (operation.Completed > 0 || operation.RemovedRack != null && operation.Items.Count == 0)
                    JsonStore.Write(paths.LastUndo, operation.Id);
                return operation;
            }, CancellationToken.None);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public async Task<OperationRecord> UndoAsync(OperationRecord operation, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        Changed?.Invoke();
        try
        {
            return await Task.Run(() =>
            {
                if (cancellation.IsCancellationRequested) return operation;
                operation.UndoRequested = true;
                Persist(operation);
                foreach (var item in operation.Items.AsEnumerable().Reverse())
                {
                    if (cancellation.IsCancellationRequested) break;
                    if (item.Outcome != ItemOutcome.Completed) continue;
                    try
                    {
                        if (IsApplicationFile(item.Source) || IsApplicationFile(item.Destination))
                            throw new IOException("Undo cannot change application files or recovery locations.");
                        if (!SafeFiles.Exists(item.Destination)) throw new IOException("The item is no longer at its recorded destination. Reveal its locations before resolving it.");
                        SafeFiles.RejectLinkAncestors(item.Destination);
                        if (!item.IsDirectory && (new FileInfo(item.Destination).Length != item.Length || File.GetLastWriteTimeUtc(item.Destination) != item.LastWriteUtc))
                            throw new IOException("The item changed after this operation. Move it manually in Explorer/Finder.");
                        if (operation.Kind == OperationKind.Shortcut)
                        {
                            // Do not delete even a shortcut speculatively. Return the generated
                            // shortcut to Desktop, where the user can inspect or delete it.
                            var target = SafeFiles.UniqueDestination(paths.Desktop, Path.GetFileName(item.Destination));
                            SafeFiles.ValidateMove(item.Destination, target, new[] { paths.Data, paths.Workspace, paths.Desktop });
                            item.UndoDestination = target;
                        }
                        else
                        {
                            SafeFiles.ValidateMove(item.Destination, item.Source, new[] { paths.Data, paths.Workspace, paths.Desktop });
                            item.UndoDestination = item.Source;
                        }
                    }
                    catch (Exception ex) { item.Error = ex.Message; Persist(operation); continue; }
                    item.Outcome = ItemOutcome.UndoPending;
                    Persist(operation);
                    try { actions.Move(item.Destination, item.UndoDestination!); }
                    catch (Exception ex) { item.Error = ex.Message; Persist(operation); continue; }
                    item.Outcome = ItemOutcome.Undone;
                    item.Error = "";
                    Persist(operation);
                }
                return operation;
            }, CancellationToken.None);
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }

    public void ResolveInspected(OperationRecord record, OperationItem item)
    {
        if (IsBusy) throw new InvalidOperationException("Wait for the active operation.");
        if (item.Outcome is ItemOutcome.Pending or ItemOutcome.Failed or ItemOutcome.UndoPending || item.Outcome == ItemOutcome.Completed && item.Error.Length > 0)
        {
            // Explicit acknowledgement only removes the recovery warning, never data.
            item.Outcome = ItemOutcome.Skipped;
            item.Error = "Locations inspected and acknowledged by the user.";
            record.Finished = record.Items.All(x => x.Outcome != ItemOutcome.Pending);
            Persist(record);
        }
    }
}
