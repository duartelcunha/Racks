using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Racks.Properties;
using Xunit.Abstractions;

namespace Racks.Tests.Guards;

public class LocalizationTests(ITestOutputHelper output)
{
    private static readonly string PropertiesDir = RepoPaths.App("Properties");
    private static readonly HashSet<string> BaseKeys = ReadKeys(Path.Combine(PropertiesDir, "Lang.resx"));

    public static TheoryData<string> CultureFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(PropertiesDir, "Lang.*.resx")) data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(CultureFiles))]
    public void Every_translated_key_exists_in_the_base_file(string cultureFile)
    {
        var unknown = ReadKeys(Path.Combine(PropertiesDir, cultureFile)).Except(BaseKeys).Order().ToList();
        Assert.True(unknown.Count == 0,
            $"{cultureFile} has keys that are not in Lang.resx (typo or removed key): {string.Join(", ", unknown)}");
    }

    [Fact]
    public void Generated_Lang_class_resolves_every_base_key()
    {
        var missing = BaseKeys.Where(k => Lang.ResourceManager.GetString(k, CultureInfo.InvariantCulture) is null).ToList();
        Assert.True(missing.Count == 0,
            $"Lang.ResourceManager cannot resolve: {string.Join(", ", missing)}. Rebuild so Lang.Designer.cs is regenerated.");
    }

    [Fact]
    public void Translations_are_loaded_from_satellite_assemblies()
        => Assert.Equal("退出", Lang.ResourceManager.GetString("TrayContextMenu.Exit", new CultureInfo("zh-CN")));

    [Fact]
    public void Report_missing_translations_per_culture()
    {
        // Informational only: a missing key falls back to English at runtime.
        foreach (var file in Directory.GetFiles(PropertiesDir, "Lang.*.resx").Order())
        {
            var missing = BaseKeys.Except(ReadKeys(file)).Order().ToList();
            output.WriteLine($"{Path.GetFileName(file)}: {missing.Count} missing{(missing.Count > 0 ? " -> " + string.Join(", ", missing) : "")}");
        }
    }

    private static HashSet<string> ReadKeys(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal)!;
}
