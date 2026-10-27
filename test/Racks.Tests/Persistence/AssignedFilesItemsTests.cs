using System.Text.Json;
using Microsoft.Win32;
using Racks.UseCases.Layout;

namespace Racks.Tests.Persistence;

// INV-IDS-1 (P11, finding F3): an AssignedFiles item must be a plain leaf name. The loader and the layout import drop the
// items that are not ("." would name the workspace itself), keep the good ones, and report each drop.
[Collection("Registry")]
public sealed class AssignedFilesItemsTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    private static List<(string, object, RegistryValueKind)> Rack(string name, string assigned) =>
    [
        ("Name", name, RegistryValueKind.String),
        ("Folder", System.IO.Path.GetTempPath(), RegistryValueKind.String),
        ("IsDesktopFilterRack", "True", RegistryValueKind.String),
        ("AssignedFiles", assigned, RegistryValueKind.String),
    ];

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"sub\x.txt")]
    [InlineData("sub/x.txt")]
    [InlineData(@"..\x.txt")]
    [InlineData(@"C:\x.txt")]
    [InlineData("C:x.txt")]
    [InlineData("x.txt:stream")]
    [InlineData("NUL")]
    [InlineData("aux.log")]
    [InlineData("COM1")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void The_loader_drops_an_item_that_is_not_a_plain_name_keeps_the_good_ones_and_reports_it_once(string bad)
    {
        _reg.SeedRack("T", Rack("T", $"a.txt|{bad}|b [1] & 'c'.pdf|\u00e9\u65e5\U0001F4C1.txt"));

        var rack = _reg.LoadSingle("T");

        Assert.Equal(new[] { "a.txt", "b [1] & 'c'.pdf", "\u00e9\u65e5\U0001F4C1.txt" }, rack.AssignedFiles);
        var record = Assert.Single(_reg.Audit.Events, e => e.Kind == "SettingFallback" && e.Detail.Contains("field=AssignedFiles;", StringComparison.Ordinal));
        Assert.Contains("not plain file names", record.Detail + " " + record.Reason);
        // Loading never writes: what the user (or a hostile editor) stored is still there.
        Assert.Contains(bad, _reg.RawRack("T", "AssignedFiles").Data!.ToString());
    }

    [Fact]
    public void A_list_of_only_good_names_is_untouched_and_a_list_of_only_bad_ones_becomes_empty()
    {
        _reg.SeedRack("Good", Rack("Good", "a.txt|b.txt"));
        _reg.SeedRack("Bad", Rack("Bad", ".|..|NUL"));

        var racks = _reg.Load();

        Assert.Equal(new[] { "a.txt", "b.txt" }, racks.Single(r => r.Name == "Good").AssignedFiles);
        Assert.Empty(racks.Single(r => r.Name == "Bad").AssignedFiles);
        Assert.Single(_reg.Audit.Events, e => e.Kind == "SettingFallback" && e.Detail.Contains("field=AssignedFiles;", StringComparison.Ordinal));
    }

    private static string LayoutJson(string assigned) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["Version"] = 1,
        ["Racks"] = new[] { new { Name = "Work", Values = new Dictionary<string, object> { ["AssignedFiles"] = assigned }, MultiStringValues = new { } } },
    });

    [Fact]
    public void A_layout_import_drops_the_bad_items_as_a_dropped_value_and_keeps_the_good_ones()
    {
        var parsed = LayoutParser.Parse(LayoutJson("a.txt|.|..|x:y|CON|b.txt"));
        Assert.True(parsed.Ok, parsed.Error);

        var result = LayoutValidator.Validate(parsed.Document!);

        Assert.True(result.Ok, result.Error);
        var dropped = Assert.Single(result.Dropped!);
        Assert.Equal("AssignedFiles", dropped.Field);
        Assert.Contains("4 of 6", dropped.Reason);
        Assert.Equal(new[] { "a.txt", "b.txt" }, Assert.Single(result.Racks!).Settings.AssignedFiles);
    }

    [Fact]
    public void A_layout_with_good_names_only_reports_nothing()
    {
        var parsed = LayoutParser.Parse(LayoutJson("a.txt|b.txt"));
        var result = LayoutValidator.Validate(parsed.Document!);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Dropped!);
        Assert.Equal(new[] { "a.txt", "b.txt" }, Assert.Single(result.Racks!).Settings.AssignedFiles);
    }
}
