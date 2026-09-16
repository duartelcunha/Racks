using Racks.Core;
using Xunit;

namespace Racks.Tests;

public sealed class FileSafetyTests : IDisposable
{
    private static string TempRoot => Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "racks-tests");
    private readonly string root = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
    private readonly AppPaths paths;
    private readonly Actions actions = new();
    private readonly FileOperations operations;
    public FileSafetyTests() { paths = new(root); paths.Initialize(); operations = new(paths, actions); }
    private string FileAt(string folder, string name = "report.txt", string content = "original content") { Directory.CreateDirectory(folder); var path = Path.Combine(folder, name); File.WriteAllText(path, content); return path; }
    private OperationRecord Move(string source, string? destination = null) => new() { Items = { new OperationItem { Source = source, Destination = destination ?? Path.Combine(paths.Workspace, Path.GetFileName(source)) } } };

    [Fact] public async Task MoveAndUndoSurviveNewCoordinator()
    {
        var source = FileAt(paths.Desktop); var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip);
        Assert.False(File.Exists(source)); Assert.Equal("original content", File.ReadAllText(record.Items[0].Destination));
        var restarted = new FileOperations(paths, actions); var undo = restarted.LastUndo(); Assert.NotNull(undo);
        await restarted.UndoAsync(undo); Assert.Equal("original content", File.ReadAllText(source)); Assert.Equal(ItemOutcome.Undone, restarted.LastUndo()!.Items[0].Outcome);
    }
    [Theory] [InlineData(RackKind.Owned)] [InlineData(RackKind.Folder)]
    public async Task DefinitionRemovalAndUndoSurviveNewCoordinator(RackKind kind)
    {
        var folder = Path.Combine(paths.Workspace, "definition-only"); Directory.CreateDirectory(folder);
        var rack = new RackDefinition { Kind = kind, Folder = folder };
        var settings = new AppSettings { Racks = { rack } };
        var record = new OperationRecord { RemovedRack = rack, SettingsPending = true };
        await operations.ExecuteAsync(record, CollisionChoice.Skip);
        var restarted = new FileOperations(paths, actions);
        var saved = restarted.LastUndo(); Assert.NotNull(saved);
        saved.ReconcileRacks(settings); Assert.Empty(settings.Racks);
        await restarted.UndoAsync(saved);
        restarted.LastUndo()!.ReconcileRacks(settings);
        Assert.Equal(rack.Id, Assert.Single(settings.Racks).Id); Assert.True(Directory.Exists(folder));
        Assert.Equal(0, actions.MoveCalls);
    }
    [Fact] public async Task RemovalKeepsFilesThatArriveDuringTheOperation()
    {
        var folder = Path.Combine(paths.Workspace, "incoming");
        var source = FileAt(folder); var rack = new RackDefinition { Folder = folder };
        var settings = new AppSettings { Racks = { rack } };
        var record = Move(source, Path.Combine(paths.Desktop, "report.txt")); record.RemovedRack = rack;
        actions.AfterMove = () => FileAt(folder, "arrived.txt");
        await operations.ExecuteAsync(record, CollisionChoice.Skip); record.ReconcileRacks(settings);
        Assert.Single(settings.Racks); Assert.True(File.Exists(Path.Combine(folder, "arrived.txt")));
    }
    [Theory] [InlineData(ItemOutcome.Pending, true)] [InlineData(ItemOutcome.Completed, true)]
    [InlineData(ItemOutcome.Failed, true)] [InlineData(ItemOutcome.UndoPending, true)]
    [InlineData(ItemOutcome.Skipped, false)] [InlineData(ItemOutcome.Undone, false)]
    public void OrganizationReconciliationKeepsUncertainRacks(ItemOutcome outcome, bool retained)
    {
        var folder = Path.Combine(paths.Workspace, "group"); Directory.CreateDirectory(folder);
        var rack = new RackDefinition { Folder = folder };
        var record = new OperationRecord { Finished = true, CreatedRacks = { rack },
            Items = { new OperationItem { Source = Path.Combine(paths.Desktop, "file.txt"), Destination = Path.Combine(folder, "file.txt"), DestinationRackId = rack.Id, Outcome = outcome } } };
        var settings = new AppSettings { Racks = { rack } };
        record.ReconcileRacks(settings); record.ReconcileRacks(settings);
        Assert.Equal(retained, settings.Racks.Any(x => x.Id == rack.Id)); Assert.True(Directory.Exists(folder));
    }
    [Fact] public void CancelledOrganizationKeepsUnexpectedFilesAndUnavailableFolders()
    {
        var folder = Path.Combine(paths.Workspace, "group"); var file = FileAt(folder);
        var rack = new RackDefinition { Folder = folder };
        var record = new OperationRecord { Finished = true, CreatedRacks = { rack } };
        var settings = new AppSettings(); record.ReconcileRacks(settings);
        Assert.Single(settings.Racks); Assert.True(File.Exists(file));
        rack.Folder = Path.Combine(paths.Workspace, "disconnected"); record.ReconcileRacks(settings); Assert.Single(settings.Racks);
    }
    [Theory] [InlineData(CollisionChoice.KeepBoth, ItemOutcome.Completed)] [InlineData(CollisionChoice.Skip, ItemOutcome.Skipped)] [InlineData(CollisionChoice.Cancel, ItemOutcome.Skipped)]
    public async Task CollisionNeverReplacesExistingFile(CollisionChoice choice, ItemOutcome expected)
    {
        var source = FileAt(paths.Desktop); var target = FileAt(paths.Workspace, content: "keep me");
        var record = await operations.ExecuteAsync(Move(source, target), choice);
        Assert.Equal("keep me", File.ReadAllText(target)); Assert.Equal(expected, record.Items[0].Outcome);
        Assert.Equal(choice != CollisionChoice.KeepBoth, File.Exists(source));
        if (choice == CollisionChoice.KeepBoth) Assert.Equal("original content", File.ReadAllText(Path.Combine(paths.Workspace, "report (2).txt")));
    }
    [Fact] public async Task FailureDoesNotPreventOtherItemsAndRemainsDurable()
    {
        var source = FileAt(paths.Desktop); var other = FileAt(paths.Desktop, "other.txt"); actions.FailSource = source;
        var record = Move(source); record.Items.Add(Move(other).Items[0]); await operations.ExecuteAsync(record, CollisionChoice.Skip);
        Assert.True(File.Exists(source)); Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome); Assert.Equal(ItemOutcome.Completed, record.Items[1].Outcome);
        Assert.True(operations.ReadRecords(out _).Single().NeedsAttention);
    }
    [Fact] public async Task ErrorAfterMoveDoesNotCauseSpeculativeRetryOrDeletion()
    {
        var source = FileAt(paths.Desktop); actions.FailAfterMove = true;
        var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip);
        Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome); Assert.True(File.Exists(record.Items[0].Destination)); Assert.Null(operations.LastUndo());
        Assert.True(operations.ReadRecords(out _).Single().NeedsAttention); Assert.Equal(1, actions.MoveCalls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DiskFullOrPermissionFailurePreservesOriginalAndDurableIntent(bool permissionDenied)
    {
        var source = FileAt(paths.Desktop);
        actions.BeforeMove = () => { if (permissionDenied) throw new UnauthorizedAccessException("Access denied"); throw new IOException("Disk full", unchecked((int)0x80070070)); };
        var result = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip);
        Assert.Equal("original content", File.ReadAllText(source));
        Assert.False(File.Exists(result.Items[0].Destination));
        var reloaded = new FileOperations(paths, actions).ReadRecords(out var errors);
        Assert.Empty(errors); Assert.True(Assert.Single(reloaded).NeedsAttention);
        Assert.Equal(ItemOutcome.Failed, reloaded[0].Items[0].Outcome);
    }
    [Fact] public async Task CancellationBetweenItemsPreservesTheRemainder()
    {
        using var cancellation = new CancellationTokenSource(); actions.AfterMove = cancellation.Cancel;
        var source = FileAt(paths.Desktop); var other = FileAt(paths.Desktop, "other.txt"); var record = Move(source); record.Items.Add(Move(other).Items[0]);
        await operations.ExecuteAsync(record, CollisionChoice.Skip, cancellation: cancellation.Token);
        Assert.Equal(ItemOutcome.Completed, record.Items[0].Outcome); Assert.Equal(ItemOutcome.Skipped, record.Items[1].Outcome); Assert.True(File.Exists(other));
    }
    [Fact] public async Task JournalFailureStopsBeforeAnyFilesystemMutation()
    {
        var source = FileAt(paths.Desktop); var record = Move(source);
        Directory.CreateDirectory(Path.Combine(paths.Operations, record.Id + ".json"));
        await Assert.ThrowsAnyAsync<IOException>(() => operations.ExecuteAsync(record, CollisionChoice.Skip));
        Assert.True(File.Exists(source)); Assert.Equal(0, actions.MoveCalls);
    }
    [Fact] public async Task JournalContainsIntentBeforePlatformMove()
    {
        var source = FileAt(paths.Desktop); var record = Move(source);
        actions.BeforeMove = () => { var saved = JsonStore.Read<OperationRecord>(Path.Combine(paths.Operations, record.Id + ".json")); Assert.Equal(ItemOutcome.Pending, saved.Items[0].Outcome); Assert.Equal(record.Items[0].Destination, saved.Items[0].Destination); };
        await operations.ExecuteAsync(record, CollisionChoice.Skip);
    }
    [Fact] public async Task UndoCollisionPreservesBothFiles()
    {
        var source = FileAt(paths.Desktop); var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip); File.WriteAllText(source, "new file");
        await operations.UndoAsync(record); Assert.Equal("new file", File.ReadAllText(source)); Assert.Equal("original content", File.ReadAllText(record.Items[0].Destination)); Assert.True(record.NeedsAttention);
    }
    [Fact] public async Task UndoRefusesChangedDestination()
    {
        var source = FileAt(paths.Desktop); var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip); File.WriteAllText(record.Items[0].Destination, "this has been edited");
        await operations.UndoAsync(record); Assert.False(File.Exists(source)); Assert.True(record.NeedsAttention);
    }
    [Fact] public async Task InterruptedUndoIsRecordedBeforeTheReverseMove()
    {
        var record = await operations.ExecuteAsync(Move(FileAt(paths.Desktop)), CollisionChoice.Skip);
        actions.BeforeMove = () => { var saved = operations.ReadRecords(out _).Single(); Assert.Equal(ItemOutcome.UndoPending, saved.Items[0].Outcome); Assert.Equal(record.Items[0].Source, saved.Items[0].UndoDestination); };
        actions.FailAfterMove = true; await operations.UndoAsync(record);
        Assert.Equal(ItemOutcome.UndoPending, record.Items[0].Outcome); Assert.True(File.Exists(record.Items[0].Source)); Assert.True(record.NeedsAttention);
        var calls = actions.MoveCalls; await operations.UndoAsync(record); Assert.Equal(calls, actions.MoveCalls);
    }
    [Fact] public async Task ShortcutUndoPreservesTargetAndReturnsShortcutForInspection()
    {
        var source = FileAt(paths.Desktop); var record = Move(source, Path.Combine(paths.Workspace, "report.lnk")); record.Kind = OperationKind.Shortcut;
        await operations.ExecuteAsync(record, CollisionChoice.Skip); Assert.True(File.Exists(source)); await operations.UndoAsync(record);
        Assert.True(File.Exists(source)); Assert.True(File.Exists(Path.Combine(paths.Desktop, "report.lnk")));
    }
    [Fact] public async Task UnavailableDestinationKeepsSource()
    {
        var source = FileAt(paths.Desktop); var record = await operations.ExecuteAsync(Move(source, Path.Combine(root, "missing", "report.txt")), CollisionChoice.Skip);
        Assert.True(File.Exists(source)); Assert.True(record.NeedsAttention);
    }
    [Fact] public async Task ProtectedRootCannotBeMoved()
    {
        var source = FileAt(paths.Desktop); var record = await operations.ExecuteAsync(Move(paths.Desktop, Path.Combine(paths.Workspace, "desktop")), CollisionChoice.Skip);
        Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome); Assert.True(File.Exists(source)); Assert.Equal(0, actions.MoveCalls);
    }
    [Fact] public async Task NormalizedRecoveryFileCannotBeMoved()
    {
        JsonStore.Write(paths.Settings, new AppSettings());
        var source = Path.Combine(Path.GetDirectoryName(paths.Settings)!, ".", Path.GetFileName(paths.Settings));
        var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip);
        Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome);
        Assert.True(File.Exists(paths.Settings)); Assert.Equal(0, actions.MoveCalls);
    }
    [Theory] [InlineData(OperationKind.Move)] [InlineData(OperationKind.Shortcut)]
    public async Task RecoveryDirectoryCannotReceiveUserFiles(OperationKind kind)
    {
        var source = FileAt(paths.Desktop);
        var destination = Path.Combine(paths.Operations, Guid.NewGuid() + ".json");
        var record = Move(source, destination); record.Kind = kind;
        await operations.ExecuteAsync(record, CollisionChoice.KeepBoth);
        Assert.True(File.Exists(source)); Assert.False(File.Exists(destination));
        Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome); Assert.Equal(0, actions.MoveCalls);
    }
    [Fact] public async Task SettingsBackupCannotBeMovedIntoARack()
    {
        JsonStore.Write(paths.Settings + ".bak", new AppSettings());
        var record = await operations.ExecuteAsync(Move(paths.Settings + ".bak"), CollisionChoice.Skip);
        Assert.True(File.Exists(paths.Settings + ".bak")); Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome);
    }
    [Fact] public async Task UpdateCacheCannotBecomeAUserFileLocation()
    {
        Directory.CreateDirectory(paths.Updates);
        var package = FileAt(paths.Updates);
        var userFile = FileAt(paths.Desktop);
        var outgoing = await operations.ExecuteAsync(Move(package), CollisionChoice.Skip);
        var incoming = await operations.ExecuteAsync(Move(userFile, Path.Combine(paths.Updates, "user.txt")), CollisionChoice.Skip);
        Assert.Equal(ItemOutcome.Failed, outgoing.Items[0].Outcome);
        Assert.Equal(ItemOutcome.Failed, incoming.Items[0].Outcome);
        Assert.True(File.Exists(package)); Assert.True(File.Exists(userFile)); Assert.Equal(0, actions.MoveCalls);
    }
    [Fact] public void ExtendedWindowsPathCannotBypassProtectedDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = FileAt(paths.Desktop);
        Assert.Throws<IOException>(() => SafeFiles.ValidateMove(@"\\?\" + paths.Desktop, Path.Combine(paths.Workspace, "desktop"), [paths.Desktop]));
        Assert.True(File.Exists(source));
    }
    [Fact] public void CaseAliasCannotBypassProtectedDirectory()
    {
        var source = FileAt(paths.Desktop);
        Assert.ThrowsAny<IOException>(() => SafeFiles.ValidateMove(paths.Desktop.ToUpperInvariant(), Path.Combine(paths.Workspace, "desktop"), [paths.Desktop]));
        Assert.True(File.Exists(source));
    }
    [Fact] public async Task LockedFileKeepsOriginalOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = FileAt(paths.Desktop); using var locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = await operations.ExecuteAsync(Move(source), CollisionChoice.Skip); Assert.Equal(ItemOutcome.Failed, record.Items[0].Outcome); Assert.True(File.Exists(source));
    }
    [Fact] public void CorruptRecordIsReportedWithoutLosingValidRecords()
    {
        File.WriteAllText(Path.Combine(paths.Operations, "broken.json"), "{bad"); operations.Persist(Move(FileAt(paths.Desktop)));
        Assert.Single(operations.ReadRecords(out var errors)); Assert.Single(errors);
    }
    [Fact] public void CorruptRecoveryDefinitionIsReportedWithoutReplayingIt()
    {
        var record = Move(FileAt(paths.Desktop)); record.SettingsPending = true;
        record.CreatedRacks.Add(new RackDefinition { Folder = "relative-path" }); operations.Persist(record);
        Assert.Empty(operations.ReadRecords(out var errors)); Assert.Single(errors);
        Assert.True(File.Exists(record.Items[0].Source));
    }
    [Fact] public void AcknowledgingRecoveryNeverChangesFiles()
    {
        var source = FileAt(paths.Desktop); var record = Move(source); operations.Persist(record); operations.ResolveInspected(record, record.Items[0]);
        Assert.True(File.Exists(source)); Assert.False(File.Exists(record.Items[0].Destination)); Assert.False(record.NeedsAttention);
    }
    [Fact] public void LegacyMoveUsesSameNoOverwriteGuarantee()
    {
        var source = FileAt(paths.Desktop); var target = FileAt(paths.Workspace, content: "keep me");
        Assert.Equal(Racks.Util.SafeMove.Result.Skipped, Racks.Util.SafeMove.TryMove(source, target, out _)); Assert.Equal("keep me", File.ReadAllText(target)); Assert.True(File.Exists(source));
    }
    [Fact] public void LegacyMoveReturnsLockedFailureWithoutDeletingSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = FileAt(paths.Desktop); using var locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(Racks.Util.SafeMove.Result.Rejected, Racks.Util.SafeMove.TryMove(source, Path.Combine(paths.Workspace, "report.txt"), out var reason)); Assert.NotEmpty(reason); Assert.True(File.Exists(source));
    }
    public void Dispose() { if (root.StartsWith(TempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true); }
    private sealed class Actions : IFileActions
    {
        public string ShortcutExtension => ".lnk"; public string? FailSource; public bool FailAfterMove; public int MoveCalls; public Action? BeforeMove; public Action? AfterMove;
        public void Move(string source, string destination) { MoveCalls++; BeforeMove?.Invoke(); if (source == FailSource) throw new IOException("Simulated disk/permission failure"); SafeFiles.MoveNoReplace(source, destination); AfterMove?.Invoke(); if (FailAfterMove) throw new IOException("Simulated response lost after rename"); }
        public void CreateShortcut(string source, string destination) { using var stream = new FileStream(destination, FileMode.CreateNew); using var writer = new StreamWriter(stream); writer.Write(source); }
    }
}
