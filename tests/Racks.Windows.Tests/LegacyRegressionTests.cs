using System.Reflection;
using Racks.Core;
using Racks.Desktop.Platform;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Racks.Tests.Windows;

public sealed class LegacyRegressionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "racks-tests", Guid.NewGuid().ToString("N"));
    private readonly string? prior = Environment.GetEnvironmentVariable("RACKS_TEST_PROFILE");
    public LegacyRegressionTests() { Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("RACKS_TEST_PROFILE", root); ResetCache(); }
    private static void ResetCache() => typeof(MagicOrganizeUndo).GetField("_last", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
    private (MagicOrganizeUndo Undo, string Source, string Destination) Organized()
    {
        var source = Path.Combine(root, "original.txt"); var destination = Path.Combine(root, "organized.txt"); File.WriteAllText(source, "test content");
        var undo = MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Racks); undo.RecordIntent(source, destination); File.Move(source, destination); undo.RecordMove(source, destination); return (undo, source, destination);
    }
    [Fact] public void LegacyUndoSurvivesReloadAndRestoresFile()
    {
        var (_, source, _) = Organized(); ResetCache(); Assert.Equal(1, MagicOrganizeUndo.Last!.RestoreFiles()); Assert.Equal("test content", File.ReadAllText(source)); Assert.Empty(MagicOrganizeUndo.Last.Moved);
    }
    [Fact] public void LegacyPartialUndoKeepsPendingFilesAndRackNames()
    {
        var (undo, source, destination) = Organized(); undo.CreatedRackNames.Add("My rack"); undo.Save(); File.WriteAllText(source, "new original");
        Assert.Equal(0, undo.RestoreFiles()); ResetCache(); Assert.Single(MagicOrganizeUndo.Last!.Moved); Assert.Single(MagicOrganizeUndo.Last.CreatedRackNames); Assert.Equal("test content", File.ReadAllText(destination));
    }
    [Fact] public void LegacyUndoDoesNotMoveAnEditedReplacement()
    {
        var (undo, source, destination) = Organized(); File.WriteAllText(destination, "edited after organizing"); Assert.Equal(0, undo.RestoreFiles()); Assert.False(File.Exists(source)); Assert.Single(undo.Moved);
    }
    [Fact] public void InterruptedLegacyIntentBlocksNewOrganization()
    {
        var undo = MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Racks); undo.RecordIntent(Path.Combine(root, "a"), Path.Combine(root, "b")); ResetCache();
        Assert.True(MagicOrganizeUndo.Last!.HasUnresolved); Assert.Throws<InvalidOperationException>(() => MagicOrganizeUndo.Begin(MagicOrganizeUndo.Mode.Racks));
    }
    [Fact] public void LegacyFolderCleanupOnlyRemovesEmptyFolders()
    {
        var (undo, _, _) = Organized(); var empty = Path.Combine(root, "empty"); var full = Path.Combine(root, "full"); Directory.CreateDirectory(empty); Directory.CreateDirectory(full); File.WriteAllText(Path.Combine(full, "keep.txt"), "keep");
        undo.CreatedFolders.AddRange([empty, full]); undo.RemoveCreatedFolders(); Assert.False(Directory.Exists(empty)); Assert.True(File.Exists(Path.Combine(full, "keep.txt")));
    }
    [Fact] public void ImportKeepsLegacyPathsAppearanceAndMembershipWithoutMovingFiles()
    {
        var paths = new AppPaths(root); paths.Initialize(); var source = Path.Combine(paths.Workspace, "keep.txt"); File.WriteAllText(source, "keep");
        var settings = new AppSettings();
        LegacyImporter.ImportSnapshot(paths, settings, new() { ["My rack"] = new() { ["Folder"] = paths.Desktop, ["IsDesktopFilterRack"] = "True", ["AssignedFiles"] = "keep.txt", ["ListViewBackgroundColor"] = "#123456", ["TitleText"] = "Imported", ["PosX"] = "250", ["AutoRouteRegex"] = @"\.txt$" } });
        var rack = Assert.Single(settings.Racks); Assert.Equal(paths.Workspace, rack.Folder); Assert.Equal("#123456", rack.Background); Assert.Equal(250, rack.X); Assert.Equal("keep.txt", Assert.Single(rack.IncludedNames!)); Assert.True(File.Exists(source)); Assert.False(Assert.Single(settings.Rules).Enabled);
    }
    [Fact] public void ImportRecognizesSandboxWithoutNonpersistedFlag()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Racks", "VirtualFrames", "test-reference-only");
        var settings = new AppSettings(); LegacyImporter.ImportSnapshot(new(root), settings, new() { ["Sandbox"] = new() { ["Folder"] = folder } });
        Assert.Equal(RackKind.Owned, Assert.Single(settings.Racks).Kind); Assert.Equal(folder, settings.Racks[0].Folder);
    }
    [Fact] public void ImportRetainsLocalizedDecimalSettings()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("pt-PT");
            var settings = new AppSettings();
            LegacyImporter.ImportSnapshot(new(root), settings, new() { ["Localized"] = new() { ["Folder"] = root, ["PosX"] = "250,5", ["ItemFontSize"] = "14,5" } });
            Assert.Equal(250.5, settings.Racks[0].X); Assert.Equal(14.5, settings.Racks[0].FontSize);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
    [Fact] public void RealShellShortcutDoesNotOverwriteExistingFile()
    {
        var source = Path.Combine(root, "source.txt"); var target = Path.Combine(root, "target.lnk"); File.WriteAllText(source, "source");
        var platform = new PlatformServices(); platform.CreateShortcut(source, target); var first = File.ReadAllBytes(target);
        Assert.Throws<IOException>(() => platform.CreateShortcut(source, target)); Assert.Equal(first, File.ReadAllBytes(target)); Assert.Equal("source", File.ReadAllText(source));
    }
    [Theory] [InlineData("zh-CN", "新建框架")] [InlineData("en", "New rack")] [InlineData("pl-PL", "New rack")]
    public void TrayResourcesResolveLocalizedTextOrEnglishFallback(string culture, string expected)
    {
        var previous = Racks.Properties.Lang.Culture;
        try
        {
            Racks.Properties.Lang.Culture = new System.Globalization.CultureInfo(culture);
            Assert.Equal(expected, Racks.Properties.Lang.TrayContextMenu_NewRack);
            foreach (var property in typeof(Racks.Properties.Lang).GetProperties().Where(x => x.Name.StartsWith("TrayContextMenu_", StringComparison.Ordinal)))
                Assert.False(string.IsNullOrWhiteSpace(property.GetValue(null) as string), property.Name);
        }
        finally { Racks.Properties.Lang.Culture = previous; }
    }
    public void Dispose() { ResetCache(); Environment.SetEnvironmentVariable("RACKS_TEST_PROFILE", prior); if (root.StartsWith(Path.Combine(Path.GetTempPath(), "racks-tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true); }
}
