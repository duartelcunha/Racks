using System.IO;
using System.Text.Json;
using Racks.Tests.Fakes;
using Racks.UseCases.Layout;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Layouts;

// Export: the same JSON shape as ever, written in one atomic step. Temp folders only; no registry (the store is a stub).
public sealed class LayoutExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RacksLayoutExport-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingAuditSink _audit = new();
    private readonly List<LoadedRack> _racks = [];

    public LayoutExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

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

    private LayoutExporter Exporter => new(new StubStore(_racks), _audit);

    private void AddRack(string name = "Work", string? key = null) => _racks.Add(new LoadedRack(key ?? name, new RackSettings
    {
        Name = name,
        Folder = @"C:\Users\me\Docs",
        PosX = 1.5,
        IconSize = 64,
        IsLocked = true,
        TitleTextAlignment = System.Windows.HorizontalAlignment.Right,
        AssignedFiles = ["a.txt", "b.txt"],
        ShowOnVirtualDesktops = [1, 3],
        LastAccessedFiles = ["x.txt"],
        CustomOrderFiles = [Tuple.Create("a.txt", "1")],
    }));

    private string Dest(string name = "layout.json") => Path.Combine(_dir, name);

    private IEnumerable<string> Leftovers() => Directory.EnumerateFiles(_dir).Where(f => f.EndsWith(".tmp", StringComparison.Ordinal));

    // ---- shape ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_file_has_the_shape_every_earlier_export_had_so_old_builds_and_new_builds_read_each_other()
    {
        AddRack();
        Assert.True(Exporter.WriteTo(Dest()).Ok);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(Dest()));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("Version").GetInt32());
        Assert.Equal("Racks", root.GetProperty("ExportedBy").GetString());
        Assert.True(root.TryGetProperty("ExportedAt", out _));
        var rack = Assert.Single(root.GetProperty("Racks").EnumerateArray());
        Assert.Equal("Work", rack.GetProperty("Name").GetString());

        var values = rack.GetProperty("Values");
        Assert.Equal("Work", values.GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.String, values.GetProperty("PosX").ValueKind);       // decimals: invariant text
        Assert.Equal("1.5", values.GetProperty("PosX").GetString());
        Assert.Equal(JsonValueKind.Number, values.GetProperty("IconSize").ValueKind);   // integers: numbers
        Assert.Equal(64, values.GetProperty("IconSize").GetInt32());
        Assert.Equal("True", values.GetProperty("IsLocked").GetString());              // switches: True/False text
        Assert.Equal("Right", values.GetProperty("TitleTextAlignment").GetString());
        Assert.Equal("a.txt|b.txt", values.GetProperty("AssignedFiles").GetString());   // the two lists the registry keeps as text
        Assert.Equal("1,3", values.GetProperty("ShowOnVirtualDesktops").GetString());
        Assert.False(values.TryGetProperty("TitleText", out _));                        // nothing the rack did not have

        var multi = rack.GetProperty("MultiStringValues");
        Assert.Equal(new[] { "x.txt" }, multi.GetProperty("LastAccessedFiles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "a.txt,1" }, multi.GetProperty("CustomOrderFiles").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void The_file_is_utf8_without_a_byte_order_mark_and_imports_through_the_validator()
    {
        AddRack();
        AddRack("Second");
        Assert.True(Exporter.WriteTo(Dest()).Ok);

        var bytes = File.ReadAllBytes(Dest());
        Assert.NotEqual(0xEF, bytes[0]);

        var parsed = LayoutParser.ReadFile(Dest());
        Assert.True(parsed.Ok, parsed.Error);
        var validated = LayoutValidator.Validate(parsed.Document!);
        Assert.True(validated.Ok, validated.Error);
        Assert.Equal(new[] { "Work", "Second" }, validated.Racks!.Select(r => r.Name));
    }

    [Fact]
    public void The_key_in_the_file_is_the_racks_name_even_when_the_registry_key_differs()
    {
        AddRack(name: "Real Name", key: "OldKey");
        Assert.True(Exporter.WriteTo(Dest()).Ok);

        var validated = LayoutValidator.Validate(LayoutParser.ReadFile(Dest()).Document!);
        Assert.True(validated.Ok, validated.Error);   // key and Name value agree, so the file imports
        Assert.Equal("Real Name", Assert.Single(validated.Racks!).Name);
    }

    [Fact]
    public void A_layout_this_build_would_not_read_back_is_not_exported()
    {
        for (int i = 0; i <= LayoutLimits.MaxRacks; i++) AddRack("rack" + i);
        var result = Exporter.WriteTo(Dest());
        Assert.False(result.Ok);
        Assert.Contains("too large", result.Message);
        Assert.False(File.Exists(Dest()));
    }

    // ---- atomic write --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_destination_is_created_and_no_temp_file_is_left()
    {
        AddRack();
        var result = Exporter.WriteTo(Dest());
        Assert.True(result.Ok);
        Assert.Equal(1, result.RackCount);
        Assert.Empty(Leftovers());
        Assert.Single(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public void An_existing_destination_is_replaced_whole()
    {
        AddRack();
        File.WriteAllText(Dest(), "OLD CONTENT " + new string('x', 100_000));
        Assert.True(Exporter.WriteTo(Dest()).Ok);
        Assert.StartsWith("{", File.ReadAllText(Dest()));
        Assert.DoesNotContain("OLD CONTENT", File.ReadAllText(Dest()));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void A_destination_that_cannot_be_replaced_keeps_its_content_and_leaves_no_temp_file()
    {
        AddRack();
        File.WriteAllText(Dest(), "PRECIOUS");
        using (var holder = new FileStream(Dest(), FileMode.Open, FileAccess.Read, FileShare.None))   // another program has it open
        {
            var result = Exporter.WriteTo(Dest());
            Assert.False(result.Ok);
            Assert.NotEmpty(result.Message);
        }
        Assert.Equal("PRECIOUS", File.ReadAllText(Dest()));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void A_read_only_destination_keeps_its_content_and_leaves_no_temp_file()
    {
        AddRack();
        File.WriteAllText(Dest(), "PRECIOUS");
        File.SetAttributes(Dest(), FileAttributes.ReadOnly);
        var result = Exporter.WriteTo(Dest());
        Assert.False(result.Ok);
        Assert.Equal("PRECIOUS", File.ReadAllText(Dest()));
        Assert.Empty(Leftovers());
    }

    [Theory]
    [InlineData("relative.json")]
    [InlineData(@"\\.\C:\x\a.json")]
    [InlineData(@"C:\x\..\a.json")]
    [InlineData("")]
    public void A_path_the_policy_refuses_writes_nothing(string path)
    {
        AddRack();
        var result = Exporter.WriteTo(path);
        Assert.False(result.Ok);
        Assert.NotEmpty(result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Fact]
    public void A_stream_name_in_the_path_is_refused()
    {
        AddRack();
        Assert.False(Exporter.WriteTo(Dest("a.json:evil")).Ok);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Fact]
    public void A_missing_folder_is_an_error_and_is_not_created()
    {
        AddRack();
        string missing = Path.Combine(_dir, "nope", "a.json");
        var result = Exporter.WriteTo(missing);
        Assert.False(result.Ok);
        Assert.Contains("folder does not exist", result.Message);
        Assert.False(Directory.Exists(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void A_destination_that_is_a_folder_is_an_error_and_the_folder_is_untouched()
    {
        AddRack();
        string folder = Dest("folder.json");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "keep");
        var result = Exporter.WriteTo(folder);
        Assert.False(result.Ok);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, "inside.txt")));
        Assert.Empty(Leftovers());
    }

    // ---- audit and errors never thrown ---------------------------------------------------------------------------------

    [Fact]
    public void Success_and_failure_are_audited_and_a_failing_audit_changes_nothing()
    {
        AddRack();
        Assert.True(Exporter.WriteTo(Dest()).Ok);
        Assert.False(Exporter.WriteTo("relative.json").Ok);

        Assert.Equal(2, _audit.Events.Count);
        Assert.All(_audit.Events, e => Assert.Equal(LayoutAudit.ExportKind, e.Kind));
        Assert.Equal("Succeeded", _audit.Events[0].Status);
        Assert.Equal(Dest(), _audit.Events[0].Destination);
        Assert.Equal("Failed", _audit.Events[1].Status);
        Assert.NotEmpty(_audit.Events[1].Reason);

        var quiet = new LayoutExporter(new StubStore(_racks), new ThrowingAuditSink());
        Assert.True(quiet.WriteTo(Dest("other.json")).Ok);
    }

    [Fact]
    public void A_store_that_throws_is_a_failed_result_not_an_exception()
    {
        var exporter = new LayoutExporter(new ThrowingStore(), _audit);
        var result = exporter.WriteTo(Dest());
        Assert.False(result.Ok);
        Assert.False(File.Exists(Dest()));
    }

    private sealed class ThrowingStore : IRackSettingsStore
    {
        public RackLoadResult LoadAll() => throw new InvalidOperationException("store is broken");
        public RackSettings ReadGlobalStyleDefaults() => new();
        public bool Save(Instance rack) => false;
        public bool Normalize(Instance rack) => false;
        public bool SaveField(Instance rack, string propertyName) => false;
        public bool SaveGlobalDefault(Instance rack, string propertyName) => false;
        public bool Delete(string name) => false;
        public int ResetGlobalStyleDefaults() => 0;
    }
}
