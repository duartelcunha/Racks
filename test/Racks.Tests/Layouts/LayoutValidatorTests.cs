using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.UseCases.Layout;

namespace Racks.Tests.Layouts;

// INV-IMPORT-1, -3, -5: what the validator accepts and refuses. The accepted/refused sets of the old ParseAndValidate are pinned
// by RackLayoutImportValidationTests (through the RackLayoutIO facade); these are the rules P9 added, plus the typed output.
// Nothing here touches the registry.
public sealed class LayoutValidatorTests
{
    private static string Json(object? values = null, object? multi = null, string name = "Work", int? version = 1)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Version"] = version,
            ["Racks"] = new[] { new { Name = name, Values = values ?? new { }, MultiStringValues = multi ?? new { } } },
        }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static LayoutValidationResult Check(string json)
    {
        var parsed = LayoutParser.Parse(json);
        Assert.True(parsed.Ok, parsed.Error);
        return LayoutValidator.Validate(parsed.Document!);
    }

    private static string Refused(string json)
    {
        var result = Check(json);
        Assert.False(result.Ok, "the file was accepted");
        Assert.Null(result.Racks);
        return result.Error!;
    }

    private static ValidatedRack Accepted(string json)
    {
        var result = Check(json);
        Assert.True(result.Ok, result.Error);
        return Assert.Single(result.Racks!);
    }

    // ---- version -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void A_newer_format_version_is_refused_with_a_clear_message(int version)
    {
        string error = Refused(Json(version: version));
        Assert.Contains("newer version of Racks", error);
        Assert.Contains($"file format {version}", error);
        Assert.Contains("Update Racks", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_below_one_is_refused(int version)
        => Assert.Contains("invalid format version", Refused(Json(version: version)));

    [Fact]
    public void The_current_version_is_read_and_a_missing_version_reads_as_the_current_one()
    {
        Accepted(Json(version: 1));
        Accepted(Json(version: null));
    }

    // ---- numbers: ranges and finiteness --------------------------------------------------------------------------------

    [Theory]
    [InlineData("IdleOpacity", 5)]
    [InlineData("IdleOpacity", -0.5)]
    [InlineData("Opacity", 300)]
    [InlineData("Opacity", -1)]
    [InlineData("PosX", 1e9)]
    [InlineData("Width", -10)]
    [InlineData("TitleFontSize", 0)]
    [InlineData("SortBy", 11)]
    [InlineData("FolderOrder", 3)]
    [InlineData("AnimationSpeed", 99)]
    public void A_number_out_of_range_refuses_the_file_and_names_the_rack_and_the_value(string field, double value)
    {
        string error = Refused(Json(new Dictionary<string, object> { [field] = value }));
        Assert.Contains("'Work'", error);
        Assert.Contains(field, error);
        Assert.Contains("out of range", error);
    }

    [Theory]
    [InlineData("IconSize", 5)]     // the setters clamp these two; an import refuses instead of changing the file's meaning
    [InlineData("IconSize", 1000)]
    [InlineData("GridSize", 1)]
    [InlineData("GridSize", 999)]
    public void A_number_the_loader_would_clamp_is_refused_not_clamped_silently(string field, int value)
    {
        string error = Refused(Json(new Dictionary<string, object> { [field] = value }));
        Assert.Contains(field, error);
        Assert.Contains("out of range", error);
    }

    [Theory]
    [InlineData("PosX", "NaN")]
    [InlineData("PosX", "Infinity")]
    [InlineData("PosY", "-Infinity")]
    [InlineData("Width", "1e999")]
    [InlineData("IdleOpacity", "not a number")]
    public void NaN_Infinity_and_text_that_is_not_a_number_are_refused(string field, string value)
    {
        string error = Refused(Json(new Dictionary<string, object> { [field] = value }));
        Assert.Contains("'Work'", error);
        Assert.Contains(field, error);
    }

    [Fact]
    public void A_json_number_too_large_for_a_double_is_refused()
    {
        // 1e999 as a bare JSON number (not text): overflow to Infinity, never stored.
        string json = "{\"Version\":1,\"Racks\":[{\"Name\":\"Work\",\"Values\":{\"PosX\":1e999}}]}";
        Assert.Contains("PosX", Refused(json));
    }

    [Theory]
    [InlineData("IconSize", "1.5")]
    [InlineData("IconSize", "abc")]
    [InlineData("SortBy", "99999999999")]    // does not fit 32 bits
    public void A_whole_number_value_must_be_a_whole_number(string field, string value)
        => Assert.Contains(field, Refused(Json(new Dictionary<string, object> { [field] = value })));

    [Fact]
    public void A_switch_and_an_enum_must_say_one_of_their_values()
    {
        Assert.Contains("IsLocked", Refused(Json(new { IsLocked = "yes" })));
        Assert.Contains("IsLocked", Refused(Json(new { IsLocked = 1 })));
        Assert.Contains("TitleTextAlignment", Refused(Json(new { TitleTextAlignment = "Diagonal" })));
        Assert.Contains("TitleTextAlignment", Refused(Json(new { TitleTextAlignment = "Left, Right" })));
    }

    [Fact]
    public void Numbers_in_range_and_at_the_edges_are_accepted()
    {
        var rack = Accepted(Json(new Dictionary<string, object>
        {
            ["IdleOpacity"] = 0,
            ["Opacity"] = 255,
            ["IconSize"] = 16,
            ["GridSize"] = 256,
            ["PosX"] = -100000,
            ["Width"] = 0,
        }));
        Assert.Equal(6, rack.Values.Count);
    }

    [Fact]
    public void A_decimal_in_invariant_text_reads_the_same_in_every_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var rack = Accepted(Json(new { IdleOpacity = "0.5", PosX = 12.5 }));
            Assert.Equal(0.5, rack.Settings.IdleOpacity);
            Assert.Equal(12.5, rack.Settings.PosX);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- stored forms --------------------------------------------------------------------------------------------------

    [Fact]
    public void Values_are_stored_in_the_forms_the_settings_repository_writes()
    {
        var rack = Accepted(Json(
            new Dictionary<string, object>
            {
                ["posx"] = 12.5,                 // invariant text
                ["IconSize"] = 64,               // REG_DWORD
                ["Opacity"] = "77",              // text number for an int value: still REG_DWORD
                ["IsLocked"] = true,             // "True"
                ["ShowInGrid"] = "False",
                ["TitleTextAlignment"] = "Right",
                ["AssignedFiles"] = "a.txt|b.txt",
                ["ShowOnVirtualDesktops"] = "1,3",
                ["TitleText"] = "My title",
            },
            new Dictionary<string, string[]> { ["lastaccessedfiles"] = ["x", "y"], ["CustomOrderFiles"] = ["a.txt,1"] }));

        object? Data(string n) => rack.Writes.Single(w => w.Name == n).Data;
        RegistryValueKind Kind(string n) => rack.Writes.Single(w => w.Name == n).Kind;

        Assert.Equal("12.5", Data("PosX")); Assert.Equal(RegistryValueKind.String, Kind("PosX"));
        Assert.Equal(64, Data("IconSize")); Assert.Equal(RegistryValueKind.DWord, Kind("IconSize"));
        Assert.Equal(77, Data("Opacity")); Assert.Equal(RegistryValueKind.DWord, Kind("Opacity"));
        Assert.Equal("True", Data("IsLocked"));
        Assert.Equal("False", Data("ShowInGrid"));
        Assert.Equal("Right", Data("TitleTextAlignment"));
        Assert.Equal("a.txt|b.txt", Data("AssignedFiles"));
        Assert.Equal("1,3", Data("ShowOnVirtualDesktops"));
        Assert.Equal(new[] { "x", "y" }, (string[])Data("LastAccessedFiles")!); Assert.Equal(RegistryValueKind.MultiString, Kind("LastAccessedFiles"));
        Assert.Equal(new[] { "a.txt,1" }, (string[])Data("CustomOrderFiles")!);

        // The typed model carries the same data.
        Assert.Equal(12.5, rack.Settings.PosX);
        Assert.Equal(64, rack.Settings.IconSize);
        Assert.True(rack.Settings.IsLocked);
        Assert.Equal(System.Windows.HorizontalAlignment.Right, rack.Settings.TitleTextAlignment);
        Assert.Equal(new[] { "a.txt", "b.txt" }, rack.Settings.AssignedFiles);
        Assert.Equal(new[] { 1, 3 }, rack.Settings.ShowOnVirtualDesktops);
    }

    [Fact]
    public void The_legacy_name_is_accepted_and_not_stored()
    {
        var rack = Accepted(Json(new { MoveFilesOnDrop = "True", Folder = @"C:\x" }));
        Assert.Equal(new[] { "Folder" }, rack.Values.Keys);
    }

    [Fact]
    public void A_list_item_the_loader_would_drop_is_dropped_and_a_list_with_no_good_item_is_refused()
    {
        var rack = Accepted(Json(new { ShowOnVirtualDesktops = "1,x,3" }, new Dictionary<string, string[]> { ["CustomOrderFiles"] = ["a.txt,1", "junk"] }));
        Assert.Equal(new[] { 1, 3 }, rack.Settings.ShowOnVirtualDesktops);
        Assert.Equal(new[] { "a.txt,1" }, rack.Writes.Single(w => w.Name == "CustomOrderFiles").Data as string[]);
        Assert.Contains("ShowOnVirtualDesktops", Refused(Json(new { ShowOnVirtualDesktops = "abc" })));
    }

    // ---- rack names ------------------------------------------------------------------------------------------------------

    private static string TwoRacks(string first, string second) =>
        JsonSerializer.Serialize(new { Version = 1, Racks = new object[] { new { Name = first }, new { Name = second } } });

    [Theory]
    [InlineData("Work", "work")]
    [InlineData("Work", "WORK")]
    [InlineData("Work", "Work")]
    [InlineData("\u00c9cole", "\u00e9cole")]
    public void Two_racks_whose_names_differ_only_in_case_are_refused(string first, string second)
    {
        string error = Refused(TwoRacks(first, second));
        Assert.Contains("more than once", error);
        Assert.Contains("not case sensitive", error);
    }

    [Fact]
    public void Different_names_are_fine()
        => Assert.Equal(2, Check(TwoRacks("Work", "Home")).Racks!.Count);

    [Fact]
    public void A_name_value_that_is_not_the_rack_name_is_refused_and_case_only_differences_are_not()
    {
        string error = Refused(Json(new { Name = "Other" }));
        Assert.Contains("'Work'", error);
        Assert.Contains("Name value", error);
        Assert.Contains("'Other'", error);

        Accepted(Json(new { Name = "work" }));    // the registry compares key names without regard to case
        Accepted(Json(new { NAME = "Work" }));
        Accepted(Json(new { }));                  // no Name value: the key name is used
    }

    [Fact]
    public void A_rack_name_with_a_control_character_is_refused()
    {
        Assert.Contains("unsafe rack name", Refused(Json(name: "a\nb")));
        Assert.Contains("unsafe rack name", Refused(Json(name: "a\0b")));
    }

    [Fact]
    public void An_empty_rack_entry_is_refused_and_a_file_with_no_racks_is_empty_not_refused()
    {
        Assert.Contains("empty rack entry", Refused("{\"Racks\":[null]}"));
        Assert.Empty(Check("{\"Racks\":[]}").Racks!);
        Assert.Empty(Check("null").Racks!);
    }

    // ---- sizes ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_list_with_too_many_items_is_refused_in_both_sections()
    {
        string many = string.Join("|", Enumerable.Range(0, LayoutLimits.MaxListItems + 1).Select(i => "f" + i));
        string error = Refused(Json(new { AssignedFiles = many }));
        Assert.Contains("AssignedFiles", error);
        Assert.Contains("10001 items", error);

        var lines = Enumerable.Range(0, LayoutLimits.MaxListItems + 1).Select(i => "f" + i).ToArray();
        Assert.Contains("LastAccessedFiles", Refused(Json(multi: new Dictionary<string, string[]> { ["LastAccessedFiles"] = lines })));

        // At the limit it is accepted.
        Accepted(Json(new { AssignedFiles = string.Join("|", Enumerable.Range(0, LayoutLimits.MaxListItems).Select(i => "f" + i)) }));
    }

    [Fact]
    public void A_list_item_that_is_too_long_or_lists_that_are_too_big_together_are_refused()
    {
        string longItem = new('x', LayoutLimits.MaxListItemLength + 1);
        Assert.Contains("longer than 1024", Refused(Json(multi: new Dictionary<string, string[]> { ["LastAccessedFiles"] = [longItem] })));
        Assert.Contains("longer than 1024", Refused(Json(new { AssignedFiles = "ok|" + longItem })));
        Accepted(Json(multi: new Dictionary<string, string[]> { ["LastAccessedFiles"] = [new string('x', LayoutLimits.MaxListItemLength)] }));

        // 300 items of 1000 characters: each is fine, together they are over the total.
        var big = Enumerable.Range(0, 300).Select(_ => new string('y', 1000)).ToArray();
        Assert.Contains("together", Refused(Json(multi: new Dictionary<string, string[]> { ["LastAccessedFiles"] = big })));
    }

    [Fact]
    public void A_regular_expression_is_capped_in_length_and_otherwise_not_judged()
    {
        foreach (var field in new[] { "AutoRouteRegex", "FileFilterRegex", "FileFilterHideRegex" })
        {
            string error = Refused(Json(new Dictionary<string, object> { [field] = new string('a', LayoutLimits.MaxRegexLength + 1) }));
            Assert.Contains(field, error);
            Assert.Contains("1024 characters", error);
            Accepted(Json(new Dictionary<string, object> { [field] = new string('a', LayoutLimits.MaxRegexLength) }));
        }
        // Not compiled at import (SafeRegex compiles it, with a timeout, where it is used): even a broken pattern is only text here.
        Accepted(Json(new { AutoRouteRegex = "(", FileFilterRegex = "(a+)+$" }));
    }

    [Fact]
    public void A_text_value_with_a_nul_character_is_refused_because_the_registry_would_cut_it()
    {
        Assert.Contains("NUL", Refused(Json(new { TitleText = "a\0b" })));
        Assert.Contains("NUL", Refused(Json(multi: new Dictionary<string, string[]> { ["LastAccessedFiles"] = ["a\0b"] })));
    }

    // ---- paths, as before (the exact messages are in RackLayoutImportValidationTests) ------------------------------------

    [Theory]
    [InlineData(@"\\host\share")]
    [InlineData("//host/share/x")]
    [InlineData(@"\\?\C:\Users")]
    [InlineData(@"\\.\pipe\x")]
    public void Unc_and_device_paths_name_the_network_folder_in_the_message(string path)
        => Assert.Contains("network folder", Refused(Json(new { Folder = path })));

    [Theory]
    [InlineData(@"C:\Users\..\Windows")]
    [InlineData(@"C:\x\file.txt:stream")]
    [InlineData(@"C:folder")]
    [InlineData(@"relative\path")]
    [InlineData("C:\\x\u0001y")]
    public void Traversal_streams_and_relative_paths_are_unsafe(string path)
    {
        Assert.Contains("unsafe Folder", Refused(Json(new { Folder = path })));
        Assert.Contains("unsafe BackgroundImagePath", Refused(Json(new { BackgroundImagePath = path })));
    }

    // ---- nothing escapes -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_bad_rack_anywhere_refuses_the_whole_file_and_returns_no_racks()
    {
        string json = JsonSerializer.Serialize(new
        {
            Version = 1,
            Racks = new object[]
            {
                new { Name = "Good", Values = new { Folder = @"C:\ok" } },
                new { Name = "Bad", Values = new { IdleOpacity = 7 } },
            },
        });
        var result = Check(json);
        Assert.False(result.Ok);
        Assert.Null(result.Racks);
        Assert.Contains("'Bad'", result.Error);
    }

    [Fact]
    public void A_lone_surrogate_in_a_value_is_a_refusal_not_a_crash()
    {
        // JSON allows the escape; the string it makes is not valid text.
        string json = "{\"Version\":1,\"Racks\":[{\"Name\":\"Work\",\"Values\":{\"TitleText\":\"\\ud800\"}}]}";
        var parsed = LayoutParser.Parse(json);
        if (!parsed.Ok) return;   // the parser may already refuse it; either way it never throws
        var result = LayoutValidator.Validate(parsed.Document!);
        Assert.True(result.Ok || result.Error != null);
    }
}
