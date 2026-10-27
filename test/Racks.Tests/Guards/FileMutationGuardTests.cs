using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

/// <summary>
/// INV-FS-1: destructive file operations on user data go through <c>FileOperationBroker</c>. This guard is a
/// ratchet like <see cref="ArchitectureRatchetTests"/>: each file under Racks/ must contain EXACTLY the number
/// of raw file mutations recorded in fs-allowlist.json (missing = 0). Too many fails (a new raw call); too few
/// also fails, so a stale or too-high entry cannot hide a new call later. When a change removes a raw call,
/// lower its number in the same PR.
///
/// What it sees (after comments are removed, over several lines, whatever the arguments):
/// <c>File.</c>/<c>Directory.</c> Move, Delete, Copy, Replace, CreateDirectory, SetAttributes, Create,
/// CreateText, OpenWrite, CreateSymbolicLink, WriteAllText/Bytes/Lines, AppendAllText/Lines/Bytes, AppendText
/// (and *Async forms), <c>.MoveTo(</c>, <c>new FileInfo/DirectoryInfo(...).Delete/CopyTo</c>, and
/// <c>File.Open</c> / <c>new FileStream</c> whose arguments name a write intent (FileMode.Create, CreateNew,
/// Append, OpenOrCreate, Truncate, or FileAccess.Write, ReadWrite). It also bans <c>using static</c> and
/// <c>using X = ...</c> for File, Directory, FileInfo, DirectoryInfo, SafeMove and SafeDelete, and the same
/// as a Using item in a csproj, because those would hide the calls from the patterns.
///
/// What it cannot see (known blind spots, so do not rely on it for these; a reviewer must read new file code):
/// calls through a variable (<c>fi.Delete()</c>, <c>dirInfo.Create()</c>, <c>info.Attributes = ...</c>);
/// <c>File.Open</c>/<c>FileStream</c> with the mode passed in a variable; Win32 and COM calls
/// (HardlinkHelper <c>CreateHardLinkW</c>, JunctionHelper <c>DeviceIoControl</c>, ShellLinkHelper
/// <c>IPersistFile.Save</c>, <c>SHFileOperation</c> in SafeDelete); processes (JunctionHelper <c>mklink</c>
/// via cmd); the update download folder's <c>new DirectoryInfo(dir).Create(security)</c> (InstallerExecutor);
/// UninstallCleanup's <c>new DirectoryInfo(..).Attributes =</c>; shell context-menu verbs (delete/cut/paste
/// run by Explorer itself); reflection; code in other projects (tools/). <c>Stream.CopyTo</c> is
/// intentionally NOT matched (it copies bytes, not files). Strings are not blanked, so a string that contains
/// one of the patterns counts too (it fails closed).
/// </summary>
public class FileMutationGuardTests
{
    private static readonly string Root = RepoPaths.Root;

    // The gateway itself: the broker folder and the two primitives it wraps.
    private static readonly string[] GatewayFolders = ["Racks/Infrastructure/FileSystem/"];
    private static readonly string[] GatewayFiles = ["Racks/Util/SafeMove.cs", "Racks/Util/SafeDelete.cs"];

    private const string Nested = @"\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)"; // (...) with up to two levels of inner parens

    private static readonly Regex StaticCall = new(
        @"\b(File|Directory)\s*\.\s*(Move|Delete|Copy|CreateDirectory|SetAttributes|Replace|WriteAllText|WriteAllBytes|WriteAllLines|AppendAllText|AppendAllLines|AppendAllBytes|AppendText|CreateText|Create|OpenWrite|CreateSymbolicLink)(Async)?\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex MoveToCall = new(@"\.\s*MoveTo\s*\(", RegexOptions.Compiled);
    // new FileInfo(...).MoveTo(...) is already counted by MoveToCall, so it is not repeated here.
    private static readonly Regex NewInfoCall = new(
        @"new\s+(FileInfo|DirectoryInfo)\s*" + Nested + @"\s*\.\s*(Delete|CopyTo)\b", RegexOptions.Compiled);
    private static readonly Regex OpenCall = new(
        @"(\bFile\s*\.\s*Open|\bnew\s+FileStream)\s*" + Nested, RegexOptions.Compiled);
    private static readonly Regex WriteIntent = new(
        @"FileMode\s*\.\s*(Create|CreateNew|Append|OpenOrCreate|Truncate)\b|FileAccess\s*\.\s*(Write|ReadWrite)\b", RegexOptions.Compiled);

    private static readonly Regex PrimitiveCall = new(@"\b(SafeMove|SafeDelete)\s*\.", RegexOptions.Compiled);
    private static readonly Regex StaticUsing = new(
        @"\busing\s+static\s+(?:[\w.]*\.)?(?:File|Directory|FileInfo|DirectoryInfo|SafeMove|SafeDelete)\s*;", RegexOptions.Compiled);
    // An alias that keeps the name (using File = System.IO.File;, needed to beat WPF's ambiguity) hides nothing.
    private static readonly Regex AliasUsing = new(
        @"\busing\s+(\w+)\s*=\s*(?:[\w.]*\.)?(File|Directory|FileInfo|DirectoryInfo|SafeMove|SafeDelete)\s*;", RegexOptions.Compiled);
    // Attribute order does not matter to MSBuild, so look ahead for both parts inside one <Using .../> element.
    private static readonly Regex HidingProjectUsing = new(
        @"<Using\b(?=[^>]*\bInclude\s*=\s*""[^""]*\b(?:File|Directory|FileInfo|DirectoryInfo|SafeMove|SafeDelete)"")(?=[^>]*\b(?:Static|Alias)\s*=)[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // String and char literals are matched first so "//" inside them is not taken for a comment.
    private static readonly Regex CommentOrLiteral = new(
        "(?<lit>@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])+')|(?<com>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)",
        RegexOptions.Compiled);

    internal static string StripComments(string code) =>
        CommentOrLiteral.Replace(code, m => m.Groups["com"].Success ? " " : m.Value);

    internal static int CountMutations(string code)
    {
        string c = StripComments(code);
        int opens = OpenCall.Matches(c).Count(m => WriteIntent.IsMatch(m.Value));
        return StaticCall.Matches(c).Count + MoveToCall.Matches(c).Count + NewInfoCall.Matches(c).Count + opens;
    }

    internal static int CountPrimitiveReferences(string code) => PrimitiveCall.Matches(StripComments(code)).Count;

    internal static int CountHidingUsings(string code)
    {
        string c = StripComments(code);
        return StaticUsing.Matches(c).Count + AliasUsing.Matches(c).Count(m => m.Groups[1].Value != m.Groups[2].Value);
    }

    // ---- the guard -------------------------------------------------------------------------

    [Fact]
    public void Raw_file_mutations_outside_the_gateway_match_the_allowlist_exactly()
    {
        var allowed = LoadAllowlist().RawMutations.ToDictionary(e => e.Key, e => e.Value.Count);
        var problems = new List<string>();
        foreach (var path in allowed.Keys.Union(SourceFiles().Select(f => f.Path)).Where(p => !IsGateway(p)))
        {
            int count = SourceFiles().Where(f => f.Path == path).Select(f => CountMutations(f.Code)).FirstOrDefault();
            allowed.TryGetValue(path, out int limit);
            if (count > limit)
                problems.Add($"{path}: {count} raw file mutation(s), allowed {limit}: route the new call through FileOperationBroker");
            else if (count < limit)
                problems.Add($"{path}: {count} raw file mutation(s), allowlist says {limit}: lower the entry (the list only shrinks)");
        }
        Assert.True(problems.Count == 0,
            "Raw File/Directory mutations outside the gateway (Racks/Infrastructure/FileSystem) must equal " +
            "Guards/fs-allowlist.json:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Nothing_outside_the_gateway_references_the_SafeMove_and_SafeDelete_primitives()
    {
        var allowed = LoadAllowlist().PrimitiveReferences.ToDictionary(e => e.Key, e => e.Value.Count);
        var problems = new List<string>();
        foreach (var (path, code) in SourceFiles().Where(f => !IsGateway(f.Path)))
        {
            int count = CountPrimitiveReferences(code);
            allowed.TryGetValue(path, out int limit);
            if (count != limit) problems.Add($"{path}: {count} reference(s), allowlist says {limit}");
        }
        foreach (var stale in allowed.Keys.Where(k => SourceFiles().All(f => f.Path != k)))
            problems.Add($"{stale}: listed but not found");
        Assert.True(problems.Count == 0,
            "SafeMove/SafeDelete are internal primitives of FileOperationBroker. Call the broker instead:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void No_using_static_or_alias_hides_File_Directory_SafeMove_or_SafeDelete()
    {
        var offenders = SourceFiles().Where(f => CountHidingUsings(f.Code) > 0).Select(f => f.Path).ToList();
        foreach (var proj in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Root, "Directory.Build.*", SearchOption.AllDirectories))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}")))
            if (HidingProjectUsing.IsMatch(File.ReadAllText(proj)))
                offenders.Add(Path.GetRelativePath(Root, proj).Replace('\\', '/'));
        Assert.True(offenders.Count == 0,
            "These files import File/Directory/SafeMove/SafeDelete statically or under an alias, which hides raw calls from the guard:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void Allowlist_entries_point_at_existing_files_and_say_why()
    {
        var list = LoadAllowlist();
        foreach (var (section, entries) in new[] { ("rawMutations", list.RawMutations), ("primitiveReferences", list.PrimitiveReferences) })
        {
            foreach (var (path, entry) in entries)
            {
                Assert.True(File.Exists(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar))), $"{section}: {path} does not exist (renamed or deleted? remove the entry)");
                Assert.True(entry.Count > 0, $"{section}: {path} has count {entry.Count}; delete the entry instead");
                Assert.False(string.IsNullOrWhiteSpace(entry.Why), $"{section}: {path} needs a one-line justification in \"why\"");
                Assert.False(IsGateway(path), $"{section}: {path} is part of the gateway and needs no entry");
            }
        }
    }

    // ---- the scanner itself ------------------------------------------------------------------

    [Theory]
    [InlineData("File.Delete(path);", 1)]
    [InlineData("System.IO.Directory.Delete(dir, recursive: true);", 1)]
    [InlineData("Directory.Delete(dir, flag);", 1)]                       // non-literal argument
    [InlineData("File.Move(\n    a,\n    b);", 1)]                         // call split over lines
    [InlineData("File . Copy (a, b, true)", 1)]
    [InlineData("await File.WriteAllTextAsync(p, text);", 1)]
    [InlineData("File.AppendAllText(p, \"x\"); File.SetAttributes(p, A);", 2)]
    [InlineData("File.AppendAllLines(p, l); File.AppendAllBytes(p, b); File.AppendText(p);", 3)]
    [InlineData("File.Create(p); File.CreateText(p); File.OpenWrite(p);", 3)]
    [InlineData("File.CreateSymbolicLink(link, target); Directory.CreateSymbolicLink(l, t);", 2)]
    [InlineData("File.Open(p, FileMode.Create)", 1)]                       // write intent
    [InlineData("File.Open(p, FileMode.Open, FileAccess.ReadWrite)", 1)]
    [InlineData("new FileStream(Path.Combine(a, b), FileMode.CreateNew, FileAccess.Write)", 1)]
    [InlineData("new FileStream(p, FileMode.Open, FileAccess.Read)", 0)]  // read only
    [InlineData("File.Open(p, FileMode.Open)", 0)]
    [InlineData("info.MoveTo(dest);", 1)]
    [InlineData("new FileInfo(Path.Combine(a, b)).Delete();", 1)]
    [InlineData("new DirectoryInfo(path).MoveTo(dest);", 1)]
    [InlineData("// File.Delete(path);", 0)]                               // comments are ignored
    [InlineData("/* Directory.Delete(x); */ int a;", 0)]
    [InlineData("/// <summary>File.Move(a, b)</summary>", 0)]
    [InlineData("var u = \"http://x\"; File.Delete(p);", 1)]               // // inside a string is not a comment
    [InlineData("source.CopyTo(destinationStream);", 0)]                    // Stream.CopyTo is not a file copy
    [InlineData("File.Exists(p); Directory.EnumerateFiles(d); File.ReadAllText(p);", 0)]
    [InlineData("MyFile.Delete(p); Profile.Move(p);", 0)]                   // word boundary
    public void The_scanner_counts_what_it_claims_to(string code, int expected)
        => Assert.Equal(expected, CountMutations(code));

    [Theory]
    [InlineData("SafeMove.TryMove(a, b, out _);", 1)]
    [InlineData("Util.SafeDelete.ToRecycleBin(p); SafeMove.Result.Moved", 2)]
    [InlineData("// SafeMove.TryMove(a, b, out _);", 0)]
    [InlineData("MySafeMove.Thing", 0)]
    public void The_primitive_scanner_counts_what_it_claims_to(string code, int expected)
        => Assert.Equal(expected, CountPrimitiveReferences(code));

    [Theory]
    [InlineData("using static System.IO.File;", 1)]
    [InlineData("using static System.IO.Directory;", 1)]
    [InlineData("global using static System.IO.File;", 1)]
    [InlineData("using F = System.IO.File;", 1)]
    [InlineData("using D = Directory;", 1)]
    [InlineData("using static Racks.Util.SafeMove;", 1)]
    [InlineData("using M = Racks.Util.SafeDelete;", 1)]
    [InlineData("using static Racks.Util.Interop;", 0)]
    [InlineData("using System.IO;", 0)]
    [InlineData("using Path = System.IO.Path;", 0)]
    [InlineData("using File = System.IO.File;", 0)]                          // keeps the name: File.Delete( is still seen
    [InlineData("// using static System.IO.File;", 0)]
    public void The_using_scanner_counts_what_it_claims_to(string code, int expected)
        => Assert.Equal(expected, CountHidingUsings(code));

    [Theory]
    [InlineData("<Using Include=\"System.IO.File\" Static=\"True\" />", true)]
    [InlineData("<Using Static=\"true\" Include=\"System.IO.Directory\" />", true)]
    [InlineData("<Using Include=\"System.IO.File\" Alias=\"F\" />", true)]
    [InlineData("<Using Alias=\"F\" Include=\"System.IO.File\" />", true)]
    [InlineData("<Using Include=\"System.Linq\" />", false)]
    [InlineData("<InternalsVisibleTo Include=\"Racks.Tests\" />", false)]
    public void The_project_using_scanner_counts_what_it_claims_to(string xml, bool expected)
        => Assert.Equal(expected, HidingProjectUsing.IsMatch(xml));

    // ---- helpers ---------------------------------------------------------------------------

    private static bool IsGateway(string relPath) =>
        GatewayFiles.Contains(relPath) || GatewayFolders.Any(f => relPath.StartsWith(f, StringComparison.Ordinal));

    private static List<(string Path, string Code)>? _sources;

    private static List<(string Path, string Code)> SourceFiles() => _sources ??= ReadSources().ToList();

    private static IEnumerable<(string Path, string Code)> ReadSources()
    {
        string app = Path.Combine(Root, "Racks");
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            yield return (rel, File.ReadAllText(file));
        }
    }

    private sealed record Entry(int Count, string Why);

    private sealed record Allowlist(Dictionary<string, Entry> RawMutations, Dictionary<string, Entry> PrimitiveReferences);

    private static Allowlist LoadAllowlist()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var list = JsonSerializer.Deserialize<Allowlist>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guards", "fs-allowlist.json")), options)!;
        return new Allowlist(list.RawMutations ?? [], list.PrimitiveReferences ?? []);
    }
}
