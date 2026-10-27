using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Tests.Persistence;
using Racks.UseCases.Layout;

namespace Racks.Tests.Layouts;

/// <summary>
/// The whole import and export path over the REAL registry code, in a throwaway root (<c>Software\RacksTests-&lt;guid&gt;</c>, never
/// HKCU\SOFTWARE\Racks): full <c>Import()</c>, the stored kinds, export then import, an old-format file, and a rollback that puts
/// every REG_ kind back. The logic cases (many failure shapes) are in <see cref="LayoutImportExecutorTests"/> with a fake.
/// </summary>
[Collection("Registry")]
public sealed class LayoutRegistryImportTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RacksLayoutReg-" + Guid.NewGuid().ToString("N"));

    public LayoutRegistryImportTests()
    {
        Assert.NotEqual("Racks", InstanceController.appName);   // never the real key
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        _reg.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private LayoutService Service(IRackRegistry? registry = null) => new(registry ?? HkcuRackRegistry.ForApp(), _reg.Repository, _reg.Audit);

    private string Write(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string Flat(SortedDictionary<string, string> snapshot) => string.Join("\n", snapshot.Select(kv => kv.Key + "=" + kv.Value));

    // A layout as the OLD exporter wrote it (en-US machine): numbers as JSON numbers for REG_DWORD, decimals as text,
    // lists in MultiStringValues, a legacy name and an unknown one in the file.
    private const string OldExport = """
    {
      "Version": 1,
      "ExportedBy": "Racks",
      "ExportedAt": "2026-03-01T10:15:30.1234567Z",
      "Racks": [
        {
          "Name": "Docs",
          "Values": {
            "Name": "Docs", "Folder": "C:\\Users\\me\\Documents", "PosX": "120.5", "PosY": "80", "Width": "300", "Height": "260",
            "IdleOpacity": "0.4", "IconSize": 48, "GridSize": 16, "Opacity": 26, "SortBy": 1, "FolderOrder": 0,
            "IsLocked": "False", "ShowInGrid": "True", "TitleText": "Docs", "TitleFontFamily": "Segoe UI", "TitleBarColor": "#0C000000",
            "AutoRouteRegex": "\\.pdf$", "ShowOnVirtualDesktops": "", "AssignedFiles": "a.pdf|b.pdf", "BackgroundImagePath": "",
            "TitleTextAlignment": "Left", "MoveFilesOnDrop": "True", "SomethingFromAnOlderBuild": "x"
          },
          "MultiStringValues": { "LastAccessedFiles": [ "a.pdf" ], "CustomOrderFiles": [ "a.pdf,0", "b.pdf,1" ] }
        },
        {
          "Name": "Pics",
          "Values": { "Name": "Pics", "Folder": "empty", "Minimized": "True" },
          "MultiStringValues": { "LastAccessedFiles": [], "CustomOrderFiles": [] }
        }
      ]
    }
    """;

    [Fact]
    public void An_old_format_export_still_imports_and_loads_without_a_single_finding()
    {
        _reg.SeedRack("Other", "Name", "Other", RegistryValueKind.String);   // replaced
        var result = Service().ImportFile(Write("old.json", OldExport), replaceExisting: true);

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(2, result.ImportedCount);
        Assert.False(_reg.RackKeyExists("Other"));

        var load = _reg.Repository.LoadAll();
        Assert.Empty(load.Issues);
        var docs = load.Racks.Single(r => r.KeyName == "Docs").Settings;
        Assert.Equal(120.5, docs.PosX);
        Assert.Equal(48, docs.IconSize);
        Assert.Equal(0.4, docs.IdleOpacity);
        Assert.Equal(System.Windows.HorizontalAlignment.Left, docs.TitleTextAlignment);
        Assert.Equal(new[] { "a.pdf", "b.pdf" }, docs.AssignedFiles);
        Assert.Equal(new[] { "a.pdf" }, docs.LastAccessedFiles);
        Assert.Equal(2, docs.CustomOrderFiles!.Count);
        Assert.Empty(docs.ShowOnVirtualDesktops!);
        Assert.True(load.Racks.Single(r => r.KeyName == "Pics").Settings.Minimized);

        // Stored kinds are the ones the repository writes: REG_DWORD for integers, invariant text for decimals.
        Assert.Equal((48, RegistryValueKind.DWord), ((int)_reg.RawRack("Docs", "IconSize").Data!, _reg.RawRack("Docs", "IconSize").Kind!.Value));
        Assert.Equal(("120.5", RegistryValueKind.String), ((string)_reg.RawRack("Docs", "PosX").Data!, _reg.RawRack("Docs", "PosX").Kind!.Value));
        Assert.Null(_reg.RawRack("Docs", "MoveFilesOnDrop").Data);   // the legacy name is read and thrown away, not stored
        Assert.Null(_reg.RawRack("Docs", "SomethingFromAnOlderBuild").Data);
    }

    [Fact]
    public void A_full_golden_layout_imports_to_exactly_what_a_normal_save_writes()
    {
        var values = new Dictionary<string, object>();
        var multi = new Dictionary<string, string[]>();
        foreach (var name in GoldenRack.Names)
        {
            var (data, kind) = GoldenRack.Raw(name, GoldenRack.RawStyle.Invariant);
            if (kind == RegistryValueKind.MultiString) multi[name] = (string[])data;
            else values[name] = data;
        }
        string json = JsonSerializer.Serialize(new { Version = 1, Racks = new[] { new { Name = GoldenRack.Name, Values = values, MultiStringValues = multi } } });

        _reg.SeedRack("Reference", GoldenRack.RawAll(GoldenRack.RawStyle.Invariant));   // what WriteInstanceToKey stores
        string reference = Flat(_reg.Snapshot("Reference"));
        var result = Service().ImportFile(Write("golden.json", json), replaceExisting: true);

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.False(_reg.RackKeyExists("Reference"));   // replace mode removed it
        Assert.NotEqual("", reference);
        Assert.Equal(reference, Flat(_reg.Snapshot(GoldenRack.Name)));   // same names, kinds and text
        Assert.Empty(GoldenRack.Mismatches(_reg.LoadSingle(GoldenRack.Name)));
    }

    [Fact]
    public void Export_then_import_gives_back_the_same_racks_value_for_value()
    {
        _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(GoldenRack.RawStyle.Invariant));
        _reg.SeedRack("Plain", new (string, object, RegistryValueKind)[] { ("Name", "Plain", RegistryValueKind.String), ("Folder", @"C:\Plain", RegistryValueKind.String), ("IconSize", 40, RegistryValueKind.DWord) });
        string before = _reg.SnapshotAll();
        string file = Path.Combine(_dir, "export.json");

        var export = Service().ExportToFile(file);
        Assert.True(export.Ok, export.Message);
        Assert.Equal(2, export.RackCount);
        Assert.Equal(before, _reg.SnapshotAll());   // exporting reads, it never writes

        Registry.CurrentUser.DeleteSubKeyTree(_reg.RackPath(GoldenRack.Name));
        Registry.CurrentUser.DeleteSubKeyTree(_reg.RackPath("Plain"));
        Assert.Equal(ImportStatus.Imported, Service().ImportFile(file, replaceExisting: true).Status);

        Assert.Equal(before, _reg.SnapshotAll());   // every key, value, kind and text, byte for byte
    }

    [Fact]
    public void An_exported_file_imports_into_a_registry_that_has_other_racks_and_replaces_them()
    {
        _reg.SeedRack("Keep", new (string, object, RegistryValueKind)[] { ("Name", "Keep", RegistryValueKind.String), ("Folder", @"C:\Keep", RegistryValueKind.String) });
        string file = Path.Combine(_dir, "export.json");
        Assert.True(Service().ExportToFile(file).Ok);

        _reg.SeedRack("Extra", "Name", "Extra", RegistryValueKind.String);
        Assert.Equal(1, Service().ImportFile(file, replaceExisting: true).ImportedCount);
        Assert.True(_reg.RackKeyExists("Keep"));
        Assert.False(_reg.RackKeyExists("Extra"));
    }

    [Fact]
    public void An_invalid_file_leaves_the_real_registry_exactly_as_it_was()
    {
        _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(GoldenRack.RawStyle.Invariant));
        string before = _reg.SnapshotAll();

        foreach (var (name, json) in new[]
        {
            ("garbage", "{ not json"),
            ("unc", "{\"Version\":1,\"Racks\":[{\"Name\":\"Ok\"},{\"Name\":\"Bad\",\"Values\":{\"Folder\":\"\\\\\\\\evil\\\\share\"}}]}"),
            ("newer", "{\"Version\":2,\"Racks\":[{\"Name\":\"Ok\"}]}"),
            ("range", "{\"Version\":1,\"Racks\":[{\"Name\":\"Ok\",\"Values\":{\"Opacity\":999}}]}"),
        })
        {
            var result = Service().ImportFile(Write(name + ".json", json), replaceExisting: true);
            Assert.Equal(ImportStatus.Rejected, result.Status);
            Assert.Equal(before, _reg.SnapshotAll());
        }
    }

    // A real registry that fails when asked to write one particular rack.
    private sealed class FailsOnKey(IRackRegistry inner, string key) : IRackRegistry
    {
        public string Path => inner.Path;
        public IReadOnlyList<string> GetRackKeyNames() => inner.GetRackKeyNames();
        public IReadOnlyDictionary<string, RegistryValue> ReadValues(string keyName) => inner.ReadValues(keyName);
        public IReadOnlyDictionary<string, RegistryValue> ReadValuesExact(string keyName) => inner.ReadValuesExact(keyName);
        public bool DeleteKey(string keyName) => inner.DeleteKey(keyName);
        public void Apply(string keyName, IReadOnlyList<RegistryWrite> writes)
        {
            if (string.Equals(keyName, key, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("write denied");
            inner.Apply(keyName, writes);
        }
    }

    [Fact]
    public void A_failed_import_over_the_real_registry_restores_every_value_kind_byte_for_byte()
    {
        _reg.SeedRack("Alpha", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Alpha", RegistryValueKind.String),
            ("PosX", "10.5", RegistryValueKind.String),
            ("IconSize", 48, RegistryValueKind.DWord),
            ("Width", 321L, RegistryValueKind.QWord),
            ("Folder", @"%USERPROFILE%\Docs", RegistryValueKind.ExpandString),
            ("Blob", new byte[] { 1, 2, 3, 0, 255 }, RegistryValueKind.Binary),
            ("LastAccessedFiles", new[] { "a.txt", "b.txt" }, RegistryValueKind.MultiString),
        });
        _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(GoldenRack.RawStyle.Qword));
        string before = _reg.SnapshotAll();

        string json = "{\"Version\":1,\"Racks\":[{\"Name\":\"First\",\"Values\":{\"Folder\":\"C:\\\\one\"}},{\"Name\":\"Second\",\"Values\":{\"Folder\":\"C:\\\\two\"}}]}";
        var result = Service(new FailsOnKey(HkcuRackRegistry.ForApp(), "Second")).ImportFile(Write("two.json", json), replaceExisting: true);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.True(result.RolledBack);
        Assert.False(result.RollbackFailed);
        Assert.Equal(before, _reg.SnapshotAll());
        Assert.False(_reg.RackKeyExists("First"));

        // The snapshot above reads REG_EXPAND_SZ expanded; the restore must also have put the UNEXPANDED text back.
        using var alpha = Registry.CurrentUser.OpenSubKey(_reg.RackPath("Alpha"))!;
        Assert.Equal(@"%USERPROFILE%\Docs", alpha.GetValue("Folder", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.ExpandString, alpha.GetValueKind("Folder"));
        Assert.Contains(_reg.Audit.Events, e => e.Kind == LayoutAudit.ImportKind && e.Status == "Failed");
    }

    [Fact]
    public void Merge_mode_over_the_real_registry_keeps_what_the_file_does_not_mention()
    {
        _reg.SeedRack("Docs", new (string, object, RegistryValueKind)[] { ("Name", "Docs", RegistryValueKind.String), ("IconSize", 40, RegistryValueKind.DWord), ("Width", 500L, RegistryValueKind.QWord) });
        _reg.SeedRack("Untouched", "Name", "Untouched", RegistryValueKind.String);

        var result = Service().ImportFile(Write("m.json", "{\"Version\":1,\"Racks\":[{\"Name\":\"docs\",\"Values\":{\"IconSize\":96}}]}"), replaceExisting: false);

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(96, _reg.RawRack("Docs", "IconSize").Data);
        Assert.Equal(500L, _reg.RawRack("Docs", "Width").Data);   // untouched, still REG_QWORD
        Assert.True(_reg.RackKeyExists("Untouched"));
    }
}
