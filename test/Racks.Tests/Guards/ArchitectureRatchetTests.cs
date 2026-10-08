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
    private static readonly string Root = FindRepoRoot();
    private static readonly Dictionary<string, int> Baseline = LoadBaseline();

    [Fact]
    public void RackWindow_code_behind_does_not_grow()
    {
        var path = Path.Combine(Root, "Racks", "RackWindow.xaml.cs");
        int lines = File.ReadAllLines(path).Length;
        Assert.True(lines <= Baseline["rackWindowLines"],
            $"RackWindow.xaml.cs has {lines} lines, baseline is {Baseline["rackWindowLines"]}. Extract code instead of adding to it.");
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Racks.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racks.sln not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, int> LoadBaseline() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guards", "baseline.json")))!;
}
