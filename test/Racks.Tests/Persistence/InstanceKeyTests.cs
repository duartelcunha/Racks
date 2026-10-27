using System.IO;
using Microsoft.Win32;

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
        // isInitializingInstances keeps InstanceController.RefreshMirror away from the real %USERPROFILE%\Racks
        // junctions when a test sets a TitleText.
        _controller = new InstanceController { isInitializingInstances = true };
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

    // Drift guard (behaviour, not source text): every public member of Instance that announces a change is either
    // persisted, which means it is in the golden table and survives write -> load, or it is on the explicit
    // exclusion list below. A new property that is forgotten in the writer, the reader or the golden table fails here.
    private static readonly HashSet<string> NotPersistedOnPurpose = new()
    {
        "IsShortcutsOnly",   // rebuilt from the folder path when the window is created
        "SettingDefault",    // a mode flag of the "Default Style" instance
    };

    [Fact]
    public void Every_persisted_property_round_trips_through_write_and_load()
    {
        var persisted = AnnouncedProperties().Where(n => !NotPersistedOnPurpose.Contains(n))
            .Concat(GoldenRack.Fields).ToHashSet();

        Assert.True(persisted.Count > 50, "the property scan found too few properties");
        Assert.Empty(persisted.Except(GoldenRack.Names).Order().ToList());   // a new persisted member: add it to GoldenRack
        Assert.Empty(GoldenRack.Names.Except(persisted).Order().ToList());   // a golden name that is not a persisted member

        _controller.WriteInstanceToKey(GoldenRack.Build());
        _controller.Instances.Clear();
        _controller.LoadInstancesFromRegistry(false, false);
        var loaded = Assert.Single(_controller.Instances);
        Assert.Empty(GoldenRack.Mismatches(loaded));
    }

    // Public writable properties whose setter raises PropertyChanged for a new value, found by setting them.
    private static List<string> AnnouncedProperties()
    {
        var found = new List<string>();
        foreach (var p in typeof(Instance).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            var probe = new Instance("probe", false);
            using var noWrites = probe.SuspendPersistence();
            bool raised = false;
            probe.PropertyChanged += (_, e) => raised |= e.PropertyName == p.Name;
            object? candidate = GoldenRack.NonDefault.TryGetValue(p.Name, out var golden) ? golden : Candidate(p);
            if (candidate == null) continue;
            try { p.SetValue(probe, candidate); } catch { continue; }
            if (raised) found.Add(p.Name);
        }
        return found;
    }

    private static object? Candidate(System.Reflection.PropertyInfo p)
    {
        var t = p.PropertyType;
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return 7;
        if (t == typeof(double)) return 7.5;
        if (t == typeof(string)) return p.Name.EndsWith("Color") ? "#010203" : p.Name.EndsWith("FontFamily") ? "Arial" : "zz";
        if (t == typeof(List<string>)) return new List<string> { "q" };
        if (t == typeof(int[])) return new[] { 7 };
        if (t == typeof(List<Tuple<string, string>>)) return new List<Tuple<string, string>> { Tuple.Create("q", "1") };
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().Last();
        return null;
    }
}
