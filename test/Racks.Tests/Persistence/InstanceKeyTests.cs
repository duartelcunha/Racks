using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Racks.Tests.Guards;

namespace Racks.Tests.Persistence;

[Collection("Registry")]
public sealed class InstanceKeyTests : IDisposable
{
    private readonly string _previousAppName = InstanceController.appName;
    private readonly string _root = "RacksTests-" + Guid.NewGuid().ToString("N");
    private readonly InstanceController _controller;

    public InstanceKeyTests()
    {
        // The registry root follows InstanceController.appName, so tests never touch HKCU\SOFTWARE\Racks.
        InstanceController.appName = _root;
        _controller = new InstanceController();
        Racks.MainWindow._controller = _controller;
    }

    public void Dispose()
    {
        InstanceController.appName = _previousAppName;
        Racks.MainWindow._controller = null!;
        try { Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\{_root}", throwOnMissingSubKey: false); } catch { }
    }

    private static Instance NewRack(string name)
    {
        var rack = new Instance(name, false) { Folder = Path.GetTempPath() };
        return rack;
    }

    private RegistryKey? Open(string name) => Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{_root}\Instances\{name}");

    [Fact]
    public void WriteOver_with_an_unchanged_name_keeps_the_key()
    {
        var rack = NewRack("keep");
        _controller.WriteInstanceToKey(rack);

        _controller.WriteOverInstanceToKey(rack, "keep");

        using var key = Open("keep");
        Assert.NotNull(key);
        Assert.Equal("keep", key!.GetValue("Name"));
    }

    [Fact]
    public void WriteOver_with_a_new_name_moves_the_key()
    {
        var rack = NewRack("old");
        _controller.WriteInstanceToKey(rack);

        rack.Name = "new";
        _controller.WriteOverInstanceToKey(rack, "old");

        using (var oldKey = Open("old")) Assert.Null(oldKey);
        using var newKey = Open("new");
        Assert.NotNull(newKey);
    }

    [Fact]
    public void WriteOver_persists_the_values_it_used_to_skip()
    {
        var rack = NewRack("full");
        rack.DropShadowEnabled = true;
        rack.GradientBackgroundEnabled = true;
        rack.DisableAnimations = true;
        rack.IsDesktopFilterRack = true;
        rack.IsTransparent = true;
        rack.AssignedFiles = new List<string> { "a.txt", "b.txt" };

        _controller.WriteOverInstanceToKey(rack, "somewhere-else");

        using var key = Open("full");
        Assert.NotNull(key);
        Assert.Equal("True", key!.GetValue("DropShadowEnabled")?.ToString());
        Assert.Equal("True", key.GetValue("GradientBackgroundEnabled")?.ToString());
        Assert.Equal("True", key.GetValue("DisableAnimations")?.ToString());
        Assert.Equal("True", key.GetValue("IsDesktopFilterRack")?.ToString());
        Assert.Equal("True", key.GetValue("IsTransparent")?.ToString());
        Assert.Equal("a.txt|b.txt", key.GetValue("AssignedFiles")?.ToString());
    }

    [Fact]
    public void An_empty_placeholder_rack_is_never_persisted()
    {
        var rack = NewRack("empty");
        _controller.WriteOverInstanceToKey(rack, "empty");
        using var key = Open("empty");
        Assert.Null(key);
    }

    // Drift guard: every persisted Instance property must be written by WriteInstanceToKey and read
    // by InitInstances. A new property that is forgotten in either place fails here.
    [Fact]
    public void Every_persisted_property_is_written_and_read()
    {
        string instanceSrc = File.ReadAllText(RepoPaths.App("Core", "Instance.cs"));
        string controllerSrc = File.ReadAllText(RepoPaths.App("Core", "InstanceController.cs"));

        var props = Regex.Matches(instanceSrc, @"OnPropertyChanged\(nameof\((\w+)\)").Select(m => m.Groups[1].Value).ToHashSet();
        string writer = Between(controllerSrc, "public void WriteInstanceToKey(", "public void AddInstance(");
        var written = Regex.Matches(writer, @"SetValue\(""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var read = Regex.Matches(controllerSrc, @"case ""(\w+)"":").Select(m => m.Groups[1].Value).ToHashSet();

        // Not persisted on purpose: rebuilt from the folder path (IsShortcutsOnly) or a mode flag (SettingDefault).
        var notPersisted = new HashSet<string> { "IsShortcutsOnly", "SettingDefault" };
        var expected = props.Except(notPersisted).ToList();

        Assert.True(expected.Count > 50, "property scan found too few properties; the regex needs updating");
        Assert.Empty(expected.Where(p => !written.Contains(p)).Order());
        Assert.Empty(expected.Where(p => !read.Contains(p)).Order());
    }

    private static string Between(string text, string start, string end)
    {
        int i = text.IndexOf(start, StringComparison.Ordinal);
        int j = text.IndexOf(end, i + start.Length, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i, $"could not find '{start}'..'{end}'");
        return text.Substring(i, j - i);
    }
}
