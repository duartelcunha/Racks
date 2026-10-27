using System.IO;
using Microsoft.Win32;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Persistence;

/// <summary>
/// What the settings layer does with values it cannot use (INV-REG-2): they are never applied, the default stays, and each
/// one is a single <c>SettingFallback</c> audit record. One bad value or one bad rack never stops the rest, and loading never
/// writes. Replaces the quirks that the P6a tests documented (NaN accepted, one bad rack aborted every rack, load rewrote values).
/// </summary>
[Collection("Registry")]
public sealed class RackSettingsValidationTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    private static List<(string, object, RegistryValueKind)> Rack(string name, params (string, object, RegistryValueKind)[] more)
    {
        var list = new List<(string, object, RegistryValueKind)> { ("Name", name, RegistryValueKind.String) };
        if (!more.Any(m => m.Item1 == "Folder")) list.Add(("Folder", Path.GetTempPath(), RegistryValueKind.String));
        list.AddRange(more);
        return list;
    }

    private List<AuditEvent> Fallbacks(string? field = null) =>
        _reg.Audit.Events.Where(e => e.Kind == "SettingFallback" && (field == null || e.Detail.Contains($"field={field};", StringComparison.Ordinal))).ToList();

    // ---- a value that cannot be used: default stays, one record -----------------------------------------------------------

    [Theory]
    [InlineData("PosX", "NaN")]
    [InlineData("PosY", "Infinity")]
    [InlineData("PosY", "-Infinity")]
    [InlineData("Width", "1e999")]
    [InlineData("Width", "-5")]
    [InlineData("Height", "99999999")]
    [InlineData("PosX", "abc")]
    [InlineData("PosX", "")]
    [InlineData("IdleOpacity", "5")]
    [InlineData("IdleOpacity", "-0.1")]
    [InlineData("AnimationSpeed", "11")]
    [InlineData("MaxGrayScaleStrength", "-1")]
    [InlineData("TitleFontSize", "0")]
    [InlineData("TitleFontSize", "9999")]
    [InlineData("Opacity", "9999")]
    [InlineData("Opacity", "-1")]
    [InlineData("SortBy", "-4")]
    [InlineData("SortBy", "11")]
    [InlineData("FolderOrder", "3")]
    [InlineData("IconSize", "x")]
    [InlineData("GridSize", "1.5")]
    [InlineData("IconSize", "99999999999")]
    [InlineData("TitleTextAlignment", "7")]
    [InlineData("TitleTextAlignment", "Diagonal")]
    [InlineData("TitleTextAlignment", "Left, Right")]
    [InlineData("IsLocked", "1")]
    [InlineData("PinToTop", "yes")]
    [InlineData("LinkOnDrop", "")]
    [InlineData("TitleFontFamily", @"\\attacker.example\share\#Evil")]
    [InlineData("ItemFontFamily", "file://attacker.example/share/#Evil")]
    [InlineData("BorderColor", "ContextColor file://attacker.example/share/p.icc 1,0,0,0")]
    [InlineData("ListViewFontColor", "#GGG")]
    [InlineData("Folder", "C:\\a\0b")]
    [InlineData("BackgroundImagePath", "C:\\a\nb")]
    public void An_unusable_value_keeps_the_default_and_is_recorded_once(string name, string garbage)
    {
        var defaults = new Instance("fresh", false);
        _reg.SeedRack("G", Rack("G", (name, garbage, RegistryValueKind.String)));

        var rack = _reg.LoadSingle("G");

        Assert.True(GoldenRack.Same(GoldenRack.Get(defaults, name), GoldenRack.Get(rack, name)), $"{name} should have stayed at its default");
        var record = Assert.Single(Fallbacks(name));
        Assert.Equal("Rejected", record.Status);
        Assert.Contains("rack=G;", record.Detail);
        Assert.Contains("fallback=", record.Detail);
        Assert.False(string.IsNullOrEmpty(record.Reason));
        Assert.EndsWith(@"\Instances\G\" + name, record.Source);
    }

    [Fact]
    public void A_value_of_the_wrong_registry_type_is_refused()
    {
        _reg.SeedRack("T", Rack("T",
            ("PosX", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary),
            ("IsLocked", 1, RegistryValueKind.DWord),
            ("LastAccessedFiles", "a.txt", RegistryValueKind.String),
            ("CustomOrderFiles", "a.txt,1", RegistryValueKind.String),
            ("AssignedFiles", new[] { "a", "b" }, RegistryValueKind.MultiString),
            ("TitleText", new[] { "a" }, RegistryValueKind.MultiString)));

        var rack = _reg.LoadSingle("T");

        Assert.False(rack.IsLocked);
        Assert.Empty(rack.LastAccessedFiles);
        Assert.Empty(rack.CustomOrderFiles);
        Assert.Empty(rack.AssignedFiles);
        Assert.Null(rack.TitleText);
        foreach (var field in new[] { "PosX", "IsLocked", "LastAccessedFiles", "CustomOrderFiles", "AssignedFiles", "TitleText" })
            Assert.Single(Fallbacks(field));
    }

    [Fact]
    public void Missing_and_empty_font_and_color_values_are_not_recorded()
    {
        _reg.SeedRack("Q", Rack("Q", ("TitleFontFamily", "", RegistryValueKind.String), ("TitleBarColor", "", RegistryValueKind.String)));
        var rack = _reg.LoadSingle("Q");
        Assert.Equal("Segoe UI", rack.TitleFontFamily);
        Assert.Empty(Fallbacks());                     // nothing missing, nothing empty, nothing else is worth a record
    }

    [Fact]
    public void The_raw_value_in_the_record_is_shortened_to_200_characters()
    {
        _reg.SeedRack("L", Rack("L", ("TitleFontFamily", new string('x', 500) + @"\", RegistryValueKind.String)));
        _reg.LoadSingle("L");
        var record = Assert.Single(Fallbacks("TitleFontFamily"));
        string raw = record.Detail.Split("raw=")[1].Split("; fallback=")[0];
        Assert.Equal(203, raw.Length);
        Assert.EndsWith("...", raw);
    }

    [Theory]
    [InlineData("IconSize", "5000", 256)]
    [InlineData("IconSize", "1", 16)]
    [InlineData("GridSize", "1", 2)]
    [InlineData("GridSize", "9999", 256)]
    public void Where_the_setter_clamps_the_value_is_clamped_and_recorded(string name, string stored, int expected)
    {
        _reg.SeedRack("C", Rack("C", (name, stored, RegistryValueKind.String)));
        var rack = _reg.LoadSingle("C");
        Assert.Equal(expected, (int)GoldenRack.Get(rack, name)!);
        var record = Assert.Single(Fallbacks(name));
        Assert.Contains($"clamped to {expected}", record.Detail);
    }

    [Theory]
    [InlineData("IconSize", 16)]
    [InlineData("IconSize", 256)]
    [InlineData("GridSize", 2)]
    [InlineData("Opacity", 0)]
    [InlineData("Opacity", 255)]
    [InlineData("SortBy", 10)]
    [InlineData("FolderOrder", 0)]
    public void The_limits_themselves_are_valid(string name, int value)
    {
        _reg.SeedRack("Lim", Rack("Lim", (name, value, RegistryValueKind.DWord)));
        var rack = _reg.LoadSingle("Lim");
        Assert.Equal(value, (int)GoldenRack.Get(rack, name)!);
        Assert.Empty(Fallbacks());
    }

    // ---- numbers and cultures --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("pt-PT", "0,5")]   // what an older build wrote on a comma machine
    [InlineData("pt-PT", "0.5")]   // what this build writes, read on the same machine
    [InlineData("en-US", "0.5")]
    [InlineData("de-DE", "0,5")]
    [InlineData("de-DE", "0.5")]
    public void Both_decimal_spellings_are_read_in_every_culture(string culture, string text)
    {
        RackRegistryFixture.WithCulture(culture, () =>
        {
            _reg.SeedRack("Cult", Rack("Cult", ("IdleOpacity", text, RegistryValueKind.String), ("TitleFontSize", "12,5", RegistryValueKind.String)));
            var rack = _reg.LoadSingle("Cult");
            Assert.Equal(0.5, rack.IdleOpacity);
            if (culture != "en-US") Assert.Equal(12.5, rack.TitleFontSize);
        });
    }

    [Fact]
    public void Numbers_stored_as_dword_qword_or_text_are_all_read()
    {
        _reg.SeedRack("N", Rack("N",
            ("IconSize", 48L, RegistryValueKind.QWord),
            ("Opacity", 90, RegistryValueKind.DWord),
            ("SortBy", "3", RegistryValueKind.String),
            ("PosX", 12L, RegistryValueKind.QWord),
            ("Width", 300, RegistryValueKind.DWord)));
        var rack = _reg.LoadSingle("N");
        Assert.Equal(48, rack.IconSize);
        Assert.Equal(90, rack.Opacity);
        Assert.Equal(3, rack.SortBy);
        Assert.Equal(12, rack.PosX);
        Assert.Equal(300, rack.Width);
        Assert.Empty(Fallbacks());
    }

    [Fact]
    public void Doubles_are_written_in_invariant_text_whatever_the_culture()
    {
        RackRegistryFixture.WithCulture("pt-PT", () =>
        {
            _reg.Controller.WriteInstanceToKey(GoldenRack.Build());
            Assert.Equal("123.5", _reg.RawRack(GoldenRack.Name, "PosX").Data);
            Assert.Equal("18.5", _reg.RawRack(GoldenRack.Name, "TitleFontSize").Data);

            var rack = GoldenRack.Build("Inc", only: Array.Empty<string>(), persist: true);
            rack.IdleOpacity = 0.4;                                   // write-through
            Assert.Equal("0.4", _reg.RawRack("Inc", "IdleOpacity").Data);
        });
    }

    [Fact]
    public void A_single_changed_value_is_stored_as_its_own_kind()
    {
        var rack = GoldenRack.Build("Kinds", only: Array.Empty<string>(), persist: true);
        rack.IconSize = 64;
        rack.IsLocked = true;
        rack.LastAccessedFiles = new List<string> { "a" };
        Assert.Equal((64, RegistryValueKind.DWord), ((int)_reg.RawRack("Kinds", "IconSize").Data!, _reg.RawRack("Kinds", "IconSize").Kind!.Value));
        Assert.Equal(("True", RegistryValueKind.String), ((string)_reg.RawRack("Kinds", "IsLocked").Data!, _reg.RawRack("Kinds", "IsLocked").Kind!.Value));
        Assert.Equal(RegistryValueKind.MultiString, _reg.RawRack("Kinds", "LastAccessedFiles").Kind);
    }

    [Fact]
    public void A_number_that_is_not_finite_is_not_stored()
    {
        var rack = GoldenRack.Build("Fin", only: new[] { "PosX" }, persist: true);
        rack.PosX = double.NaN;
        Assert.Equal("123.5", _reg.RawRack("Fin", "PosX").Data);     // the last good value stays
        Assert.Contains(_reg.Audit.Events, e => e.Kind == "SettingWriteFailed" && e.Detail.Contains("field=PosX"));
    }

    // ---- lists ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_bad_virtual_desktop_item_drops_that_item_only()
    {
        _reg.SeedRack("VD", Rack("VD", ("ShowOnVirtualDesktops", "1,x,3", RegistryValueKind.String)));
        Assert.Equal(new[] { 1, 3 }, _reg.LoadSingle("VD").ShowOnVirtualDesktops);
        Assert.Single(Fallbacks("ShowOnVirtualDesktops"));
    }

    [Fact]
    public void A_virtual_desktop_list_with_no_good_item_is_refused()
    {
        _reg.SeedRack("VD", Rack("VD", ("ShowOnVirtualDesktops", "a,b", RegistryValueKind.String)));
        Assert.Null(_reg.LoadSingle("VD").ShowOnVirtualDesktops);
        Assert.Single(Fallbacks("ShowOnVirtualDesktops"));
    }

    [Fact]
    public void Malformed_custom_order_items_are_dropped_and_recorded_once_for_the_value()
    {
        _reg.SeedRack("CO", Rack("CO", ("CustomOrderFiles", new[] { "a.txt,1", "no-comma", "also-bad", "b.txt,2" }, RegistryValueKind.MultiString)));
        Assert.Equal(new[] { Tuple.Create("a.txt", "1"), Tuple.Create("b.txt", "2") }, _reg.LoadSingle("CO").CustomOrderFiles);
        Assert.Single(Fallbacks("CustomOrderFiles"));
    }

    // ---- identity ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_rack_without_a_Name_value_takes_the_name_of_its_key()
    {
        _reg.SeedRack("From Key", new (string, object, RegistryValueKind)[] { ("Folder", Path.GetTempPath(), RegistryValueKind.String), ("PosX", "10", RegistryValueKind.String) });
        var rack = Assert.Single(_reg.Load());
        Assert.Equal("From Key", rack.Name);
        Assert.Equal(@"SOFTWARE\" + _reg.RootName + @"\Instances\From Key", rack.GetKeyLocation());
        var record = Assert.Single(Fallbacks("Name"));
        Assert.Contains("fallback=the registry key name is used", record.Detail);
        Assert.Contains("raw=(missing)", record.Detail);
    }

    [Fact]
    public void An_empty_Name_value_is_treated_as_missing()
    {
        _reg.SeedRack("Key2", new (string, object, RegistryValueKind)[] { ("Name", "  ", RegistryValueKind.String), ("Folder", Path.GetTempPath(), RegistryValueKind.String) });
        Assert.Equal("Key2", Assert.Single(_reg.Load()).Name);
        Assert.Single(Fallbacks("Name"));
    }

    [Fact]
    public void A_Name_that_differs_from_its_key_is_used_and_recorded()
    {
        _reg.SeedRack("key-name", Rack("Value Name"));
        Assert.Equal("Value Name", Assert.Single(_reg.Load()).Name);
        Assert.Single(Fallbacks("Name"));
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(".")]
    public void A_rack_with_an_unsafe_name_is_skipped_and_its_data_is_left_alone(string name)
    {
        _reg.SeedRack("Bad", Rack(name, ("PosX", "10", RegistryValueKind.String)));
        _reg.SeedRack("Good", Rack("Good"));
        string before = _reg.SnapshotAll();

        var racks = _reg.Load();

        Assert.Equal(new[] { "Good" }, racks.Select(r => r.Name).ToArray());
        Assert.Equal(before, _reg.SnapshotAll());
        Assert.True(_reg.RackKeyExists("Bad"));
        var record = Assert.Single(Fallbacks("Name"));
        Assert.Equal("Skipped", record.Status);
    }

    [Fact]
    public void A_rack_named_empty_is_still_the_placeholder_and_its_key_is_not_deleted()
    {
        _reg.SeedRack("empty", Rack("empty"));
        Assert.Empty(_reg.Load());
        Assert.True(_reg.RackKeyExists("empty"));
        Assert.Empty(Fallbacks());
    }

    // ---- one bad rack never stops the others -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_rack_with_a_poisoned_value_loads_with_the_default_and_the_racks_after_it_still_load()
    {
        _reg.SeedRack("A-good", Rack("A-good"));
        _reg.SeedRack("B-poison", Rack("B-poison", ("Folder", "C:\\a\0b", RegistryValueKind.String), ("PosX", "NaN", RegistryValueKind.String)));
        _reg.SeedRack("Z-good", Rack("Z-good"));

        var racks = _reg.Load();

        Assert.Equal(new[] { "A-good", "B-poison", "Z-good" }, racks.Select(r => r.Name).ToArray());
        var poisoned = racks.Single(r => r.Name == "B-poison");
        Assert.True(poisoned.IsFolderMissing);                      // its folder is unusable: the rack is flagged, not dropped
        Assert.Single(Fallbacks("Folder"));
        Assert.Single(Fallbacks("PosX"));
    }

    [Fact]
    public void A_rack_that_cannot_be_read_at_all_does_not_stop_the_others()
    {
        _reg.SeedRack("A", Rack("A"));
        _reg.SeedRack("B", Rack("B"));
        _reg.SeedRack("C", Rack("C"));
        var repo = new RackSettingsRepository(new FailingRackRegistry(HkcuRackRegistry.ForApp()) { FailReadFor = "B" }, HkcuRegistryRoot.ForApp(), _reg.Audit);

        var result = repo.LoadAll();

        Assert.Equal(new[] { "A", "C" }, result.Racks.Select(r => r.KeyName).ToArray());
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Failed", issue.Status);
        Assert.Equal(1, _reg.Audit.Events.Count(e => e.Kind == "SettingFallback" && e.Status == "Failed"));
    }

    [Fact]
    public void An_unreadable_Instances_key_loads_no_racks_without_throwing()
    {
        var repo = new RackSettingsRepository(new FailingRackRegistry(HkcuRackRegistry.ForApp()) { FailList = true }, HkcuRegistryRoot.ForApp(), _reg.Audit);
        Assert.Empty(repo.LoadAll().Racks);
        Assert.Single(Fallbacks());
    }

    // ---- loading never writes ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Loading_changes_nothing_in_the_registry_not_even_values_that_needed_fixing()
    {
        RackRegistryFixture.WithCulture("pt-PT", () =>
        {
            _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(GoldenRack.RawStyle.CultureText));
            _reg.SeedRack("Messy", Rack("Messy",
                ("IconSize", 5000, RegistryValueKind.DWord),             // would be clamped
                ("PosX", "123,5", RegistryValueKind.String),            // legacy text
                ("Width", "NaN", RegistryValueKind.String),
                ("IsLocked", "1", RegistryValueKind.String),
                ("TitleText", "x", RegistryValueKind.String),
                ("MoveFilesOnDrop", "True", RegistryValueKind.String),
                ("Unknown", "z", RegistryValueKind.String)));
            _reg.SeedRoot("IconSize", "5000");
            string before = _reg.SnapshotAll();

            _reg.Load();
            _ = new Instance("empty", false);                           // reads the global defaults too
            _reg.Load();

            Assert.Equal(before, _reg.SnapshotAll());
        });
    }

    [Fact]
    public void The_only_writes_a_load_makes_are_the_two_one_shot_migrations()
    {
        _reg.SeedRack("Mig", Rack("Mig", ("Folder", Path.Combine(InstanceController.VirtualFramesRoot, "Mig"), RegistryValueKind.String),
            ("LinkOnDrop", "True", RegistryValueKind.String), ("FolderOpenInsideFrame", "True", RegistryValueKind.String), ("IconSize", 64, RegistryValueKind.DWord)));
        var before = _reg.Snapshot("Mig");

        _reg.Load(revertMigration: true, folderOpenMigration: true);

        var after = _reg.Snapshot("Mig");
        var changed = after.Where(kv => !before.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key).Order().ToArray();
        Assert.Equal(new[] { "FolderOpenInsideFrame", "LinkOnDrop" }, changed);
        Assert.Equal("String:False", after["LinkOnDrop"]);
    }

    // ---- global defaults -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void One_bad_global_default_does_not_skip_the_ones_after_it()
    {
        _reg.SeedRoot("IdleOpacity", "not a number");   // first in the old list
        _reg.SeedRoot("IconSize", "48");                // last in the old list
        _reg.SeedRoot("TitleBarColor", "#112233");

        var rack = new Instance("empty", false);

        Assert.Equal(1.0, rack.IdleOpacity);
        Assert.Equal(48, rack.IconSize);
        Assert.Equal("#112233", rack.TitleBarColor);
        var record = Assert.Single(Fallbacks("IdleOpacity"));
        Assert.Contains("rack=(global defaults)", record.Detail);
        Assert.EndsWith(@"\" + _reg.RootName + @"\IdleOpacity", record.Source);
    }

    [Fact]
    public void A_bad_global_default_is_recorded_once_however_many_racks_read_it()
    {
        _reg.SeedRoot("IconSize", "huge");
        for (int i = 0; i < 3; i++) _ = new Instance("empty", false);
        Assert.Single(Fallbacks("IconSize"));
    }

    [Fact]
    public void A_global_icon_size_is_clamped_like_the_setter()
    {
        _reg.SeedRoot("IconSize", "5000");
        Assert.Equal(256, new Instance("empty", false).IconSize);
        Assert.Single(Fallbacks("IconSize"));
    }

    [Fact]
    public void Global_defaults_that_are_nonsense_are_refused_one_by_one()
    {
        _reg.SeedRoot("GrayScaleEnabled", "1");
        _reg.SeedRoot("TitleTextAlignment", "9");
        _reg.SeedRoot("SortBy", "-3");
        _reg.SeedRoot("TitleFontSize", "Infinity");
        _reg.SeedRoot("ShowHiddenFiles", "True");
        var rack = new Instance("empty", false);
        Assert.False(rack.GrayScaleEnabled);
        Assert.Equal(System.Windows.HorizontalAlignment.Center, rack.TitleTextAlignment);
        Assert.Equal(1, rack.SortBy);
        Assert.Equal(13.0, rack.TitleFontSize);
        Assert.True(rack.ShowHiddenFiles);
        Assert.Equal(4, Fallbacks().Count);
    }
}
