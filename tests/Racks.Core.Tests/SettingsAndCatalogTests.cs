using Racks.Core;
using Xunit;

namespace Racks.Tests;

public sealed class SettingsAndCatalogTests : IDisposable
{
    private static string TempRoot => Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "racks-tests");
    private readonly string root = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
    private readonly AppPaths paths;
    public SettingsAndCatalogTests() { paths = new(root); paths.Initialize(); }
    [Fact] public void CorruptSettingsRequireExplicitRestoreAndPreserveDamagedCopy()
    {
        var store = new SettingsStore(paths); store.Save(new() { Theme = "Dark" }); store.Save(new() { Theme = "Light" });
        File.WriteAllText(paths.Settings, "{broken"); Assert.ThrowsAny<Exception>(store.Load);
        Assert.Equal("Dark", store.RestoreBackup().Theme); Assert.Single(Directory.GetFiles(root, "*.damaged-*")); Assert.Equal("Dark", store.Load().Theme);
    }
    [Fact] public void FutureFormatAndInvalidDimensionsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(new() { Version = 99 }));
        Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(new() { Racks = { new() { Folder = root, Width = double.NaN } } }));
    }
    [Fact] public void AtomicSaveLeavesNoTemporaryFiles()
    {
        var store = new SettingsStore(paths); for (var i = 0; i < 10; i++) store.Save(new() { Theme = i.ToString() });
        Assert.Equal("9", store.Load().Theme); Assert.Empty(Directory.GetFiles(root, "*.tmp")); Assert.Equal("8", JsonStore.Read<AppSettings>(paths.Settings + ".bak").Theme);
    }
    [Fact] public void RepeatedIncompleteStartupEntersSafeModeAndReadyResetsIt()
    {
        var startup = new StartupRecovery(paths); Assert.False(startup.Begin()); Assert.False(startup.Begin()); Assert.True(startup.Begin()); startup.Ready(); Assert.False(startup.Begin());
    }
    [Fact] public void CatalogOnlyIncludesRegisteredDirectChildrenAndHonorsLegacyFilter()
    {
        File.WriteAllText(Path.Combine(paths.Workspace, "included.txt"), "content"); File.WriteAllText(Path.Combine(paths.Workspace, "other.txt"), "content");
        var rack = new RackDefinition { Folder = paths.Workspace, IncludedNames = ["included.txt"] };
        var items = FileCatalog.Read([rack], out var errors); Assert.Empty(errors); Assert.Equal("included.txt", Assert.Single(items).Name);
    }
    [Fact] public void SearchRanksExactThenPrefixThenSubstring()
    {
        CatalogItem Item(string name) => new(Guid.NewGuid(), "Rack", name, name, false, 1, DateTime.UtcNow);
        var found = FileCatalog.Search([Item("my report.txt"), Item("report.txt"), Item("report")], "report").Select(x => x.Name);
        Assert.Equal(new[] { "report", "report.txt", "my report.txt" }, found);
    }
    [Fact] public void OrganizationIsPredictableAndDoesNotMoveFiles()
    {
        var path = Path.Combine(paths.Desktop, "photo.jpg"); File.WriteAllText(path, "image"); var groups = FileCatalog.Organize(paths.Desktop);
        Assert.Equal("Images", Assert.Single(groups).Name); Assert.True(File.Exists(path));
    }
    [Theory] [InlineData(".PDF", "Invoice", "invoice.PDF", true)] [InlineData("png", "", "invoice.pdf", false)] [InlineData("pdf", "tax", "invoice.pdf", false)]
    public void RoutingMatchesConditionsTogether(string extension, string name, string file, bool expected) => Assert.Equal(expected, RuleMatcher.Matches(new() { Extensions = extension, NameContains = name }, file));
    [Fact] public void RoutingRejectsLoopsAndEmptyConditions()
    {
        var rack = new RackDefinition { Folder = paths.Workspace }; var settings = new AppSettings { Racks = [rack] };
        Assert.Throws<IOException>(() => RuleMatcher.Validate(new() { Source = paths.Workspace, DestinationRackId = rack.Id, Extensions = "pdf" }, settings));
        Assert.Throws<IOException>(() => RuleMatcher.Validate(new() { Source = paths.Desktop, DestinationRackId = rack.Id }, settings));
        RuleMatcher.Validate(new() { Source = paths.Desktop, DestinationRackId = rack.Id, Extensions = "pdf" }, settings);
    }
    [Fact] public void MissingFolderDoesNotHideOtherRacks()
    {
        File.WriteAllText(Path.Combine(paths.Workspace, "report.txt"), "content");
        var items = FileCatalog.Read([new() { Folder = Path.Combine(root, "missing") }, new() { Folder = paths.Workspace }], out var errors); Assert.Single(items); Assert.Single(errors);
    }
    [Theory] [InlineData("../escape")] [InlineData("a/b")] [InlineData("a\\b")] [InlineData("..")] [InlineData("a.")]
    public void FileNamesCannotEscapeTheirRack(string name) => Assert.False(SafeFiles.IsLeafName(name));
    public void Dispose() { if (root.StartsWith(TempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true); }
}
