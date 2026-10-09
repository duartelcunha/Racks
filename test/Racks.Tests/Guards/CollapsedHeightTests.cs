using System.IO;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

// With the drop shadow on, a rack's background sits inside a 10 px margin. Collapsing it to the bare
// title-bar height left a 10 px sliver with the name clipped off. Every collapse must use
// CollapsedHeight (title bar + that margin).
public class CollapsedHeightTests
{
    [Fact]
    public void Racks_never_collapse_to_the_bare_title_bar_height()
    {
        var offenders = Directory.EnumerateFiles(RepoPaths.App(), "RackWindow*.cs")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (file: Path.GetFileName(f), line, n: i + 1)))
            .Where(x => Regex.IsMatch(x.line, @"AnimateWindowHeight\(\s*titleBar\.Height|this\.Height\s*=\s*titleBar\.Height|ActualHeight\s*!=\s*titleBar\.Height"))
            .Select(x => $"{x.file}:{x.n}")
            .ToList();
        Assert.True(offenders.Count == 0, "Use CollapsedHeight instead of titleBar.Height at: " + string.Join(", ", offenders));
    }
}
