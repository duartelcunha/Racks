using System.IO;
using System.Text.RegularExpressions;
using Racks.Tests.Guards;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-IMPORT-3: font names, colors and image paths from rack settings never reach a WPF parser
// that could open a network location (and leak the user's NTLM hash).
public class SafeWpfValuesTests
{
    [Theory]
    [InlineData("Segoe UI")]
    [InlineData("Segoe UI Variable Display, Segoe UI")]
    [InlineData("Segoe UI Variable Display,Segoe UI")]
    [InlineData("Times New Roman")]
    [InlineData("Arial")]
    [InlineData("Noto Sans CJK JP")]
    [InlineData("微软雅黑")]
    [InlineData("Font_Name-2.0 & Co's")]
    [InlineData("a, b, c, d, e, f, g, h")]
    public void Plain_font_family_names_are_accepted(string name)
    {
        Assert.True(SafeWpfValues.IsSafeFontFamily(name));
        Assert.Equal(name, SafeWpfValues.CreateFontFamily(name).Source);
    }

    [Theory]
    [InlineData(@"\\attacker.example\share\#Evil")]
    [InlineData(@"\\attacker.example\share\Evil.ttf")]
    [InlineData("//attacker.example/share/#Evil")]
    [InlineData("file://attacker.example/share/#Evil")]
    [InlineData(@"C:\Fonts\#Evil")]
    [InlineData("./#Evil")]
    [InlineData("#Evil")]
    [InlineData("Arial#Evil")]
    [InlineData("Arial, \\\\attacker.example\\share\\#Evil")]
    [InlineData("pack://application:,,,/#Evil")]
    [InlineData("%TEMP%")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Arial,,Segoe UI")]
    [InlineData("Arial\nSegoe UI")]
    [InlineData("a, b, c, d, e, f, g, h, i")]
    [InlineData(null)]
    public void Font_family_values_that_can_name_a_location_are_refused(string? name)
    {
        Assert.False(SafeWpfValues.IsSafeFontFamily(name));
        Assert.Equal(SafeWpfValues.DefaultFontFamily, SafeWpfValues.SafeFontFamilyName(name));
        Assert.Equal(SafeWpfValues.DefaultFontFamily, SafeWpfValues.CreateFontFamily(name).Source);
    }

    [Fact]
    public void A_font_family_name_is_at_most_64_characters_per_family()
    {
        Assert.True(SafeWpfValues.IsSafeFontFamily(new string('a', 64)));
        Assert.False(SafeWpfValues.IsSafeFontFamily(new string('a', 65)));
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#FFFF")]
    [InlineData("#AABBCC")]
    [InlineData("#0C000000")]
    [InlineData("#ff7cff00")]
    [InlineData("Red")]
    [InlineData("Transparent")]
    [InlineData("sc#1,0.5,0.25,0")]
    [InlineData("sc#1, 0.5, 0.25, 1e-3")]
    public void Plain_colors_are_accepted(string color)
    {
        Assert.True(SafeWpfValues.IsSafeColor(color));
        // and WPF parses them without needing anything else
        System.Windows.Media.ColorConverter.ConvertFromString(color);
    }

    [Theory]
    [InlineData("ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5")]
    [InlineData(@"ContextColor \\attacker.example\share\p.icc 1.0,0.5,0.5,0.5")]
    [InlineData("contextcolor file:///C:/p.icc 1,0,0,0")]
    [InlineData("#GGG")]
    [InlineData("#12345")]
    [InlineData("#")]
    [InlineData("Red Green")]
    [InlineData("#FFF;")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Foo")]
    [InlineData("NotAColorName")]
    [InlineData("sc#,,")]
    [InlineData("sc#1,2")]
    public void Colors_that_are_not_plain_values_are_refused(string? color)
    {
        Assert.False(SafeWpfValues.IsSafeColor(color));
        Assert.Equal("#FFFFFF", SafeWpfValues.SafeColorOr(color, "#FFFFFF"));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("sc#,,")]
    public void Values_the_pattern_allows_are_still_refused_when_wpf_cannot_parse_them(string color)
    {
        // The shape is plain (a name, sc# numbers), but it is not a color WPF knows. WPF throws
        // FormatException for "Foo" and InvalidOperationException for "sc#,,".
        Assert.ThrowsAny<Exception>(() => System.Windows.Media.ColorConverter.ConvertFromString(color));
        Assert.False(SafeWpfValues.IsSafeColor(color));
    }

    [Fact]
    public void Context_colors_are_refused_by_the_pattern_before_wpf_parses_anything()
    {
        // ColorConverter opens the profile URI of "ContextColor <uri> ..." while parsing, so the
        // pattern check must come first and return before the converter is called.
        string src = File.ReadAllText(RepoPaths.App("Util", "SafeWpfValues.cs"));
        int method = src.IndexOf("public static bool IsSafeColor(", StringComparison.Ordinal);
        int pattern = src.IndexOf("if (!SafeMatch(ColorPattern, value)) return false;", method, StringComparison.Ordinal);
        int parse = src.IndexOf("ColorConverter.ConvertFromString(value)", method, StringComparison.Ordinal);
        Assert.True(method > 0 && pattern > method && parse > pattern);
        foreach (var context in new[] { "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5", "ContextColor x" })
            Assert.False(SafeWpfValues.IsSafeColor(context));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\bg.png", true)]
    [InlineData("D:/bg.jpg", true)]
    [InlineData(@"\\attacker.example\share\bg.png", false)]
    [InlineData("//attacker.example/share/bg.png", false)]
    [InlineData(@"\\?\UNC\attacker.example\share\bg.png", false)]
    [InlineData(@"\\.\C:\bg.png", false)]
    [InlineData(@"C:bg.png", false)]
    [InlineData(@"C:\x\..\bg.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Background_images_load_only_from_local_drive_paths(string? path, bool ok)
        => Assert.Equal(ok, SafeWpfValues.IsLocalDrivePath(path));

    [Fact]
    public void Rack_font_and_color_setters_ignore_values_that_can_name_a_location()
    {
        var rack = new Instance("probe", false);
        using (rack.SuspendPersistence())
        {
            rack.TitleFontFamily = "Arial";
            rack.ItemFontFamily = "Consolas";
            rack.TitleBarColor = "#112233";
            rack.ListViewFontShadowColor = "#000000";

            rack.TitleFontFamily = @"\\attacker.example\share\#Evil";
            rack.ItemFontFamily = "file://attacker.example/share/#Evil";
            rack.TitleBarColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.TitleTextColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.BorderColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ActiveBackgroundColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ActiveBorderColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ActiveTitleTextColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ListViewBackgroundColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ListViewFontColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
            rack.ListViewFontShadowColor = "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5";
        }

        Assert.Equal("Arial", rack.TitleFontFamily);
        Assert.Equal("Consolas", rack.ItemFontFamily);
        Assert.Equal("#112233", rack.TitleBarColor);
        Assert.Equal("#000000", rack.ListViewFontShadowColor);
        foreach (var c in new[] { rack.TitleTextColor, rack.BorderColor, rack.ActiveBackgroundColor, rack.ActiveBorderColor,
                                  rack.ActiveTitleTextColor, rack.ListViewBackgroundColor, rack.ListViewFontColor })
            Assert.True(SafeWpfValues.IsSafeColor(c), c);
    }

    [Fact]
    public void Font_families_are_only_built_through_the_safe_factory()
    {
        // new FontFamily(<anything but a string literal>) outside SafeWpfValues could take a value
        // from rack settings and let WPF open the location in it.
        var call = new Regex(@"new\s+(System\.Windows\.Media\.)?FontFamily\s*\(\s*(?!"")", RegexOptions.None, TimeSpan.FromSeconds(1));
        var hits = Directory.EnumerateFiles(RepoPaths.App(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) != "SafeWpfValues.cs")
            .Where(f => call.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(hits);
    }
}
