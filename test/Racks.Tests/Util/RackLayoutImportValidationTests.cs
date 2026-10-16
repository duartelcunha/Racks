using System.IO;
using System.Text.Json;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-IMPORT-1: an imported layout is validated (and normalized) before it can become registry
// state. These tests run the pure validation step; nothing here touches the registry.
public class RackLayoutImportValidationTests
{
    private static string Layout(object values, object? multi = null, string name = "Work")
        => JsonSerializer.Serialize(new
        {
            Version = 1,
            Racks = new[] { new { Name = name, Values = values, MultiStringValues = multi ?? new { } } },
        });

    [Theory]
    [InlineData(@"\\attacker.example\share")]
    [InlineData(@"\\attacker.example\share\inbox")]
    [InlineData("//attacker.example/share")]
    [InlineData(@"\/attacker.example\share")]
    [InlineData(@"/\attacker.example\share")]
    [InlineData(@"\\?\UNC\attacker.example\share")]
    [InlineData(@"\\?\C:\Users\me\Desktop")]
    [InlineData(@"\\.\C:\Users\me\Desktop")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\??\C:\Users\me")]
    [InlineData(@"\\attacker.example@SSL\DavWWWRoot\x")]
    [InlineData(@"\\attacker.example@SSL@443\share")]
    [InlineData(@"\\10.0.0.5\c$")]
    public void Unc_and_device_paths_are_rejected(string path)
    {
        Assert.False(RackLayoutIO.IsSafeImportedPath(path));
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { Folder = path })));
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { BackgroundImagePath = path })));
    }

    [Theory]
    [InlineData(@"C:folder")]            // drive-relative
    [InlineData(@"C:")]
    [InlineData(@"\Users\me")]           // rooted on the current drive, not fully qualified
    [InlineData(@"relative\path")]
    [InlineData(@"C:\Users\..\Windows")] // traversal
    [InlineData(@"C:\x\file.txt:stream")] // alternate data stream
    [InlineData("C:\\x\u0000y")]
    [InlineData(@"1:\x")]
    public void Relative_traversal_and_odd_paths_are_rejected(string path)
        => Assert.False(RackLayoutIO.IsSafeImportedPath(path));

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\Projects")]
    [InlineData(@"D:/Pictures/bg.png")]
    [InlineData(@"c:\lower\case")]
    [InlineData("")]
    [InlineData("empty")]
    [InlineData("Default Style")]
    public void Local_drive_paths_and_sentinels_are_accepted(string path)
    {
        Assert.True(RackLayoutIO.IsSafeImportedPath(path));
        var racks = RackLayoutIO.ParseAndValidate(Layout(new { Folder = path }));
        Assert.Single(racks);
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("FOLDER")]
    [InlineData("fOlDeR")]
    [InlineData("backgroundimagepath")]
    public void Path_value_names_are_checked_case_insensitively(string key)
    {
        var values = new Dictionary<string, string> { [key] = @"\\attacker.example\share" };
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(values)));
    }

    [Fact]
    public void Two_spellings_of_one_name_are_rejected()
    {
        // The registry would merge them into one value, so a safe "Folder" plus a hostile
        // "folder" must not get through.
        var values = new Dictionary<string, string> { ["Folder"] = @"C:\ok", ["folder"] = @"C:\other" };
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(values)));
    }

    [Fact]
    public void Known_names_are_written_with_their_canonical_spelling()
    {
        var values = new Dictionary<string, object> { ["folder"] = @"C:\Users\me\Docs", ["posx"] = 12, ["istransparent"] = true };
        var rack = Assert.Single(RackLayoutIO.ParseAndValidate(Layout(values)));
        Assert.Equal(new[] { "Folder", "IsTransparent", "PosX" }, rack.Values.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Unknown_value_names_are_dropped()
    {
        var values = new Dictionary<string, object> { ["Folder"] = @"C:\x", ["Shell"] = @"\\evil\x", ["_MigratedFromDeskFrame"] = "1" };
        var rack = Assert.Single(RackLayoutIO.ParseAndValidate(Layout(values)));
        Assert.Equal(new[] { "Folder" }, rack.Values.Keys);
    }

    [Fact]
    public void Multi_string_values_are_allowlisted_and_typed()
    {
        var rack = Assert.Single(RackLayoutIO.ParseAndValidate(Layout(new { }, new Dictionary<string, string[]>
        {
            ["lastaccessedfiles"] = new[] { "a.txt", "b.txt" },
            ["CustomOrderFiles"] = new[] { "a.txt,0" },
            ["Unknown"] = new[] { "x" },
        })));
        Assert.Equal(new[] { "CustomOrderFiles", "LastAccessedFiles" }, rack.MultiStringValues.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void A_scalar_name_in_the_multi_string_section_is_rejected()
        => Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(
            Layout(new { }, new Dictionary<string, string[]> { ["Folder"] = new[] { @"\\evil\share" } })));

    [Fact]
    public void A_multi_string_name_in_the_scalar_section_is_rejected()
        => Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { LastAccessedFiles = "x" })));

    [Fact]
    public void A_path_value_must_be_a_string()
        => Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { Folder = 5 })));

    [Fact]
    public void A_scalar_value_cannot_be_an_object_or_array()
    {
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { PosX = new[] { 1, 2 } })));
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { PosX = new { a = 1 } })));
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData("..")]
    [InlineData("")]
    public void The_name_value_follows_the_rack_name_rule(string name)
    {
        var values = new Dictionary<string, string> { ["Name"] = name };
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(values)));
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void Unsafe_rack_names_are_rejected(string name)
        => Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(Layout(new { }, name: name)));

    [Fact]
    public void A_bad_rack_anywhere_rejects_the_whole_file()
    {
        string json = JsonSerializer.Serialize(new
        {
            Racks = new object[]
            {
                new { Name = "Good", Values = new { Folder = @"C:\ok" } },
                new { Name = "Bad", Values = new { Folder = @"\\evil\share" } },
            },
        });
        Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(json));
    }

    [Fact]
    public void An_export_shaped_file_round_trips()
    {
        string json = """
        {
          "Version": 1,
          "ExportedBy": "Racks",
          "Racks": [
            {
              "Name": "Docs",
              "Values": { "Name": "Docs", "Folder": "C:\\Users\\me\\AppData\\Roaming\\Racks\\VirtualFrames\\Docs",
                          "PosX": "120", "IsLocked": "False", "AutoRouteRegex": "\\.pdf$", "ShowOnVirtualDesktops": "" },
              "MultiStringValues": { "LastAccessedFiles": [], "CustomOrderFiles": [ "a.pdf,0" ] }
            }
          ]
        }
        """;
        var rack = Assert.Single(RackLayoutIO.ParseAndValidate(json));
        Assert.Equal(6, rack.Values.Count);
        Assert.Equal(2, rack.MultiStringValues.Count);
    }

    [Fact]
    public void Invalid_json_is_reported_as_invalid_data()
        => Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate("{ not json"));
}
