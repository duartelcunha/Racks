using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Layout;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;
using Racks.UseCases.Layout;

namespace Racks.Tests.Layouts;

// P9 review fixes: a precise rollback, a recovery file when the restore fails, an atomic write that cannot lose both files,
// an exporter that checks its own output, old backups that stay importable, duplicate JSON keys. In-memory registry and temp
// folders only; nothing here touches the real registry.
public sealed class LayoutReviewFixTests : IDisposable
{
    private readonly ScriptedRackRegistry _registry = new();
    private readonly RecordingAuditSink _audit = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RacksLayoutReview-" + Guid.NewGuid().ToString("N"));

    public LayoutReviewFixTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

    private string Recovery => Path.Combine(_dir, "recovery");
    private LayoutImportExecutor Executor => new(_registry, _audit, Recovery);
    private LayoutService Service(ScriptedRackRegistry? registry = null) => new(registry ?? _registry, new NoStore(), _audit, Recovery);

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

    private static IReadOnlyList<ValidatedRack> Validated(string json)
    {
        var parsed = LayoutParser.Parse(json);
        Assert.True(parsed.Ok, parsed.Error);
        var result = LayoutValidator.Validate(parsed.Document!);
        Assert.True(result.Ok, result.Error);
        return result.Racks!;
    }

    private static string Layout(params (string Name, object Values)[] racks) =>
        JsonSerializer.Serialize(new { Version = 1, Racks = racks.Select(r => new { r.Name, r.Values }).ToArray() });

    // Racks in the forms the app writes, so a layout made from them imports back to exactly the same state.
    private static void SeedImportable(ScriptedRackRegistry registry)
    {
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            registry.Seed(name, "Name", name, RegistryValueKind.String);
            registry.Seed(name, "Folder", @"C:\Users\me\" + name, RegistryValueKind.String);
            registry.Seed(name, "PosX", "10.5", RegistryValueKind.String);
            registry.Seed(name, "IconSize", 48, RegistryValueKind.DWord);
            registry.Seed(name, "IsLocked", "True", RegistryValueKind.String);
            registry.Seed(name, "AssignedFiles", "a.txt|b.txt", RegistryValueKind.String);
            registry.Seed(name, "LastAccessedFiles", new[] { "a.txt", "b.txt" }, RegistryValueKind.MultiString);
        }
    }

    private static string TwoNew() => Layout(("Delta", new { Folder = @"C:\d" }), ("Epsilon", new { Folder = @"C:\e" }));

    // ---- 1. precise rollback ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_failure_at_the_very_first_delete_touches_nothing_else()
    {
        SeedImportable(_registry);
        string before = _registry.Dump();
        _registry.FailWhen = (op, _, n) => op == "delete" && n == 1;

        var result = Executor.Execute(Validated(TwoNew()), replaceExisting: true);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.True(result.RolledBack);
        Assert.False(result.RollbackFailed);
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(1, _registry.Calls("delete"));   // only the attempt that failed: no other rack was deleted, none "rolled back" by deleting
        Assert.Equal(0, _registry.Calls("apply"));    // nothing written, nothing rewritten
        Assert.False(Directory.Exists(Recovery));      // no recovery file: nothing was lost
    }

    [Fact]
    public void A_failure_at_the_second_delete_restores_only_the_first_rack()
    {
        SeedImportable(_registry);
        string before = _registry.Dump();
        _registry.FailWhen = (op, _, n) => op == "delete" && n == 2;

        var result = Executor.Execute(Validated(TwoNew()), replaceExisting: true);

        Assert.True(result.RolledBack);
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(2, _registry.Calls("delete"));   // Alpha, then the failed Beta; Gamma was never reached
        Assert.Equal(1, _registry.Calls("apply"));    // only Alpha is written back
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.Security.SecurityException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    public void A_key_that_fails_half_way_with_any_exception_type_is_rolled_back(Type exceptionType)
    {
        SeedImportable(_registry);
        string before = _registry.Dump();
        _registry.Failure = message => (Exception)Activator.CreateInstance(exceptionType, message)!;
        _registry.HalfwayFailureKey = "Epsilon";   // Delta is complete, Epsilon has half its values, then it throws

        var result = Executor.Execute(Validated(Layout(("Delta", new { Folder = @"C:\d", IconSize = 40 }), ("Epsilon", new { Folder = @"C:\e", IconSize = 50, IsLocked = true, PosX = 3 }))),
            replaceExisting: true);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.True(result.StateChanged);
        Assert.True(result.RolledBack);
        Assert.False(result.RollbackFailed);
        Assert.Contains("half way", result.Message);
        Assert.Equal(before, _registry.Dump());   // the half-written Epsilon and the finished Delta are gone, the old racks are back
    }

    [Fact]
    public void A_failure_while_listing_and_reading_during_the_rollback_is_reported_not_thrown()
    {
        SeedImportable(_registry);
        _registry.Failure = message => new UnauthorizedAccessException(message);
        _registry.FailWhen = (op, key, n) => (op == "apply" && key == "Epsilon") || (op == "list" && n >= 2);

        var result = Executor.Execute(Validated(TwoNew()), replaceExisting: true);

        Assert.True(result.RollbackFailed);
        Assert.Contains("could not be read", result.RollbackDetails);
    }

    // ---- recovery file ---------------------------------------------------------------------------------------------------

    [Fact]
    public void When_the_restore_fails_too_the_snapshot_is_saved_as_a_recovery_layout_that_imports_back_to_the_original()
    {
        SeedImportable(_registry);
        string original = _registry.Dump();
        _registry.FailWhen = (op, _, _) => op == "apply";   // the import's writes fail, and so does every write of the restore

        var result = Executor.Execute(Validated(TwoNew()), replaceExisting: true, source: "in.json");

        Assert.True(result.RollbackFailed);
        Assert.Null(result.RecoveryError);
        string file = Assert.IsType<string>(result.RecoveryFile);
        Assert.True(File.Exists(file));
        Assert.Equal(Recovery, Path.GetDirectoryName(file));
        Assert.Matches(@"^layout-\d{8}-\d{6}-\d{3}\.json$", Path.GetFileName(file));

        // The audit record names the file.
        var record = _audit.Events.Single(e => e.Kind == LayoutAudit.RollbackFailedKind);
        Assert.Contains("recoveryFile=" + file, record.Detail);

        // The recovery file is a layout: a fresh registry that imports it gets the original racks back, kind for kind.
        var fresh = new ScriptedRackRegistry();
        var again = Service(fresh).ImportFile(file, replaceExisting: true);
        Assert.Equal(ImportStatus.Imported, again.Status);
        Assert.Equal(3, again.ImportedCount);
        Assert.Empty(again.Dropped ?? []);
        Assert.Equal(original, fresh.Dump());
    }

    [Fact]
    public void If_the_recovery_file_cannot_be_written_either_the_result_and_the_audit_say_so()
    {
        SeedImportable(_registry);
        File.WriteAllText(Recovery, "a file where the recovery folder should be");   // CreateDirectory will fail
        _registry.FailWhen = (op, _, _) => op == "apply";

        var result = Executor.Execute(Validated(TwoNew()), replaceExisting: true);

        Assert.True(result.RollbackFailed);
        Assert.Null(result.RecoveryFile);
        Assert.False(string.IsNullOrWhiteSpace(result.RecoveryError));
        Assert.Contains("NOT WRITTEN", _audit.Events.Single(e => e.Kind == LayoutAudit.RollbackFailedKind).Detail);

        var message = LayoutMessages.ForImport(result)!;
        Assert.Contains("could not be saved either", message.Text);
        Assert.Contains(result.RecoveryError!, message.Text);
    }

    [Fact]
    public void The_message_after_a_failed_restore_points_to_the_recovery_file()
    {
        var result = new ImportResult(ImportStatus.Failed, 0, "write denied", StateChanged: true, RollbackFailed: true,
            RecoveryFile: @"C:\Users\me\AppData\Roaming\Racks\recovery\layout-20261027-161055-123.json");
        var text = LayoutMessages.ForImport(result)!.Text;
        Assert.Contains("write denied", text);
        Assert.Contains(@"C:\Users\me\AppData\Roaming\Racks\recovery\layout-20261027-161055-123.json", text);
        Assert.Contains("Import that file to get them back", text);
    }

    [Fact]
    public void The_recovery_folder_keeps_only_the_newest_five()
    {
        var start = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var saved = Enumerable.Range(0, 8).Select(i => RecoveryFolder.Save(Recovery, [(byte)'{', (byte)'}'], start.AddSeconds(i)).Path!).ToList();

        var left = Directory.EnumerateFiles(Recovery).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(RecoveryFolder.KeepNewest, left.Count);
        Assert.Equal(saved.Skip(3).Select(Path.GetFileName).Order(StringComparer.Ordinal), left);   // the 5 newest
        Assert.DoesNotContain(Directory.EnumerateFiles(Recovery), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Pruning_never_touches_files_that_are_not_recovery_files()
    {
        Directory.CreateDirectory(Recovery);
        File.WriteAllText(Path.Combine(Recovery, "notes.txt"), "mine");
        for (int i = 0; i < 7; i++) RecoveryFolder.Save(Recovery, [(byte)'{', (byte)'}'], DateTime.UtcNow.AddSeconds(i));
        Assert.True(File.Exists(Path.Combine(Recovery, "notes.txt")));
    }

    // ---- 2. atomic write ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_writer_replaces_an_existing_file_and_creates_a_new_one_with_the_same_single_move()
    {
        string dest = Path.Combine(_dir, "out.json");
        Assert.True(LayoutFileWriter.Write(dest, "first"u8.ToArray()).Ok);
        Assert.Equal("first", File.ReadAllText(dest));
        Assert.True(LayoutFileWriter.Write(dest, "second"u8.ToArray()).Ok);
        Assert.Equal("second", File.ReadAllText(dest));
        Assert.Empty(Directory.EnumerateFiles(_dir, ".racks-*"));
    }

    [Fact]
    public void A_failed_replace_keeps_the_destination_content_and_leaves_no_temp_file()
    {
        string dest = Path.Combine(_dir, "out.json");
        File.WriteAllText(dest, "PRECIOUS");
        using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = LayoutFileWriter.Write(dest, "new"u8.ToArray());
            Assert.False(result.Ok);
            Assert.DoesNotContain(".racks-", result.Error);   // the destination is intact, so no temp file is mentioned
        }
        Assert.Equal("PRECIOUS", File.ReadAllText(dest));
        Assert.Empty(Directory.EnumerateFiles(_dir, ".racks-*"));
    }

    [Fact]
    public void A_file_name_close_to_the_limit_still_works_because_the_temp_name_is_short()
    {
        string name = new string('n', 245) + ".json";   // 250 characters: the old temp name (dot + name + guid + .tmp) was over 255
        string dest = Path.Combine(_dir, name);
        var result = LayoutFileWriter.Write(dest, "x"u8.ToArray());
        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(dest));
        Assert.Empty(Directory.EnumerateFiles(_dir, ".racks-*"));
    }

    // ---- 3. the exporter checks its own output --------------------------------------------------------------------------

    private sealed class StubStore(List<LoadedRack> racks) : IRackSettingsStore
    {
        public RackLoadResult LoadAll() => new(racks.ToList(), []);
        public RackSettings ReadGlobalStyleDefaults() => new();
        public bool Save(Instance rack) => throw new NotSupportedException();
        public bool Normalize(Instance rack) => throw new NotSupportedException();
        public bool SaveField(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool SaveGlobalDefault(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool Delete(string name) => throw new NotSupportedException();
        public int ResetGlobalStyleDefaults() => throw new NotSupportedException();
    }

    [Fact]
    public void An_export_the_import_would_not_take_back_is_a_failure_that_names_the_rack_and_the_value()
    {
        var pairs = Enumerable.Range(0, LayoutLimits.MaxListItems + 1).Select(i => Tuple.Create("f" + i, "1")).ToList();
        var racks = new List<LoadedRack> { new("Work", new RackSettings { Name = "Work", CustomOrderFiles = pairs }) };
        string dest = Path.Combine(_dir, "export.json");

        var result = new LayoutExporter(new StubStore(racks), _audit).WriteTo(dest);

        Assert.False(result.Ok);
        Assert.Contains("would not import it back", result.Message);
        Assert.Contains("rack 'Work'", result.Message);
        Assert.Contains("CustomOrderFiles", result.Message);
        Assert.Contains("10001 items", result.Message);
        Assert.False(File.Exists(dest));                                      // nothing was truncated or written
        Assert.Equal("Failed", _audit.Events.Last().Status);
    }

    [Fact]
    public void Two_racks_whose_names_collide_after_the_name_mapping_are_not_exported()
    {
        var racks = new List<LoadedRack>
        {
            new("KeyOne", new RackSettings { Name = "Same" }),
            new("KeyTwo", new RackSettings { Name = "same" }),   // the Name value is the identity: this would be a duplicate on import
        };
        var result = new LayoutExporter(new StubStore(racks), _audit).WriteTo(Path.Combine(_dir, "export.json"));
        Assert.False(result.Ok);
        Assert.Contains("more than once", result.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "export.json")));
    }

    // ---- 4. old backups stay importable ---------------------------------------------------------------------------------

    private const string OldBackup = """
    {
      "Version": 1,
      "Racks": [
        { "Name": "Docs", "Values": { "Name": "Docs", "Folder": "C:\\Users\\me\\Docs", "PosX": "NaN", "PosY": "40", "TitleFontSize": "500",
                                      "IconSize": 48, "IsLocked": "False" },
          "MultiStringValues": { "LastAccessedFiles": [ "a.pdf" ] } },
        { "Name": "Pics", "Values": { "Folder": "C:\\Pics", "IdleOpacity": "7" } }
      ]
    }
    """;

    [Fact]
    public void An_old_backup_with_bad_numbers_imports_the_rest_and_reports_each_dropped_value_to_the_result_and_the_audit()
    {
        SeedImportable(_registry);
        string path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, OldBackup);

        var result = Service().ImportFile(path, replaceExisting: true);

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(new[] { "Docs:PosX", "Docs:TitleFontSize", "Pics:IdleOpacity" },
            result.Dropped!.Select(d => d.Rack + ":" + d.Field).Order(StringComparer.Ordinal));

        Assert.Equal(new[] { "Docs", "Pics" }, _registry.KeyNames);
        Assert.Null(_registry.Get("Docs", "PosX"));
        Assert.Null(_registry.Get("Docs", "TitleFontSize"));
        Assert.Equal("40", _registry.Get("Docs", "PosY")!.Value.Data);
        Assert.Equal(48, _registry.Get("Docs", "IconSize")!.Value.Data);
        Assert.Null(_registry.Get("Pics", "IdleOpacity"));

        var records = _audit.Events.Where(e => e.Kind == LayoutAudit.ValueDroppedKind).ToList();
        Assert.Equal(3, records.Count);
        Assert.All(records, r => Assert.Equal("Skipped", r.Status));
        Assert.Contains(records, r => r.Detail == "rack=Docs; field=PosX");
        Assert.Contains(_audit.Events, e => e.Kind == LayoutAudit.ImportKind && e.Status == "Succeeded");

        // The user is told, once, which values were left out.
        var text = LayoutMessages.ForImport(result)!.Text;
        Assert.Contains("Imported 2 rack(s)", text);
        Assert.Contains("rack 'Docs': PosX", text);
        Assert.Contains("rack 'Docs': TitleFontSize", text);
        Assert.Contains("rack 'Pics': IdleOpacity", text);
    }

    [Fact]
    public void A_clean_import_has_nothing_dropped_and_no_message()
    {
        var result = Service().ImportJson(TwoNew(), replaceExisting: true);
        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Null(result.Dropped);
        Assert.Null(LayoutMessages.ForImport(result));
        Assert.DoesNotContain(_audit.Events, e => e.Kind == LayoutAudit.ValueDroppedKind);
    }

    [Fact]
    public void The_message_lists_at_most_a_few_dropped_values_and_counts_the_rest()
    {
        var dropped = Enumerable.Range(0, 20).Select(i => new DroppedValue("R", "F" + i, "bad")).ToList();
        var text = LayoutMessages.ForImport(new ImportResult(ImportStatus.Imported, 1, "", Dropped: dropped))!.Text;
        Assert.Contains("F0", text);
        Assert.Contains("F7", text);
        Assert.DoesNotContain("F8", text);
        Assert.Contains("12 more", text);
    }

    // ---- 5. duplicate JSON keys ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{\"Version\":1,\"Version\":1,\"Racks\":[]}")]
    [InlineData("{\"Racks\":[{\"Name\":\"a\",\"Name\":\"b\"}]}")]
    [InlineData("{\"Racks\":[{\"Name\":\"a\",\"Values\":{\"Folder\":\"C:\\\\ok\",\"Folder\":\"\\\\\\\\evil\\\\share\"}}]}")]
    [InlineData("{\"Racks\":[{\"Name\":\"a\",\"Values\":{\"PosX\":1,\"PosX\":2}}]}")]
    [InlineData("{\"Racks\":[{\"Name\":\"a\",\"MultiStringValues\":{\"LastAccessedFiles\":[],\"LastAccessedFiles\":[\"x\"]}}]}")]
    public void A_property_repeated_in_one_object_refuses_the_file(string json)
    {
        var result = LayoutParser.Parse(json);
        Assert.False(result.Ok);
        Assert.Contains("repeats a property", result.Error);
    }

    [Fact]
    public void A_repeated_property_refused_through_the_service_leaves_the_registry_untouched()
    {
        SeedImportable(_registry);
        string before = _registry.Dump();
        var result = Service().ImportJson("{\"Racks\":[{\"Name\":\"a\",\"Values\":{\"Folder\":\"C:\\\\ok\",\"Folder\":\"C:\\\\other\"}}]}", replaceExisting: true);
        Assert.Equal(ImportStatus.Rejected, result.Status);
        Assert.Equal(before, _registry.Dump());
        Assert.Equal(0, _registry.TotalCalls);
    }

    // ---- 6. dialog exceptions are audited -----------------------------------------------------------------------------

    [Fact]
    public void An_exception_that_reaches_the_dialog_layer_is_audited()
    {
        LayoutAudit.DialogFailed(_audit, import: true, new InvalidOperationException("dialog exploded"));
        LayoutAudit.DialogFailed(_audit, import: false, new IOException("disk"));

        Assert.Equal(2, _audit.Events.Count);
        Assert.All(_audit.Events, e => { Assert.Equal(LayoutAudit.DialogFailedKind, e.Kind); Assert.Equal("Failed", e.Status); });
        Assert.Equal("import dialog", _audit.Events[0].Source);
        Assert.Equal("dialog exploded", _audit.Events[0].Reason);
        Assert.Equal("export dialog", _audit.Events[1].Source);
        Assert.Contains("IOException", _audit.Events[1].Detail);

        LayoutAudit.DialogFailed(new ThrowingAuditSink(), import: true, new Exception("x"));   // and a failing audit never throws
    }
}
