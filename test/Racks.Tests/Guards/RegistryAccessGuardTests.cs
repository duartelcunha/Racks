using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

/// <summary>
/// INV-REG-1 (app-wide values): presentation code does not read or write the registry itself. App-wide switches
/// and one-shot markers go through <c>Racks.Infrastructure.Registry.AppSettings</c> and <c>MigrationMarkers</c>.
/// This guard is a ratchet like <see cref="FileMutationGuardTests"/>: each presentation file (the same list as
/// <see cref="PresentationBoundaryTests"/>) must contain EXACTLY the number of registry references recorded in
/// registry-presentation-allowlist.json (missing = 0). Too many fails (a new direct access: add a setting to
/// <c>AppSettings</c>); too few also fails, so a stale entry cannot hide a new access later. Lower the number in
/// the PR that removes a site. What is left is per-rack persistence (P6) and the rack/App bootstrap (P8).
///
/// What it counts (comments removed, strings not blanked so a string with a pattern fails closed):
/// <c>RegistryHelper</c>, <c>RegistryKey</c>, <c>RegistryValueKind</c>, <c>RegistryHive</c>, <c>RegistryView</c>,
/// <c>ReadKeyValueRoot*</c>, <c>WriteToRegistry*</c>, <c>WriteMultiLine*</c>, <c>WriteIntArrayToRegistry</c>,
/// <c>KeyExists*</c>, <c>AddToAutoRun</c>, <c>RemoveFromAutoRun</c> and <c>Registry.CurrentUser / LocalMachine /
/// ClassesRoot / Users / CurrentConfig / OpenBaseKey</c>.
///
/// What it cannot see (a reviewer must read new presentation code): <c>reg.exe</c> and PowerShell run as processes
/// (SettingsWindow's "export settings" does this on purpose), reflection, other helpers that open the registry for
/// presentation (for example <c>RackLayoutIO</c>, which is not a presentation file), XAML-only references, code in
/// other projects, and presentation-like code outside the file list of <see cref="PresentationBoundaryTests"/>.
/// </summary>
public class RegistryAccessGuardTests
{
    private static readonly string Root = RepoPaths.Root;

    private static readonly Regex RegistryReference = new(
        @"\b(RegistryHelper|RegistryKey|RegistryValueKind|RegistryHive|RegistryView|ReadKeyValueRoot\w*|WriteToRegistry\w*|WriteMultiLine\w*|WriteIntArrayToRegistry|KeyExists\w*|AddToAutoRun|RemoveFromAutoRun)\b"
        + @"|\bRegistry\s*\.\s*(CurrentUser|LocalMachine|ClassesRoot|Users|CurrentConfig|OpenBaseKey)\b",
        RegexOptions.Compiled);

    internal static int CountRegistryReferences(string code) =>
        RegistryReference.Matches(PresentationBoundaryTests.StripComments(code)).Count;

    // ---- the guard -------------------------------------------------------------------------

    [Fact]
    public void Registry_references_in_presentation_match_the_allowlist_exactly()
    {
        var allowed = LoadAllowlist().ToDictionary(e => e.Key, e => e.Value.Count);
        var files = PresentationBoundaryTests.PresentationFiles();
        var problems = new List<string>();
        foreach (var path in allowed.Keys.Union(files.Select(f => f.Path)))
        {
            int count = files.Where(f => f.Path == path).Select(f => CountRegistryReferences(f.Code)).FirstOrDefault();
            allowed.TryGetValue(path, out int limit);
            if (count > limit)
                problems.Add($"{path}: {count} registry reference(s), allowed {limit}: add the value to AppSettings/MigrationMarkers instead");
            else if (count < limit)
                problems.Add($"{path}: {count} registry reference(s), allowlist says {limit}: lower the entry (the list only shrinks)");
        }
        Assert.True(problems.Count == 0,
            "Presentation files must not touch the registry directly. The counts must equal Guards/registry-presentation-allowlist.json:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void Allowlist_entries_point_at_presentation_files_and_say_why()
    {
        var paths = PresentationBoundaryTests.PresentationFiles().Select(f => f.Path).ToHashSet();
        foreach (var (path, entry) in LoadAllowlist())
        {
            Assert.True(paths.Contains(path), $"{path} is not a presentation file that exists (renamed or deleted? remove the entry)");
            Assert.True(entry.Count > 0, $"{path} has count {entry.Count}; delete the entry instead");
            Assert.False(string.IsNullOrWhiteSpace(entry.Why), $"{path} needs a one-line justification in \"why\"");
        }
    }

    [Fact]
    public void The_settings_layer_is_not_counted_as_presentation()
    {
        // A guard that scanned the wrong files would stay green forever: the layer itself must use the registry,
        // and the call sites that were moved there must still be among the files scanned.
        var paths = PresentationBoundaryTests.PresentationFiles().Select(f => f.Path).ToList();
        Assert.DoesNotContain(paths, p => p.StartsWith("Racks/Infrastructure/", StringComparison.Ordinal));
        Assert.Contains("Racks/SettingsWindow.xaml.cs", paths);
        Assert.Contains("Racks/MainWindow.xaml.cs", paths);
        Assert.True(File.Exists(Path.Combine(Root, "Racks", "Infrastructure", "Registry", "AppSettings.cs")));
    }

    // ---- the scanner itself ------------------------------------------------------------------

    [Theory]
    [InlineData("_controller.reg.WriteToRegistryRoot(\"X\", true);", 1)]
    [InlineData("if (reg.KeyExistsRoot(\"X\")) v = reg.ReadKeyValueRoot(\"X\") as bool?;", 2)]
    [InlineData("var v = helper.ReadKeyValueRootInt(\"X\");", 1)]
    [InlineData("reg.WriteMultiLineRegistry(n, l, i); reg.WriteIntArrayToRegistry(n, a, i); reg.WriteToRegistry(n, v, i);", 3)]
    [InlineData("reg.AddToAutoRun(a, p); reg.RemoveFromAutoRun(a);", 2)]
    [InlineData("var h = new RegistryHelper(InstanceController.appName);", 1)]
    [InlineData("using RegistryKey? key = Registry.CurrentUser.OpenSubKey(p, writable: true);", 2)]
    [InlineData("Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(k, false);", 1)]
    [InlineData("Registry . LocalMachine.OpenSubKey(k); Registry.ClassesRoot.X(); Registry.Users.Y();", 3)]
    [InlineData("RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);", 3)]
    [InlineData("key.SetValue(\"x\", 1, RegistryValueKind.DWord);", 1)]
    [InlineData("Registry.CurrentUser\n    .OpenSubKey(p);", 1)]                              // split over lines
    [InlineData("// reg.WriteToRegistryRoot(\"X\", true);", 0)]                                 // comments are ignored
    [InlineData("/* Registry.CurrentUser.DeleteSubKeyTree(k); */ int a;", 0)]
    [InlineData("/// <see cref=\"RegistryHelper\"/>", 0)]
    [InlineData("var u = \"http://x\"; Registry.CurrentUser.Foo();", 1)]                        // // in a string is not a comment
    [InlineData("var s = \"RegistryKey\";", 1)]                                                 // strings fail closed
    [InlineData("MyRegistryKey k; ThisKeyExistsHere(); MyRegistryHelper h;", 0)]                // word boundary
    [InlineData("AppSettings.Default.TrySetIcePhysics(true); MigrationMarkers.Default.IsSet(n);", 0)]  // the layer we want
    [InlineData("Racks.Infrastructure.Registry.AppSettings.Default.StartOnLogin", 0)]
    [InlineData("using Microsoft.Win32;", 0)]
    public void The_scanner_counts_what_it_claims_to(string code, int expected)
        => Assert.Equal(expected, CountRegistryReferences(code));

    // ---- helpers ---------------------------------------------------------------------------

    private sealed record Entry(int Count, string Why);

    private static Dictionary<string, Entry> LoadAllowlist()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var list = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Entry>>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guards", "registry-presentation-allowlist.json")), options)!;
        return list["presentationReferences"];
    }
}
