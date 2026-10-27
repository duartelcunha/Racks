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

    // MainWindow is the hidden host window: tray, hotkeys, IPC and delegation to Racks/UseCases (P7). It must not grow back,
    // and a MainWindow.*.cs partial must not become the place where the growth hides (RackWindow's split taught us that).
    [Fact]
    public void MainWindow_code_behind_does_not_grow_and_is_one_file()
    {
        var parts = Directory.EnumerateFiles(Path.Combine(Root, "Racks"), "MainWindow*.cs").Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "MainWindow.xaml.cs" }, parts);

        var lines = File.ReadAllLines(Path.Combine(Root, "Racks", "MainWindow.xaml.cs"));
        Assert.True(lines.Length <= Baseline["mainWindowLines"],
            $"MainWindow.xaml.cs has {lines.Length} lines, baseline is {Baseline["mainWindowLines"]}. Put the logic in a class under Racks/UseCases instead.");

        // Longest method with a block body at class level (8 spaces: "name(...)" on one line, "{" on the next, "}" at the same indent).
        int longest = 0;
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"^ {8}\S") || !Regex.IsMatch(lines[i + 1], @"^ {8}\{\s*$")) continue;
            int j = i + 2;
            while (j < lines.Length && !Regex.IsMatch(lines[j], @"^ {8}\}\s*$")) j++;
            longest = Math.Max(longest, j - i + 1);
        }
        Assert.True(longest <= Baseline["mainWindowMethodMaxLines"],
            $"A MainWindow method has {longest} lines, the largest allowed is {Baseline["mainWindowMethodMaxLines"]}. Extract a use case.");
    }

    // App only bootstraps and exposes the WPF lifecycle (P8): the ordered startup steps, migrations, single instance, uninstall
    // mode and crash reporting live in Racks/UseCases/Startup and Racks/UseCases/Crash. It must not grow back.
    [Fact]
    public void App_code_behind_does_not_grow_and_is_one_file()
    {
        var parts = Directory.EnumerateFiles(Path.Combine(Root, "Racks"), "App*.cs").Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "App.xaml.cs" }, parts);

        // The class must not hide under another file name (any folder under Racks/, generated obj/bin output excluded).
        var elsewhere = Directory.EnumerateFiles(Path.Combine(Root, "Racks"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) != "App.xaml.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"partial\s+class\s+App"))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(elsewhere);

        int lines = File.ReadAllLines(Path.Combine(Root, "Racks", "App.xaml.cs")).Length;
        Assert.True(lines <= Baseline["appLines"],
            $"App.xaml.cs has {lines} lines, baseline is {Baseline["appLines"]}. Put the logic in a class under Racks/UseCases/Startup instead.");
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
