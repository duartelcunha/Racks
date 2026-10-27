using System.IO;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

/// <summary>
/// Presentation code (windows, dialogs, App, views, view models, converters) reaches the Windows shell for change
/// notifications, desktop icons and the workspace folder ONLY through <c>ShellOperationGateway</c>
/// (Racks/Infrastructure/Shell). This guard allows zero references to the helpers behind it:
/// <c>NotifyShellMove</c>, <c>NotifyShellUpdateDir</c>, <c>SHChangeNotify</c>, <c>DesktopIconPositioner</c>,
/// <c>DesktopIcons.</c> and <c>DesktopIconManager</c> (Initialize and RacksWorkspacePath). There is no baseline
/// file: every site could move, so the ratchet is at zero. If a site ever cannot move, add a baseline JSON with
/// exact per-file counts like fs-allowlist.json instead of loosening a regex.
///
/// Which files count as presentation (under Racks/): MainWindow*.cs, RackWindow*.cs, SettingsWindow*.cs,
/// RackSettingsDialog*.cs, AboutWindow*.cs, HelpWindow*.cs, QuickFinderWindow*.cs, App.xaml.cs, and everything
/// under Views/, ViewModels/ and Converters/. Comments are removed before matching; strings are not blanked, so
/// a string that contains a pattern counts too (fails closed).
///
/// What it cannot see (known blind spots; a reviewer must read new presentation code): calls through reflection,
/// raw Win32 shell P/Invokes declared inside presentation (for example a new <c>[DllImport("shell32")]</c> or
/// <c>SHFileOperation</c>; only <c>SHChangeNotify</c> by name is matched), <c>using static</c> or aliases for
/// the gateway's helper classes (the namespace-qualified names would still be matched if used, an alias to a
/// renamed type would not), shell COM objects, <c>Process.Start</c> of Explorer, XAML-only references, code in
/// other projects, and presentation-like code that lives outside the file list above (for example a new folder).
/// The shell context menu, window geometry/style Win32 calls, hotkeys and the tray are deliberately allowed to
/// stay in presentation.
/// </summary>
public class PresentationBoundaryTests
{
    private static readonly string Root = RepoPaths.Root;

    private static readonly string[] RootFilePatterns =
    [
        "MainWindow*.cs", "RackWindow*.cs", "SettingsWindow*.cs", "RackSettingsDialog*.cs",
        "AboutWindow*.cs", "HelpWindow*.cs", "QuickFinderWindow*.cs", "App.xaml.cs",
    ];
    private static readonly string[] PresentationFolders = ["Views", "ViewModels", "Converters"];

    private static readonly Regex Forbidden = new(
        @"\b(NotifyShellMove|NotifyShellUpdateDir|SHChangeNotify|DesktopIconPositioner|DesktopIconManager)\b|\bDesktopIcons\s*\.",
        RegexOptions.Compiled);

    // String and char literals are matched first so "//" inside them is not taken for a comment.
    private static readonly Regex CommentOrLiteral = new(
        "(?<lit>@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])+')|(?<com>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)",
        RegexOptions.Compiled);

    internal static string StripComments(string code) =>
        CommentOrLiteral.Replace(code, m => m.Groups["com"].Success ? " " : m.Value);

    internal static int CountForbidden(string code) => Forbidden.Matches(StripComments(code)).Count;

    // ---- the guard -------------------------------------------------------------------------

    [Fact]
    public void Presentation_code_does_not_touch_the_shell_helpers_directly()
    {
        var problems = PresentationFiles()
            .Select(f => (f.Path, Count: CountForbidden(f.Code)))
            .Where(f => f.Count > 0)
            .Select(f => $"{f.Path}: {f.Count} direct reference(s)")
            .ToList();
        Assert.True(problems.Count == 0,
            "Presentation must call Racks.Infrastructure.Shell.ShellOperationGateway.Default instead of " +
            "Interop.NotifyShell*, DesktopIcons, DesktopIconPositioner or DesktopIconManager:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void The_scan_covers_the_presentation_files_it_is_meant_to_cover()
    {
        // A guard that silently scans nothing would stay green forever.
        var paths = PresentationFiles().Select(f => f.Path).ToList();
        Assert.Contains("Racks/MainWindow.xaml.cs", paths);
        Assert.Contains("Racks/RackWindow.DragDrop.cs", paths);
        Assert.Contains("Racks/RackWindow.Menu.cs", paths);
        Assert.Contains("Racks/App.xaml.cs", paths);
        Assert.Contains(paths, p => p.StartsWith("Racks/Views/", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.StartsWith("Racks/ViewModels/", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Root, "Racks", "Infrastructure", "Shell", "ShellOperationGateway.cs")));
    }

    // ---- MainWindow in particular (P7) ----------------------------------------------------------

    // MainWindow only wires and shows. The work it used to do lives in Racks/UseCases (Magic Organize, hot corner,
    // double-click hide, hotkeys, tray switches, update glue, display-change debounce) and behind Infrastructure/Shell
    // (H.Hooks, RegisterHotKey, WindowFromPoint, UI Automation, the cursor and the screens).
    private static readonly (string Why, Regex Pattern)[] MainWindowForbidden =
    [
        ("raw file or folder access", new Regex(@"\b(File|Directory|FileInfo|DirectoryInfo)\s*\.", RegexOptions.Compiled)),
        ("file broker (Magic Organize owns it)", new Regex(@"\b(FileOperationBroker|SafeMove|SafeDelete)\b", RegexOptions.Compiled)),
        ("registry", new Regex(@"\b(RegistryKey|RegistryHelper)\b|\bRegistry\s*\.\s*(CurrentUser|LocalMachine)", RegexOptions.Compiled)),
        ("updater internals", new Regex(@"\bUpdater\s*\.|\b(UpdateManifest|UpdateTrust)\b", RegexOptions.Compiled)),
        ("mouse hook / UI Automation / hit testing", new Regex(@"\bH\.Hooks\b|\b(LowLevelMouseHook|AutomationElement|WindowFromPoint)\b", RegexOptions.Compiled)),
        ("hotkey Win32 calls", new Regex(@"\b(Un)?[Rr]egisterHotKey\b", RegexOptions.Compiled)),
        ("polling the pointer or the screens", new Regex(@"\bCursor\s*\.\s*Position\b|\bScreen\s*\.\s*PrimaryScreen\b|\bGetCursorPos\b", RegexOptions.Compiled)),
        ("shell helpers behind the gateway", Forbidden),
    ];

    [Fact]
    public void MainWindow_delegates_instead_of_doing_the_work()
    {
        string code = StripComments(File.ReadAllText(Path.Combine(Root, "Racks", "MainWindow.xaml.cs")));
        var problems = MainWindowForbidden
            .Select(f => (f.Why, Count: f.Pattern.Matches(code).Count))
            .Where(f => f.Count > 0)
            .Select(f => $"{f.Why}: {f.Count} reference(s)")
            .ToList();
        Assert.True(problems.Count == 0,
            "MainWindow.xaml.cs must hand this to a class under Racks/UseCases or Racks/Infrastructure:\n" + string.Join("\n", problems));

        // The one timer left is the tray-icon retry; polling (the hot corner) is a service with an injected scheduler.
        Assert.True(Regex.Matches(code, @"\bDispatcherTimer\b").Count <= 1, "MainWindow may own at most the tray retry timer.");
    }

    [Fact]
    public void The_MainWindow_scanner_catches_what_it_claims_to()
    {
        string Hit(string snippet) => string.Join("|", MainWindowForbidden.Where(f => f.Pattern.IsMatch(StripComments(snippet))).Select(f => f.Why));
        Assert.Contains("raw file", Hit("File.Exists(p);"));
        Assert.Contains("registry", Hit("Registry.CurrentUser.OpenSubKey(k);"));
        Assert.Contains("updater", Hit("await Updater.CheckUpdateAsync(u, true);"));
        Assert.Contains("mouse hook", Hit("new LowLevelMouseHook();"));
        Assert.Contains("hotkey", Hit("Interop.RegisterHotKey(h, 1, 2, 3);"));
        Assert.Contains("polling", Hit("var p = System.Windows.Forms.Cursor.Position;"));
        Assert.Equal("", Hit("// File.Exists(p); Updater.Check()"));                      // comments are ignored
        Assert.Equal("", Hit("_hotkeys.RegisterAll(h); _updates.CheckNowAsync();"));        // the use cases' own names are fine
    }

    // ---- the scanner itself ------------------------------------------------------------------

    [Theory]
    [InlineData("Util.Interop.NotifyShellMove(a, b, true);", 1)]
    [InlineData("Interop.NotifyShellUpdateDir(p);", 1)]
    [InlineData("Util.DesktopIconPositioner.ArrangeInGrid(list);", 1)]
    [InlineData("DesktopIcons.SetHidden(true); DesktopIcons .EnsureDefViewVisible();", 2)]
    [InlineData("Racks.Core.DesktopIconManager.Initialize(); var p = DesktopIconManager.RacksWorkspacePath;", 2)]
    [InlineData("SHChangeNotify(0, 0, IntPtr.Zero, IntPtr.Zero);", 1)]
    [InlineData("Interop.NotifyShellMove(\n    a,\n    b, false);", 1)]                        // call split over lines
    [InlineData("// Util.Interop.NotifyShellMove(a, b, true);", 0)]                              // comments are ignored
    [InlineData("/* DesktopIcons.SetHidden(true); */ int a;", 0)]
    [InlineData("/// <see cref=\"DesktopIconManager\"/>", 0)]
    [InlineData("var u = \"http://x\"; DesktopIcons.SetHidden(false);", 1)]                      // // in a string is not a comment
    [InlineData("var s = \"DesktopIcons.SetHidden\";", 1)]                                       // strings fail closed
    [InlineData("HideDesktopIcons.IsChecked = true; ShowDesktopIcons.Foo();", 0)]                // word boundary
    [InlineData("MyDesktopIconManager.X(); NotifyShellMoveAll();", 0)]
    [InlineData("ShellOperationGateway.Default.NotifyItemMoved(a, b, true);", 0)]                // the gateway is what we want
    [InlineData("ShellOperationGateway.Default.SetDesktopIconsHidden(true);", 0)]
    public void The_scanner_counts_what_it_claims_to(string code, int expected)
        => Assert.Equal(expected, CountForbidden(code));

    // ---- helpers ---------------------------------------------------------------------------

    private static List<(string Path, string Code)>? _files;

    internal static List<(string Path, string Code)> PresentationFiles() => _files ??= ReadFiles().ToList();

    private static IEnumerable<(string Path, string Code)> ReadFiles()
    {
        string app = Path.Combine(Root, "Racks");
        var files = RootFilePatterns.SelectMany(p => Directory.EnumerateFiles(app, p, SearchOption.TopDirectoryOnly))
            .Concat(PresentationFolders
                .Select(f => Path.Combine(app, f))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)))
            .Distinct();
        foreach (var file in files)
        {
            string rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            yield return (rel, File.ReadAllText(file));
        }
    }
}
