using System.IO;
using Microsoft.Win32;
using Racks.Infrastructure.Registry;
using Racks.Tests.Settings;

namespace Racks.Tests.Persistence;

/// <summary>A rack registry that fails the way a locked-down or broken one does, around the real throwaway one.</summary>
public sealed class FailingRackRegistry(IRackRegistry inner) : IRackRegistry
{
    public bool FailList { get; set; }
    public string? FailReadFor { get; set; }
    public bool FailApply { get; set; }
    public bool FailDelete { get; set; }

    public string Path => inner.Path;

    public IReadOnlyList<string> GetRackKeyNames() =>
        FailList ? throw new UnauthorizedAccessException("list denied") : inner.GetRackKeyNames();

    public IReadOnlyDictionary<string, RegistryValue> ReadValues(string keyName) =>
        keyName == FailReadFor ? throw new IOException("read failed") : inner.ReadValues(keyName);

    public void Apply(string keyName, IReadOnlyList<RegistryWrite> writes)
    {
        if (FailApply) throw new UnauthorizedAccessException("write denied");
        inner.Apply(keyName, writes);
    }

    public bool DeleteKey(string keyName) =>
        FailDelete ? throw new UnauthorizedAccessException("delete denied") : inner.DeleteKey(keyName);
}

/// <summary>The write side of the settings layer, deleting, resetting, and what happens when the registry fails (nothing reaches the UI as an exception).</summary>
[Collection("Registry")]
public sealed class RackSettingsRepositoryTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    private int WriteFailures() => _reg.Audit.Events.Count(e => e.Kind == "SettingWriteFailed");

    // ---- saving ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_full_save_keeps_the_historic_rules_for_empty_and_missing_values()
    {
        var rack = GoldenRack.Build("Rules", only: new[] { "LastAccessedFiles", "AssignedFiles" });
        _reg.Controller.WriteInstanceToKey(rack);
        Assert.NotNull(_reg.RawRack("Rules", "AssignedFiles").Data);

        rack.AssignedFiles = new List<string>();
        rack.LastAccessedFiles = new List<string>();
        _reg.Controller.WriteInstanceToKey(rack);

        Assert.Null(_reg.RawRack("Rules", "AssignedFiles").Data);                      // removed when empty
        Assert.NotNull(_reg.RawRack("Rules", "LastAccessedFiles").Data);               // a full save leaves an empty list alone
        Assert.Null(_reg.RawRack("Rules", "ShowOnVirtualDesktops").Data);               // null: not written
        Assert.Null(_reg.RawRack("Rules", "TitleText").Data);                           // null text: not written
        Assert.Equal("", _reg.RawRack("Rules", "AutoRouteRegex").Data);                 // these two are always written
        Assert.Equal("", _reg.RawRack("Rules", "BackgroundImagePath").Data);
    }

    [Fact]
    public void Changing_a_single_list_value_stores_the_empty_list()
    {
        var rack = GoldenRack.Build("Single", only: new[] { "LastAccessedFiles", "ShowOnVirtualDesktops" }, persist: true);
        rack.LastAccessedFiles = new List<string>();
        rack.ShowOnVirtualDesktops = null;
        Assert.Empty((string[])_reg.RawRack("Single", "LastAccessedFiles").Data!);
        Assert.Equal("", _reg.RawRack("Single", "ShowOnVirtualDesktops").Data);
    }

    [Fact]
    public void Values_that_are_not_stored_are_ignored_by_the_single_value_write()
    {
        var rack = GoldenRack.Build("Odd", only: Array.Empty<string>(), persist: true);
        rack.IsShortcutsOnly = true;                       // rebuilt from the folder; never stored
        Assert.Null(_reg.RawRack("Odd", "IsShortcutsOnly").Data);
        Assert.True(_reg.Repository.SaveField(rack, "NotAValue"));
        Assert.Empty(_reg.Audit.Events);
    }

    [Fact]
    public void A_default_style_instance_writes_global_defaults_as_text()
    {
        var style = new Instance("Default Style", true);
        style.IconSize = 48;
        style.IdleOpacity = 0.4;
        style.GrayScaleEnabled = true;
        style.TitleTextAlignment = System.Windows.HorizontalAlignment.Right;
        style.PosX = 5;                                    // a rack-only value: not a global default
        style.TitleBarColor = "#112233";

        Assert.Equal(("48", RegistryValueKind.String), ((string)_reg.RawRoot("IconSize").Data!, _reg.RawRoot("IconSize").Kind!.Value));
        Assert.Equal("0.4", _reg.RawRoot("IdleOpacity").Data);
        Assert.Equal("True", _reg.RawRoot("GrayScaleEnabled").Data);
        Assert.Equal("Right", _reg.RawRoot("TitleTextAlignment").Data);
        Assert.Equal("#112233", _reg.RawRoot("TitleBarColor").Data);
        Assert.Null(_reg.RawRoot("PosX").Data);
        Assert.False(_reg.RackKeyExists("Default Style"));
        Assert.Equal(48, new Instance("empty", false).IconSize);   // and a new rack starts from it
    }

    // ---- deleting ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Delete_removes_one_rack_key_and_nothing_else()
    {
        _reg.SeedRack("Keep", "Name", "Keep", RegistryValueKind.String);
        _reg.SeedRack("Drop", "Name", "Drop", RegistryValueKind.String);
        Assert.True(_reg.Repository.Delete("Drop"));
        Assert.False(_reg.RackKeyExists("Drop"));
        Assert.True(_reg.RackKeyExists("Keep"));
        Assert.False(_reg.Repository.Delete("Drop"));      // already gone
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"Keep\Child")]
    [InlineData("a/b")]
    public void Delete_refuses_a_name_that_is_not_one_safe_segment(string name)
    {
        _reg.SeedRack("Keep", "Name", "Keep", RegistryValueKind.String);
        string before = _reg.SnapshotAll();
        Assert.False(_reg.Repository.Delete(name));
        Assert.Equal(before, _reg.SnapshotAll());
        Assert.Equal(1, WriteFailures());
    }

    [Fact]
    public void Renaming_a_rack_moves_its_key()
    {
        var rack = GoldenRack.Build("Old", only: new[] { "PosX" });
        _reg.Controller.WriteInstanceToKey(rack);
        rack.Name = "New";
        _reg.Controller.WriteOverInstanceToKey(rack, "Old");
        Assert.False(_reg.RackKeyExists("Old"));
        Assert.Equal("New", _reg.RawRack("New", "Name").Data);
    }

    // ---- reset ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Resetting_the_global_style_defaults_keeps_switches_and_markers()
    {
        _reg.SeedRoot("TitleBarColor", "#112233");
        _reg.SeedRoot("IconSize", "64");
        _reg.SeedRoot("IcePhysics", "False");
        _reg.SeedRoot("FirstRunWelcomeShownV2", "True");

        Assert.Equal(2, _reg.Repository.ResetGlobalStyleDefaults());

        Assert.Null(_reg.RawRoot("TitleBarColor").Data);
        Assert.Equal("False", _reg.RawRoot("IcePhysics").Data);
        Assert.Equal("True", _reg.RawRoot("FirstRunWelcomeShownV2").Data);
    }

    // ---- the store is replaceable --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_replaced_store_receives_the_writes_and_the_registry_is_not_touched()
    {
        var spy = new SpyStore();
        var previous = Instance.Store;
        Instance.Store = spy;
        try
        {
            var rack = new Instance("probe", false);
            rack.IconSize = 99;
            rack.Folder = Path.GetTempPath();
            using (rack.SuspendPersistence()) rack.IsLocked = true;   // suspended: not saved

            Assert.Equal(new[] { "IconSize", "Folder" }, spy.Fields);
        }
        finally { Instance.Store = previous; }
        Assert.False(_reg.RackKeyExists("probe"));
    }

    // ---- failures never reach the UI -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_failing_registry_never_throws_into_property_changes_saves_renames_or_deletes()
    {
        var failing = new FailingRackRegistry(HkcuRackRegistry.ForApp()) { FailApply = true, FailDelete = true };
        var previous = Instance.Store;
        Instance.Store = new RackSettingsRepository(failing, HkcuRegistryRoot.ForApp(), _reg.Audit);
        try
        {
            var rack = new Instance("Failing", false);
            var error = Record.Exception(() =>
            {
                rack.IconSize = 64;                                   // write-through
                rack.PosX = 10;
                _reg.Controller.WriteInstanceToKey(rack);              // full save
                rack.Name = "Renamed";
                _reg.Controller.WriteOverInstanceToKey(rack, "Failing");   // save + delete the old key
                Instance.Store.Delete("Renamed");
            });

            Assert.Null(error);
            Assert.False(_reg.RackKeyExists("Failing"));
            Assert.True(WriteFailures() >= 5);
            Assert.False(Instance.Store.Save(rack));
        }
        finally { Instance.Store = previous; }
    }

    [Fact]
    public void A_rack_without_a_name_is_not_saved_and_not_reported()
    {
        // A rack whose folder is a drive root has no name (pre-existing): there is no key to write, and nothing to report on every change.
        var rack = new Instance("", false);
        rack.IconSize = 64;
        rack.PosX = 5;
        Assert.False(_reg.Repository.Save(rack));
        Assert.Empty(_reg.Audit.Events);
        Assert.False(_reg.RackKeyExists(""));
    }

    [Fact]
    public void A_failing_root_still_gives_a_new_rack_its_built_in_defaults()
    {
        var previous = Instance.Store;
        Instance.Store = new RackSettingsRepository(HkcuRackRegistry.ForApp(), new FailingRegistryRoot(), _reg.Audit);
        try
        {
            var rack = Record.Exception(() => new Instance("empty", false));
            Assert.Null(rack);
            Assert.Equal(32, new Instance("empty", false).IconSize);
            Assert.Contains(_reg.Audit.Events, e => e.Kind == "SettingFallback" && e.Reason.StartsWith("read failed"));
        }
        finally { Instance.Store = previous; }
    }

    [Fact]
    public void A_failing_audit_sink_changes_nothing()
    {
        var repo = new RackSettingsRepository(HkcuRackRegistry.ForApp(), HkcuRegistryRoot.ForApp(), new Racks.Tests.Fakes.ThrowingAuditSink());
        _reg.SeedRack("Bad", new (string, object, RegistryValueKind)[] { ("Name", "Bad", RegistryValueKind.String), ("PosX", "NaN", RegistryValueKind.String) });
        var result = repo.LoadAll();
        Assert.Single(result.Racks);
        Assert.Single(result.Issues);
    }

    private sealed class SpyStore : IRackSettingsStore
    {
        public List<string> Fields { get; } = new();
        public RackLoadResult LoadAll() => new(Array.Empty<LoadedRack>(), Array.Empty<SettingIssue>());
        public RackSettings ReadGlobalStyleDefaults() => new();
        public bool Save(Instance rack) => true;
        public bool Normalize(Instance rack) => true;
        public bool SaveField(Instance rack, string propertyName) { Fields.Add(propertyName); return true; }
        public bool SaveGlobalDefault(Instance rack, string propertyName) => true;
        public bool Delete(string name) => true;
        public int ResetGlobalStyleDefaults() => 0;
    }
}
