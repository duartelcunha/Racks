using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

/// <summary>
/// Ratchets: these numbers may only go down. When a change lowers one, update baseline.json
/// in the same PR so the gain is locked in.
/// </summary>
public class ArchitectureRatchetTests
{
    private static readonly string Root = RepoPaths.Root;
    private static readonly Dictionary<string, int> Baseline = LoadBaseline();

    [Fact]
    public void RackWindow_code_behind_does_not_grow()
    {
        var path = Path.Combine(Root, "Racks", "RackWindow.xaml.cs");
        int lines = File.ReadAllLines(path).Length;
        Assert.True(lines <= Baseline["rackWindowLines"],
            $"RackWindow.xaml.cs has {lines} lines, baseline is {Baseline["rackWindowLines"]}. Extract code instead of adding to it.");
    }

    // RackWindow is split into partial files (RackWindow.*.cs). The split must not become a place to
    // hide growth: no single part may grow past its baseline, and the parts together may not grow.
    [Fact]
    public void RackWindow_parts_do_not_grow()
    {
        var parts = Directory.EnumerateFiles(Path.Combine(Root, "Racks"), "RackWindow*.cs")
            .ToDictionary(Path.GetFileName, f => File.ReadAllLines(f).Length);
        Assert.True(parts.Count > 1, "expected RackWindow.xaml.cs plus partial files");

        var biggest = parts.MaxBy(p => p.Value);
        Assert.True(biggest.Value <= Baseline["rackWindowPartMaxLines"],
            $"{biggest.Key} has {biggest.Value} lines, the largest allowed part is {Baseline["rackWindowPartMaxLines"]}. Extract a class instead.");

        int total = parts.Values.Sum();
        Assert.True(total <= Baseline["rackWindowTotalLines"],
            $"RackWindow*.cs total {total} lines, baseline is {Baseline["rackWindowTotalLines"]}. Move logic out of the window instead of adding to it.");
    }

    [Fact]
    public void Static_service_locator_usage_does_not_grow()
    {
        int count = SourceFiles("*.cs").Sum(f => Regex.Matches(File.ReadAllText(f), @"MainWindow\._controller").Count);
        Assert.True(count <= Baseline["controllerUsages"],
            $"MainWindow._controller is used {count} times, baseline is {Baseline["controllerUsages"]}. Pass dependencies in instead.");
    }

    [Fact]
    public void Hardcoded_xaml_literals_do_not_grow()
    {
        var literal = new Regex(@"\s(Content|Header|Text|ToolTip|Title)=""[^""{]");
        int count = SourceFiles("*.xaml").Sum(f => literal.Matches(File.ReadAllText(f)).Count);
        Assert.True(count <= Baseline["xamlLiterals"],
            $"Found {count} hardcoded XAML literals, baseline is {Baseline["xamlLiterals"]}. Use {{x:Static prop:Lang.Key}}.");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "Racks"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, int> LoadBaseline() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guards", "baseline.json")))!;
}
