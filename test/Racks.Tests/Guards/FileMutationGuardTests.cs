using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

/// <summary>
/// INV-FS-1: destructive file operations on user data go through <c>FileOperationBroker</c>. This guard is a
/// ratchet like <see cref="ArchitectureRatchetTests"/>: each file under Racks/ may contain at most the number
/// of raw file mutations recorded in fs-allowlist.json (missing = 0), and the numbers may only go down.
/// When a change removes a raw call, lower its number in the same PR.
///
/// What it sees: <c>File.</c>/<c>Directory.</c> Move, Delete, Copy, CreateDirectory, SetAttributes, Replace,
/// WriteAllText/Bytes/Lines and AppendAllText (and their *Async forms), <c>.MoveTo(</c>, and
/// <c>new FileInfo/DirectoryInfo(...).Delete/CopyTo/MoveTo</c>, after comments are removed, over several
/// lines and whatever the arguments are.
///
/// What it cannot see (so do not rely on it for these): calls through a variable (<c>fi.Delete()</c>,
/// <c>dirInfo.Create()</c>, <c>info.Attributes = ...</c>), <c>File.Create/Open/OpenWrite</c> and
/// <c>new FileStream(..., FileMode.Create)</c>, Win32 or shell calls (<c>SHFileOperation</c>,
/// <c>DeleteFile</c>), <c>Process.Start("cmd", ...)</c>, reflection, and code in other projects (tools/).
/// <c>Stream.CopyTo</c> is intentionally NOT matched (it copies bytes, not files). Strings are not
/// blanked, so a string that contains one of the patterns counts too (it fails closed). A reviewer still has
/// to read new file code; the guard only stops the easy, silent way of bypassing the gateway.
/// </summary>
public class FileMutationGuardTests
{
    private static readonly string Root = RepoPaths.Root;

    // The gateway itself: the broker folder and the two primitives it wraps.
    private static readonly string[] GatewayFolders = ["Racks/Infrastructure/FileSystem/"];
    private static readonly string[] GatewayFiles = ["Racks/Util/SafeMove.cs", "Racks/Util/SafeDelete.cs"];

    private const string Nested = @"\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)"; // (...) with up to two levels of inner parens

    private static readonly Regex StaticCall = new(
        @"\b(File|Directory)\s*\.\s*(Move|Delete|Copy|CreateDirectory|SetAttributes|Replace|WriteAllText|WriteAllBytes|WriteAllLines|AppendAllText)(Async)?\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex MoveToCall = new(@"\.\s*MoveTo\s*\(", RegexOptions.Compiled);
    // new FileInfo(...).MoveTo(...) is already counted by MoveToCall, so it is not repeated here.
    private static readonly Regex NewInfoCall = new(
        @"new\s+(FileInfo|DirectoryInfo)\s*" + Nested + @"\s*\.\s*(Delete|CopyTo)\b", RegexOptions.Compiled);
    private static readonly Regex PrimitiveCall = new(@"\b(SafeMove|SafeDelete)\s*\.", RegexOptions.Compiled);

    // String and char literals are matched first so "//" inside them is not taken for a comment.
    private static readonly Regex CommentOrLiteral = new(
        "(?<lit>@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])+')|(?<com>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)",
        RegexOptions.Compiled);

    internal static string StripComments(string code) =>
        CommentOrLiteral.Replace(code, m => m.Groups["com"].Success ? " " : m.Value);

    internal static int CountMutations(string code)
    {
        string c = StripComments(code);
        return StaticCall.Matches(c).Count + MoveToCall.Matches(c).Count + NewInfoCall.Matches(c).Count;
    }

    internal static int CountPrimitiveReferences(string code) => PrimitiveCall.Matches(StripComments(code)).Count;

    // ---- the guard -------------------------------------------------------------------------

    [Fact]
    public void Raw_file_mutations_outside_the_gateway_do_not_exceed_the_allowlist()
    {
        var allowed = LoadAllowlist().RawMutations.ToDictionary(e => e.Key, e => e.Value.Count);
        var over = new List<string>();
        foreach (var (path, code) in SourceFiles().Where(f => !IsGateway(f.Path)))
        {
            int count = CountMutations(code);
            allowed.TryGetValue(path, out int limit);
            if (count > limit)
                over.Add($"{path}: {count} raw file mutation(s), allowed {limit}");
        }
        Assert.True(over.Count == 0,
            "Raw File/Directory mutations outside the gateway. Route them through FileOperationBroker " +
            "(Racks/Infrastructure/FileSystem) or, for a genuine exception, raise the number in " +
            "Guards/fs-allowlist.json with a justification:\n" + string.Join("\n", over));
    }

    [Fact]
    public void Nothing_outside_the_gateway_references_the_SafeMove_and_SafeDelete_primitives()
    {
        var allowed = LoadAllowlist().PrimitiveReferences.ToDictionary(e => e.Key, e => e.Value.Count);
        var over = new List<string>();
        foreach (var (path, code) in SourceFiles().Where(f => !IsGateway(f.Path)))
        {
            int count = CountPrimitiveReferences(code);
            allowed.TryGetValue(path, out int limit);
            if (count > limit)
                over.Add($"{path}: {count} reference(s), allowed {limit}");
        }
        Assert.True(over.Count == 0,
            "SafeMove/SafeDelete are internal primitives of FileOperationBroker. Call the broker instead:\n" + string.Join("\n", over));
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

    // ---- helpers ---------------------------------------------------------------------------

    private static bool IsGateway(string relPath) =>
        GatewayFiles.Contains(relPath) || GatewayFolders.Any(f => relPath.StartsWith(f, StringComparison.Ordinal));

    private static IEnumerable<(string Path, string Code)> SourceFiles()
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
