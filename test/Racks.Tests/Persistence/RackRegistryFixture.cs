using System.IO;
using System.Globalization;
using System.Reflection;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;

namespace Racks.Tests.Persistence;

/// <summary>
/// A throwaway registry area for rack persistence tests: <c>Software\RacksTests-&lt;guid&gt;</c>, reached by pointing
/// <c>InstanceController.appName</c> at it (every registry path of the app follows that name). Nothing here ever
/// touches HKCU\SOFTWARE\Racks. Test classes that use it are in the "Registry" collection (appName is process-wide).
/// <c>isInitializingInstances</c> stays true so <c>InstanceController.RefreshMirror</c> never touches the user's
/// real %USERPROFILE%\Racks junctions when a test sets a TitleText.
/// </summary>
public sealed class RackRegistryFixture : IDisposable
{
    private readonly string _previousAppName = InstanceController.appName;
    private readonly IRackSettingsStore _previousStore = Instance.Store;

    public string RootName { get; } = "RacksTests-" + Guid.NewGuid().ToString("N");
    public InstanceController Controller { get; }

    /// <summary>Every audit record the settings layer writes while this fixture is alive.</summary>
    public RecordingAuditSink Audit { get; } = new();

    /// <summary>The settings store of this fixture (the real registry code over the throwaway root, audit recorded here).</summary>
    public RackSettingsRepository Repository { get; }

    public RackRegistryFixture()
    {
        InstanceController.appName = RootName;
        Controller = new InstanceController { isInitializingInstances = true };
        Racks.MainWindow._controller = Controller;
        Repository = new RackSettingsRepository(HkcuRackRegistry.ForApp(), HkcuRegistryRoot.ForApp(), Audit,
            () => Registry.CurrentUser.OpenSubKey(RootPath, writable: true));
        Instance.Store = Repository;
    }

    public void Dispose()
    {
        Instance.Store = _previousStore;
        InstanceController.appName = _previousAppName;
        Racks.MainWindow._controller = null!;
        try { Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\{RootName}", throwOnMissingSubKey: false); } catch { }
    }

    public string RootPath => $@"SOFTWARE\{RootName}";
    public string RackPath(string keyName) => $@"SOFTWARE\{RootName}\Instances\{keyName}";

    // ---- seeding (raw values, written without any code under test) ----------------------------------

    public void SeedRack(string keyName, string valueName, object data, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RackPath(keyName));
        key.SetValue(valueName, data, kind);
    }

    public void SeedRack(string keyName, IEnumerable<(string Name, object Data, RegistryValueKind Kind)> values)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RackPath(keyName));
        foreach (var (name, data, kind) in values) key.SetValue(name, data, kind);
    }

    public void SeedRoot(string valueName, object data, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RootPath);
        key.SetValue(valueName, data, kind);
    }

    // ---- reading back --------------------------------------------------------------------------------

    public (object? Data, RegistryValueKind? Kind) RawRack(string keyName, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RackPath(keyName));
        object? data = key?.GetValue(valueName);
        return data == null ? (null, null) : (data, key!.GetValueKind(valueName));
    }

    public (object? Data, RegistryValueKind? Kind) RawRoot(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RootPath);
        object? data = key?.GetValue(valueName);
        return data == null ? (null, null) : (data, key!.GetValueKind(valueName));
    }

    public bool RackKeyExists(string keyName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RackPath(keyName));
        return key != null;
    }

    /// <summary>Every value of one rack key as name -> (kind, text), for byte-for-byte comparisons.</summary>
    public SortedDictionary<string, string> Snapshot(string keyName)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(RackPath(keyName));
        if (key == null) return result;
        foreach (var name in key.GetValueNames())
        {
            object? data = key.GetValue(name);
            string text = data switch
            {
                string[] lines => "[" + string.Join("|", lines) + "]",
                byte[] bytes => Convert.ToHexString(bytes),
                _ => Convert.ToString(data, CultureInfo.InvariantCulture) ?? "",
            };
            result[name] = key.GetValueKind(name) + ":" + text;
        }
        return result;
    }

    /// <summary>Every rack key and root value, to prove a load or a failed operation changed nothing.</summary>
    public string SnapshotAll()
    {
        var sb = new System.Text.StringBuilder();
        using var instances = Registry.CurrentUser.OpenSubKey(RootPath + @"\Instances");
        if (instances != null)
            foreach (var name in instances.GetSubKeyNames().Order(StringComparer.Ordinal))
                sb.Append('[').Append(name).Append("]\n").Append(string.Join("\n", Snapshot(name).Select(kv => kv.Key + "=" + kv.Value))).Append('\n');
        using var root = Registry.CurrentUser.OpenSubKey(RootPath);
        if (root != null)
            foreach (var name in root.GetValueNames().Order(StringComparer.Ordinal))
                sb.Append("root ").Append(name).Append('=').Append(Convert.ToString(root.GetValue(name), CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    // ---- loading ---------------------------------------------------------------------------------------

    /// <summary>Runs the registry reader of InitInstances (without creating windows) and returns the racks it produced.</summary>
    public List<Instance> Load(bool revertMigration = false, bool folderOpenMigration = false)
    {
        Controller.Instances.Clear();
        Controller.LoadInstancesFromRegistry(revertMigration, folderOpenMigration);
        return Controller.Instances.ToList();
    }

    public Instance LoadSingle(string? name = null)
    {
        var racks = Load();
        return name == null ? Assert.Single(racks) : Assert.Single(racks, r => r.Name == name);
    }

    // ---- cultures ----------------------------------------------------------------------------------------

    public static void WithCulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}

/// <summary>
/// The golden table: one NON-default value for every value name the app persists per rack (67 names), the typed
/// value a loader must produce, and the stored forms (REG_DWORD / REG_SZ / REG_QWORD / REG_MULTI_SZ, culture text)
/// that old and new installs have written. Defaults are read from a fresh <c>Instance</c>, so a new property whose
/// default changes cannot silently equal its "non-default" value.
/// </summary>
public static class GoldenRack
{
    public const string Name = "Golden";

    public enum RawStyle
    {
        /// <summary>Doubles as invariant text, ints as REG_DWORD, bools as "True"/"False": what WriteInstanceToKey writes in en-US.</summary>
        Invariant,
        /// <summary>Doubles in the CURRENT culture's text, ints as REG_SZ text: what the incremental write-through wrote.</summary>
        CultureText,
        /// <summary>Numbers as REG_QWORD: what the layout import of builds before P9 wrote for a JSON number (it writes REG_DWORD now).</summary>
        Qword,
    }

    public static readonly IReadOnlyList<string> Names;
    public static readonly IReadOnlyDictionary<string, object> NonDefault;

    /// <summary>Public FIELDS of Instance that are persisted (no PropertyChanged, so only a bulk save writes them).</summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string> { "IsDesktopFilterRack", "AssignedFiles" };

    /// <summary>Names the reader knows but throws away (the old inverted drop semantic).</summary>
    public static readonly IReadOnlyList<string> Legacy = new[] { "MoveFilesOnDrop" };

    static GoldenRack()
    {
        var probe = new Instance("probe", false);
        object Flip(string n) => !(bool)Get(probe, n)!;

        var table = new Dictionary<string, object>
        {
            // doubles
            ["PosX"] = 123.5,
            ["PosY"] = 45.25,
            ["Width"] = 321.75,
            ["Height"] = 222.5,
            ["IdleOpacity"] = 0.4,
            ["AnimationSpeed"] = 0.75,
            ["MaxGrayScaleStrength"] = 0.6,
            ["TitleFontSize"] = 18.5,
            // ints
            ["IconSize"] = 64,
            ["GridSize"] = 24,
            ["Opacity"] = 77,
            ["SortBy"] = 4,
            ["FolderOrder"] = 2,
            // enum
            ["TitleTextAlignment"] = System.Windows.HorizontalAlignment.Right,
            // strings
            ["Name"] = Name,
            ["Folder"] = Path.GetTempPath().TrimEnd('\\'),
            ["TitleText"] = "My Title",
            ["AutoRouteRegex"] = @"\.pdf$",
            ["FileFilterRegex"] = "^a",
            ["FileFilterHideRegex"] = "^b",
            ["BackgroundImagePath"] = @"C:\Pictures\bg.png",
            // fonts and colors
            ["TitleFontFamily"] = "Consolas",
            ["ItemFontFamily"] = "Arial",
            ["TitleBarColor"] = "#112233",
            ["TitleTextColor"] = "#223344",
            ["BorderColor"] = "#334455",
            ["ListViewBackgroundColor"] = "#44556677",
            ["ActiveBackgroundColor"] = "#556677",
            ["ActiveBorderColor"] = "#667788",
            ["ActiveTitleTextColor"] = "#778899",
            ["ListViewFontColor"] = "#8899AA",
            ["ListViewFontShadowColor"] = "#99AABB",
            // lists
            ["AssignedFiles"] = new List<string> { "a.txt", "sub dir c.pdf" },
            ["ShowOnVirtualDesktops"] = new[] { 1, 3 },
            ["LastAccessedFiles"] = new List<string> { "x.txt", "y.txt" },
            ["CustomOrderFiles"] = new List<Tuple<string, string>> { Tuple.Create("a.txt", "1"), Tuple.Create("b.txt", "2") },
        };
        foreach (var n in new[]
                 {
                     "GrayScaleEnabled", "GrayScaleEnabled_InactiveOnly", "Minimized", "ShowHiddenFiles", "LastAccesedToFirstRow",
                     "EnableCustomItemsOrder", "ShowFileExtension", "ShowFileExtensionIcon", "ShowHiddenFilesIcon", "ShowDisplayName",
                     "IsLocked", "ShowInGrid", "AutoExpandonCursor", "ShowShortcutArrow", "FolderOpenInsideFrame",
                     "HideTitleBarIconsWhenInactive", "SnapWidthToIconWidth", "SnapWidthToIconWidth_PlusScrollbarWidth",
                     "CheckFolderSize", "LinkOnDrop", "SnapToGrid", "PinToTop", "IsTransparent", "DropShadowEnabled",
                     "GradientBackgroundEnabled", "DisableAnimations", "IsDesktopFilterRack", "BorderEnabled",
                     "ActiveBorderEnabled", "ActiveBackgroundEnabled", "ActiveTitleTextEnabled",
                 })
            table[n] = Flip(n);

        NonDefault = table;
        Names = table.Keys.Order(StringComparer.Ordinal).ToList();
    }

    // ---- member access (properties and the two public fields) -------------------------------------------------

    public static object? Get(object target, string name)
    {
        var type = target.GetType();
        var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null) return p.GetValue(target);
        return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target);
    }

    public static void Set(object target, string name, object? value)
    {
        var type = target.GetType();
        var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null) { p.SetValue(target, value); return; }
        type.GetField(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);
    }

    /// <summary>A new rack named <paramref name="name"/> with the non-default value of every name in <paramref name="only"/> (default: all).</summary>
    public static Instance Build(string name = Name, IEnumerable<string>? only = null, bool persist = false)
    {
        var rack = new Instance(name, false);
        // A property change never writes the Name: the rack was saved once before (a new rack is saved when it is created).
        if (persist) Racks.MainWindow._controller.WriteInstanceToKey(rack);
        var names = only ?? Names;
        IDisposable? scope = persist ? null : rack.SuspendPersistence();
        try
        {
            foreach (var n in names)
            {
                if (n == "Name") continue;
                Set(rack, n, Copy(NonDefault[n]));
            }
        }
        finally { scope?.Dispose(); }
        return rack;
    }

    private static object Copy(object v) => v switch
    {
        List<string> l => new List<string>(l),
        int[] a => a.ToArray(),
        List<Tuple<string, string>> t => t.ToList(),
        _ => v,
    };

    // ---- stored forms ----------------------------------------------------------------------------------------

    /// <summary>How an old/new install stores <paramref name="name"/> in the given style (culture text uses the current culture).</summary>
    public static (object Data, RegistryValueKind Kind) Raw(string name, RawStyle style)
    {
        object v = NonDefault[name];
        switch (v)
        {
            case double d:
                return (style == RawStyle.Qword ? d.ToString(CultureInfo.InvariantCulture)
                        : style == RawStyle.CultureText ? d.ToString(CultureInfo.CurrentCulture)
                        : d.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
            case int i:
                return style switch
                {
                    RawStyle.Invariant => (i, RegistryValueKind.DWord),
                    RawStyle.Qword => ((long)i, RegistryValueKind.QWord),
                    _ => (i.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String),
                };
            case bool b:
                return (b ? "True" : "False", RegistryValueKind.String);
            case Enum e:
                return (e.ToString(), RegistryValueKind.String);
            case string s:
                return (s, RegistryValueKind.String);
            case int[] a:
                return (string.Join(",", a), RegistryValueKind.String);
            case List<Tuple<string, string>> t:
                return (t.Select(x => $"{x.Item1},{x.Item2}").ToArray(), RegistryValueKind.MultiString);
            case List<string> l when name == "AssignedFiles":
                return (string.Join("|", l), RegistryValueKind.String);
            case List<string> l:
                return (l.ToArray(), RegistryValueKind.MultiString);
        }
        throw new InvalidOperationException(name);
    }

    public static IEnumerable<(string Name, object Data, RegistryValueKind Kind)> RawAll(RawStyle style, IEnumerable<string>? only = null)
        => (only ?? Names).Select(n => { var (d, k) = Raw(n, style); return (n, d, k); });

    // ---- comparing ------------------------------------------------------------------------------------------

    /// <summary>The names whose value on <paramref name="rack"/> differs from the golden non-default value.</summary>
    public static List<string> Mismatches(Instance rack, IEnumerable<string>? only = null)
    {
        var bad = new List<string>();
        foreach (var n in only ?? Names)
        {
            if (!Same(NonDefault[n], Get(rack, n))) bad.Add($"{n}: expected {Show(NonDefault[n])}, got {Show(Get(rack, n))}");
        }
        return bad;
    }

    public static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (List<string> a, List<string> b) => a.SequenceEqual(b),
        (int[] a, int[] b) => a.SequenceEqual(b),
        (List<Tuple<string, string>> a, List<Tuple<string, string>> b) => a.SequenceEqual(b),
        _ => Equals(expected, actual),
    };

    public static string Show(object? v) => v switch
    {
        null => "null",
        List<string> l => "[" + string.Join(", ", l) + "]",
        int[] a => "[" + string.Join(", ", a) + "]",
        List<Tuple<string, string>> t => "[" + string.Join(", ", t.Select(x => x.ToString())) + "]",
        _ => v.ToString() ?? "",
    };
}
