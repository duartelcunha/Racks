using System.Globalization;
using System.IO;
using Racks.Properties;
using Racks.Tests.Guards;

namespace Racks.Tests.Util;

public class L10nTests
{
    [Fact]
    public void Format_uses_positional_arguments_so_translations_can_reorder_them()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("3 items moved to Desktop", L10n.F("{0} items moved to {1}", 3, "Desktop"));
            Assert.Equal("Desktop: 3", L10n.F("{1}: {0}", 3, "Desktop"));   // reordered, as a translator would
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Format_follows_the_current_culture_for_numbers()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-PT");
            Assert.Equal("1,5", L10n.F("{0}", 1.5));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(1, "item", "items", "item")]
    [InlineData(0, "item", "items", "items")]
    [InlineData(2, "item", "items", "items")]
    [InlineData(1000, "item", "items", "items")]
    public void Plural_picks_one_only_for_exactly_one(long count, string one, string other, string expected)
        => Assert.Equal(expected, L10n.Plural(count, one, other));

    [Fact]
    public void Windows_share_one_font_resource_instead_of_repeating_the_font_name()
    {
        var offenders = Directory.EnumerateFiles(RepoPaths.App(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("FontFamily=\"Segoe UI Variable"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_shared_font_has_fallbacks_for_chinese_japanese_and_korean()
    {
        string app = File.ReadAllText(RepoPaths.App("App.xaml"));
        Assert.Contains("Microsoft YaHei UI", app);
        Assert.Contains("Yu Gothic UI", app);
        Assert.Contains("Malgun Gothic", app);
    }
}
