using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;
using Racks.UseCases.Layout;

namespace Racks.Tests.Layouts;

// INV-IMPORT-2: snapshot, delete, write, verify, and a restore that is checked and never swallowed. The registry is an
// in-memory fake that keeps every value kind and fails on command; nothing here touches the real registry.
public sealed class LayoutImportExecutorTests
{
    private readonly ScriptedRackRegistry _registry = new();
    private readonly RecordingAuditSink _audit = new();

    private LayoutImportExecutor Executor => new(_registry, _audit);
    private LayoutService Service => new(_registry, new NoStore(), _audit);

    private sealed class NoStore : IRackSettingsStore
    {
        public RackLoadResult LoadAll() => new([], []);
        public RackSettings ReadGlobalStyleDefaults() => new();
        public bool Save(Instance rack) => throw new NotSupportedException();
        public bool Normalize(Instance rack) => throw new NotSupportedException();
        public bool SaveField(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool SaveGlobalDefault(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool Delete(string name) => throw new NotSupportedException();
        public int ResetGlobalStyleDefaults() => throw new NotSupportedException();
    }

    private static string Layout(params (string Name, object Values)[] racks) => JsonSerializer.Serialize(new
    {
        Version = 1,
        Racks = racks.Select(r => new { r.Name, r.Values }).ToArray(),
    });

    private static IReadOnlyList<ValidatedRack> Validated(string json)
    {
        var parsed = LayoutParser.Parse(json);
        Assert.True(parsed.Ok, parsed.Error);
        var result = LayoutValidator.Validate(parsed.Document!);
        Assert.True(result.Ok, result.Error);
        return result.Racks!;
    }

    // Three existing racks with every awkward kind: DWORD, QWORD, text, expandable text, binary, REG_MULTI_SZ, an unnamed value.
    private void SeedExisting()
    {
        _registry.Seed("Alpha", "Name", "Alpha", RegistryValueKind.String);
        _registry.Seed("Alpha", "PosX", "10.5", RegistryValueKind.String);
        _registry.Seed("Alpha", "IconSize", 48, RegistryValueKind.DWord);
        _registry.Seed("Alpha", "Width", 321L, RegistryValueKind.QWord);
        _registry.Seed("Alpha", "Folder", @"%USERPROFILE%\Docs", RegistryValueKind.ExpandString);
        _registry.Seed("Alpha", "Blob", new byte[] { 1, 2, 3, 0, 255 }, RegistryValueKind.Binary);
        _registry.Seed("Alpha", "LastAccessedFiles", new[] { "a.txt", "b.txt" }, RegistryValueKind.MultiString);
        _registry.Seed("Alpha", "", "the default value", RegistryValueKind.String);
        _registry.Seed("Beta", "Name", "Beta", RegistryValueKind.String);
        _registry.Seed("Beta", "LastAccessedFiles", Array.Empty<string>(), RegistryValueKind.MultiString);
        _registry.SeedKey("Empty");   // a rack key with no values at all
        _registry.Seed("Unknown Legacy", "Whatever", "keep me", RegistryValueKind.String);
    }

    private string TwoNewRacks() => Layout(
        ("Gamma", new { Name = "Gamma", PosX = 5.25, IconSize = 64, IsLocked = true, Folder = @"C:\Users\me\Docs" }),
        ("Delta", new { Folder = @"D:\x" }));

    // ---- success -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_mode_leaves_exactly_the_imported_racks_with_the_expected_typed_values()
    {
        SeedExisting();
        var racks = Validated(TwoNewRacks());

        var result = Executor.Execute(racks, replaceExisting: true, source: "file.json");

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(2, result.ImportedCount);
        Assert.False(result.StateChanged);
        Assert.Equal(new[] { "Delta", "Gamma" }, _registry.KeyNames);   // every old rack, empty ones and unknown ones, is gone

        Assert.Equal("5.25", _registry.Get("Gamma", "PosX")!.Value.Data);
        Assert.Equal(RegistryValueKind.String, _registry.Get("Gamma", "PosX")!.Value.Kind);
        Assert.Equal(64, _registry.Get("Gamma", "IconSize")!.Value.Data);
        Assert.Equal(RegistryValueKind.DWord, _registry.Get("Gamma", "IconSize")!.Value.Kind);
        Assert.Equal("True", _registry.Get("Gamma", "IsLocked")!.Value.Data);
        Assert.Equal(@"C:\Users\me\Docs", _registry.Get("Gamma", "Folder")!.Value.Data);
        Assert.Equal(@"D:\x", _registry.Get("Delta", "Folder")!.Value.Data);
        Assert.Null(_registry.Get("Delta", "Name"));   // only what the file carried is written

        var record = Assert.Single(_audit.Events);
        Assert.Equal(LayoutAudit.ImportKind, record.Kind);
        Assert.Equal("Succeeded", record.Status);
        Assert.Equal("file.json", record.Source);
        Assert.Contains("mode=replace", record.Detail);
        Assert.Contains("racks=2", record.Detail);
    }

    [Fact]
    public void Merge_mode_changes_only_the_values_in_the_file_and_leaves_every_other_rack_alone()
    {
        SeedExisting();
        string before = _registry.Dump();
        var racks = Validated(Layout(("alpha", new { PosX = 99.5 }), ("Gamma", new { Folder = @"C:\g" })));   // "alpha" is the key "Alpha"

        var result = Executor.Execute(racks, replaceExisting: false);

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(new[] { "Alpha", "Beta", "Empty", "Gamma", "Unknown Legacy" }, _registry.KeyNames);
        Assert.Equal("99.5", _registry.Get("Alpha", "PosX")!.Value.Data);                // changed
        Assert.Equal(48, _registry.Get("Alpha", "IconSize")!.Value.Data);                // kept, kind and all
        Assert.Equal(RegistryValueKind.QWord, _registry.Get("Alpha", "Width")!.Value.Kind);
        Assert.Equal(RegistryValueKind.Binary, _registry.Get("Alpha", "Blob")!.Value.Kind);
        Assert.Equal(@"C:\g", _registry.Get("Gamma", "Folder")!.Value.Data);
        Assert.Contains("mode=merge", Assert.Single(_audit.Events).Detail);
        Assert.NotEqual(before, _registry.Dump());
    }

    [Fact]
    public void A_file_with_no_racks_touches_nothing_not_even_in_replace_mode()
    {
        SeedExisting();
        string before = _registry.Dump();
        var result = Executor.Execute([], replaceExisting: true);
        Assert.Equal(ImportResult.Imported(0), result);
        Assert.Equal(0, _registry.TotalCalls);
        Assert.Equal(before, _registry.Dump());
        Assert.Empty(_audit.Events);
    }

    // ---- invalid input never reaches the registry --------------------------------------------------------------------

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Version\":9,\"Racks\":[]}")]
    [InlineData("{\"Version\":1,\"Racks\":[{\"Name\":\"Ok\",\"Values\":{\"Folder\":\"C:\\\\ok\"}},{\"Name\":\"Bad\",\"Values\":{\"Folder\":\"\\\\\\\\evil\\\\share\"}}]}")]
    [InlineData("{\"Version\":1,\"Racks\":[{\"Name\":\"Ok\"},{\"Name\":\"ok\"}]}")]
    [InlineData("{\"Version\":1,\"Racks\":[{\"Name\":\"Ok\",\"Values\":{\"IdleOpacity\":9}}]}")]
    public void An_invalid_file_leaves_the_registry_untouched_and_never_even_reads_it(string json)
    {
        SeedExisting();
        string before = _registry.Dump();

        var result = Service.ImportJson(json, replaceExisting: true);

        Assert.Equal(ImportStatus.Rejected, result.Status);
        Assert.NotEmpty(result.Message);
        Assert.False(result.StateChanged);
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(0, _registry.TotalCalls);   // no snapshot, no delete, no write: validation finishes first

        var record = Assert.Single(_audit.Events);
        Assert.Equal(LayoutAudit.ImportKind, record.Kind);
        Assert.Equal("Rejected", record.Status);
        Assert.Equal(result.Message, record.Reason);
    }

    [Fact]
    public void An_oversized_or_unreadable_file_is_rejected_before_the_registry_is_touched()
    {
        SeedExisting();
        string before = _registry.Dump();
        string dir = Path.Combine(Path.GetTempPath(), "RacksLayoutExec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string big = Path.Combine(dir, "big.json");
            File.WriteAllText(big, "{\"Racks\":[]}" + new string(' ', LayoutLimits.MaxFileBytes));
            var tooBig = Service.ImportFile(big, replaceExisting: true);
            Assert.Equal(ImportStatus.Rejected, tooBig.Status);
            Assert.Contains("too large", tooBig.Message);

            var missing = Service.ImportFile(Path.Combine(dir, "missing.json"), replaceExisting: true);
            Assert.Equal(ImportStatus.Rejected, missing.Status);

            var relative = Service.ImportFile("relative.json", replaceExisting: true);
            Assert.Equal(ImportStatus.Rejected, relative.Status);
        }
        finally { Directory.Delete(dir, recursive: true); }
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(0, _registry.TotalCalls);
    }

    // ---- failure and rollback --------------------------------------------------------------------------------------

    [Fact]
    public void A_failure_in_the_middle_of_the_writes_restores_the_original_racks_exactly()
    {
        SeedExisting();
        string before = _registry.Dump();
        var racks = Validated(TwoNewRacks());
        _registry.FailWhen = (op, key, _) => op == "apply" && key == "Delta";   // Gamma is written, Delta fails

        var result = Executor.Execute(racks, replaceExisting: true, source: "file.json");

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.True(result.StateChanged);
        Assert.True(result.RolledBack);
        Assert.False(result.RollbackFailed);
        Assert.Contains("scripted apply failure", result.Message);
        Assert.Equal(before, _registry.Dump());   // every key, value, kind and byte as it was; Gamma is gone

        var record = Assert.Single(_audit.Events);
        Assert.Equal(LayoutAudit.ImportKind, record.Kind);
        Assert.Equal("Failed", record.Status);
        Assert.Contains("rolledBack=True", record.Detail);
    }

    [Fact]
    public void A_failure_while_deleting_restores_the_original_racks_exactly()
    {
        SeedExisting();
        string before = _registry.Dump();
        _registry.FailWhen = (op, _, n) => op == "delete" && n == 2;   // the second old rack cannot be deleted

        var result = Executor.Execute(Validated(TwoNewRacks()), replaceExisting: true);

        Assert.True(result.RolledBack);
        Assert.Equal(before, _registry.Dump());
    }

    [Fact]
    public void A_registry_that_does_not_keep_what_was_written_is_caught_by_the_verify_step_and_rolled_back()
    {
        SeedExisting();
        string before = _registry.Dump();
        _registry.DropValueOnApply = "IsLocked";   // accepts the write, stores nothing

        var result = Executor.Execute(Validated(TwoNewRacks()), replaceExisting: true);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.Contains("IsLocked", result.Message);
        Assert.Contains("Gamma", result.Message);
        Assert.True(result.RolledBack);
        _registry.DropValueOnApply = null;
        Assert.Equal(before, _registry.Dump());
    }

    [Fact]
    public void Merge_mode_failure_puts_back_the_racks_it_touched_and_leaves_the_others_alone()
    {
        SeedExisting();
        string before = _registry.Dump();
        _registry.FailWhen = (op, key, _) => op == "apply" && key == "Gamma";
        var racks = Validated(Layout(("Alpha", new { PosX = 1 }), ("Gamma", new { Folder = @"C:\g" })));

        var result = Executor.Execute(racks, replaceExisting: false);

        Assert.True(result.RolledBack);
        Assert.Equal(before, _registry.Dump());
    }

    [Fact]
    public void A_restore_that_fails_is_surfaced_not_swallowed_and_is_audited()
    {
        SeedExisting();
        var racks = Validated(TwoNewRacks());
        // The second write fails (the import), and so does every write after it (the restore of the old racks).
        _registry.FailWhen = (op, _, n) => op == "apply" && n >= 2;

        var result = Executor.Execute(racks, replaceExisting: true, source: "file.json");

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.True(result.StateChanged);
        Assert.False(result.RolledBack);
        Assert.True(result.RollbackFailed);
        Assert.Contains("could not restore", result.RollbackDetails);
        Assert.Contains("Alpha", result.RollbackDetails);                 // names what was not put back
        Assert.Contains("missing after the restore", result.RollbackDetails);
        Assert.NotEmpty(result.Message);                                  // the original cause is still there

        Assert.Equal(2, _audit.Events.Count);
        var failed = _audit.Events.Single(e => e.Kind == LayoutAudit.ImportKind);
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("rolledBack=False", failed.Detail);
        var rollback = _audit.Events.Single(e => e.Kind == LayoutAudit.RollbackFailedKind);
        Assert.Equal("Failed", rollback.Status);
        Assert.Equal("file.json", rollback.Source);
        Assert.Contains("Alpha", rollback.Detail);
        Assert.Contains(result.Message, rollback.Detail);
    }

    [Fact]
    public void A_restore_that_cannot_delete_what_the_import_left_behind_is_reported_too()
    {
        SeedExisting();
        // Gamma is written, Delta fails; then the restore cannot remove Gamma again.
        _registry.FailWhen = (op, key, _) => (op == "apply" && key == "Delta") || (op == "delete" && key == "Gamma");

        var result = Executor.Execute(Validated(TwoNewRacks()), replaceExisting: true);

        Assert.True(result.RollbackFailed);
        Assert.Contains("could not remove 'Gamma'", result.RollbackDetails);
        Assert.Contains("left behind", result.RollbackDetails);
        Assert.Contains(_audit.Events, e => e.Kind == LayoutAudit.RollbackFailedKind);
    }

    [Fact]
    public void If_the_current_racks_cannot_even_be_read_nothing_is_changed_and_it_says_so()
    {
        SeedExisting();
        string before = _registry.Dump();
        _registry.FailWhen = (op, _, _) => op == "list";

        var result = Executor.Execute(Validated(TwoNewRacks()), replaceExisting: true);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.False(result.StateChanged);
        Assert.False(result.RolledBack);
        Assert.False(result.RollbackFailed);
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(0, _registry.Calls("delete") + _registry.Calls("apply"));
    }

    [Fact]
    public void An_audit_sink_that_fails_never_changes_the_outcome()
    {
        SeedExisting();
        var executor = new LayoutImportExecutor(_registry, new ThrowingAuditSink());
        var ok = executor.Execute(Validated(TwoNewRacks()), replaceExisting: true);
        Assert.Equal(ImportStatus.Imported, ok.Status);

        _registry.FailWhen = (op, _, n) => op == "apply" && n >= 1;
        var failed = executor.Execute(Validated(Layout(("Z", new { Folder = @"C:\z" }))), replaceExisting: false);
        Assert.Equal(ImportStatus.Failed, failed.Status);
    }
}
