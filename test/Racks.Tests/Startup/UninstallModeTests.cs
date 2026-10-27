using System.IO;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Tests.Persistence;
using Racks.UseCases.Startup;
using Racks.Util;

namespace Racks.Tests.Startup;

[Collection("Registry")]
public sealed class UninstallModeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "racks-uninstall-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static (UninstallMode Mode, StartupHarness.FakeWatchdog Dog, List<int> Exits, List<IReadOnlyDictionary<string, string>> Titles) Create(
        CleanupReport? report = null, Func<IRackSettingsStore>? store = null, Exception? cleanupThrows = null)
    {
        var dog = new StartupHarness.FakeWatchdog();
        var exits = new List<int>();
        var titles = new List<IReadOnlyDictionary<string, string>>();
        var mode = new UninstallMode(dog, exits.Add, t =>
        {
            titles.Add(t);
            if (cleanupThrows != null) throw cleanupThrows;
            return report ?? new CleanupReport();
        }, store ?? (() => new StartupHarness.EmptyStore()));
        return (mode, dog, exits, titles);
    }

    // ---- arguments --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(new string[0], StartupMode.Normal)]
    [InlineData(new[] { "--uninstall-cleanup" }, StartupMode.UninstallCleanup)]
    [InlineData(new[] { "--uninstall-cleanup", @"C:\r.txt" }, StartupMode.UninstallCleanup)]
    [InlineData(new[] { "--uninstall-anim" }, StartupMode.UninstallAnimation)]
    [InlineData(new[] { "--other" }, StartupMode.Normal)]
    [InlineData(new[] { "x", "--uninstall-cleanup" }, StartupMode.Normal)]
    [InlineData(new[] { "--UNINSTALL-CLEANUP" }, StartupMode.Normal)]
    public void Only_the_first_argument_picks_the_mode_and_it_is_matched_exactly(string[] args, StartupMode expected)
    {
        Assert.Equal(expected, UninstallMode.ParseMode(args));
    }

    // ---- watchdog ---------------------------------------------------------------------------------------------------

    [Fact]
    public void The_cleanup_arms_a_two_minute_watchdog_that_exits_with_code_2()
    {
        var (mode, dog, exits, _) = Create();

        mode.RunCleanup(["--uninstall-cleanup"]);

        Assert.Equal([TimeSpan.FromMinutes(2)], dog.Armed);
        Assert.Empty(exits);
        dog.Fire();
        Assert.Equal([2], exits);
    }

    [Fact]
    public void The_watchdog_is_armed_before_the_cleanup_runs_so_a_hang_inside_it_is_covered()
    {
        var dog = new StartupHarness.FakeWatchdog();
        int armedWhenCleanupStarted = -1;
        var mode = new UninstallMode(dog, _ => { }, _ => { armedWhenCleanupStarted = dog.Armed.Count; return new CleanupReport(); },
            () => new StartupHarness.EmptyStore());

        mode.RunCleanup(["--uninstall-cleanup"]);

        Assert.Equal(1, armedWhenCleanupStarted);
    }

    [Fact]
    public void The_animation_mode_has_no_watchdog_and_only_marks_the_process()
    {
        var (mode, dog, _, _) = Create();
        Assert.False(mode.IsRunningForUninstaller);

        mode.MarkRunningForUninstaller();

        Assert.True(mode.IsRunningForUninstaller);
        Assert.Empty(dog.Armed);
    }

    // ---- the cleanup and its report ----------------------------------------------------------------------------------

    [Fact]
    public void The_cleanup_marks_the_process_as_the_uninstaller_first()
    {
        var (mode, _, _, _) = Create();
        mode.RunCleanup(["--uninstall-cleanup"]);
        Assert.True(mode.IsRunningForUninstaller);
    }

    [Fact]
    public void The_report_is_written_to_the_path_given_as_the_second_argument()
    {
        var report = new CleanupReport { Returned = 3 };
        string path = Path.Combine(_dir, "sub", "report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var (mode, _, _, _) = Create(report);

        var result = mode.RunCleanup(["--uninstall-cleanup", path]);

        Assert.Same(report, result);
        Assert.Equal(report.Lines(), File.ReadAllLines(path));
    }

    [Fact]
    public void Without_a_path_nothing_is_written()
    {
        var (mode, _, _, _) = Create();
        mode.RunCleanup(["--uninstall-cleanup"]);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void A_report_that_cannot_be_written_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        var (mode, _, _, _) = Create();
        mode.RunCleanup(["--uninstall-cleanup", _dir]);   // a directory, not a file: WriteTo throws, the run goes on
    }

    [Fact]
    public void A_cleanup_that_throws_becomes_a_report_with_one_error()
    {
        var (mode, _, _, _) = Create(cleanupThrows: new InvalidOperationException("shell hung up"));

        var result = mode.RunCleanup(["--uninstall-cleanup"]);

        Assert.Equal(["cleanup failed: shell hung up"], result.Errors);
    }

    // ---- rack titles -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_titles_map_comes_from_the_saved_racks_and_is_case_insensitive()
    {
        using var reg = new RackRegistryFixture();
        string folder = Path.Combine(_dir, "My Docs");
        Directory.CreateDirectory(folder);
        reg.SeedRack("A", new (string, object, RegistryValueKind)[]
        {
            ("Name", "A", RegistryValueKind.String),
            ("Folder", folder, RegistryValueKind.String),
            ("TitleText", "Work", RegistryValueKind.String),
        });
        reg.SeedRack("B", "Name", "B", RegistryValueKind.String);   // no folder, no title: not named

        var titles = RackTitles.ByFolder(reg.Repository);

        Assert.Equal("Work", titles[folder.ToUpperInvariant()]);
        Assert.DoesNotContain(titles, kv => kv.Value == "B");
    }

    [Fact]
    public void The_cleanup_receives_the_titles_of_the_store()
    {
        using var reg = new RackRegistryFixture();
        string folder = Path.Combine(_dir, "Photos");
        Directory.CreateDirectory(folder);
        reg.SeedRack("P", new (string, object, RegistryValueKind)[]
        {
            ("Name", "P", RegistryValueKind.String),
            ("Folder", folder, RegistryValueKind.String),
            ("TitleText", "Pics", RegistryValueKind.String),
        });
        var (mode, _, _, titles) = Create(store: () => reg.Repository);

        mode.RunCleanup(["--uninstall-cleanup"]);

        Assert.Equal("Pics", Assert.Single(titles)[folder]);
    }

    [Fact]
    public void A_store_that_throws_gives_an_empty_map_and_the_cleanup_still_runs()
    {
        var (mode, _, _, titles) = Create(store: () => throw new InvalidOperationException("no registry"));

        var result = mode.RunCleanup(["--uninstall-cleanup"]);

        Assert.Empty(Assert.Single(titles));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_load_failure_inside_the_store_names_nothing()
    {
        Assert.Empty(RackTitles.ByFolder(new ThrowingLoadStore()));
    }

    private sealed class ThrowingLoadStore : IRackSettingsStore
    {
        public RackLoadResult LoadAll() => throw new InvalidOperationException("boom");
        public RackSettings ReadGlobalStyleDefaults() => throw new NotSupportedException();
        public bool Save(Instance rack) => throw new NotSupportedException();
        public bool Normalize(Instance rack) => throw new NotSupportedException();
        public bool SaveField(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool SaveGlobalDefault(Instance rack, string propertyName) => throw new NotSupportedException();
        public bool Delete(string name) => throw new NotSupportedException();
        public int ResetGlobalStyleDefaults() => throw new NotSupportedException();
    }
}
