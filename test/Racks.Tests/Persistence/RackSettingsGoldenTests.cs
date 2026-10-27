using System.IO;
using System.Globalization;
using Microsoft.Win32;

namespace Racks.Tests.Persistence;

/// <summary>
/// Golden compatibility fixtures for the per-rack registry values (hardening P6). Every one of the 67 persisted
/// names gets a NON-default value; the state is produced by each writer the app has (the bulk
/// <c>WriteInstanceToKey</c>, the incremental write-through of <c>Instance</c>, and hand-written raw values in the
/// stored forms old installs and layout imports leave) and read back by the real loader. These tests describe what
/// an existing install MUST keep loading as, so they stay green across the P6b rewrite of the reader.
/// Behaviour that P6b changes on purpose is NOT here: see <see cref="RackSettingsLoadQuirksToday"/>.
/// </summary>
[Collection("Registry")]
public sealed class RackSettingsGoldenTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    [Fact]
    public void The_golden_table_covers_all_67_persisted_names()
    {
        Assert.Equal(67, GoldenRack.Names.Count);
        Assert.Equal(67, GoldenRack.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Every golden value really differs from what a fresh rack has (a "non-default" that equals the default proves nothing).
        var fresh = new Instance("fresh", false);
        foreach (var n in GoldenRack.Names.Where(n => n is not ("Name" or "PosX" or "PosY" or "Width" or "Height")))
            Assert.False(GoldenRack.Same(GoldenRack.NonDefault[n], GoldenRack.Get(fresh, n)), $"{n} is already its default");
    }

    // ---- writer 1: the bulk WriteInstanceToKey ----------------------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-PT")]
    [InlineData("de-DE")]
    public void A_complete_rack_written_by_WriteInstanceToKey_loads_back_identically(string culture)
    {
        RackRegistryFixture.WithCulture(culture, () =>
        {
            var source = GoldenRack.Build();
            _reg.Controller.WriteInstanceToKey(source);

            var loaded = _reg.LoadSingle(GoldenRack.Name);

            Assert.Empty(GoldenRack.Mismatches(loaded));
        });
    }

    [Fact]
    public void The_bulk_writer_stores_each_kind_as_it_always_did()
    {
        RackRegistryFixture.WithCulture("en-US", () =>
        {
            _reg.Controller.WriteInstanceToKey(GoldenRack.Build());

            void Is(string name, RegistryValueKind kind, string text)
            {
                var (data, k) = _reg.RawRack(GoldenRack.Name, name);
                Assert.Equal(kind, k);
                Assert.Equal(text, data is string[] lines ? string.Join("|", lines) : Convert.ToString(data, CultureInfo.InvariantCulture));
            }
            Is("IconSize", RegistryValueKind.DWord, "64");
            Is("GridSize", RegistryValueKind.DWord, "24");
            Is("Opacity", RegistryValueKind.DWord, "77");
            Is("SortBy", RegistryValueKind.DWord, "4");
            Is("FolderOrder", RegistryValueKind.DWord, "2");
            Is("PosX", RegistryValueKind.String, "123.5");
            Is("TitleFontSize", RegistryValueKind.String, "18.5");
            Is("IsLocked", RegistryValueKind.String, "True");
            Is("ShowInGrid", RegistryValueKind.String, "False");
            Is("TitleTextAlignment", RegistryValueKind.String, "Right");
            Is("AssignedFiles", RegistryValueKind.String, @"a.txt|sub dir\c.pdf");
            Is("ShowOnVirtualDesktops", RegistryValueKind.String, "1,3");
            Is("LastAccessedFiles", RegistryValueKind.MultiString, "x.txt|y.txt");
            Is("CustomOrderFiles", RegistryValueKind.MultiString, "a.txt,1|b.txt,2");
            Is("Name", RegistryValueKind.String, GoldenRack.Name);
        });
    }

    // ---- writer 2: the incremental write-through of Instance ----------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-PT")]
    public void A_rack_saved_property_by_property_loads_back_identically(string culture)
    {
        RackRegistryFixture.WithCulture(culture, () =>
        {
            // The two public FIELDS (IsDesktopFilterRack, AssignedFiles) raise no change event: only a bulk save writes them.
            var properties = GoldenRack.Names.Where(n => !GoldenRack.Fields.Contains(n)).ToList();
            GoldenRack.Build(only: properties, persist: true);   // saves the rack once (the Name), then changes it value by value

            var loaded = _reg.LoadSingle(GoldenRack.Name);

            Assert.Empty(GoldenRack.Mismatches(loaded, properties));
            Assert.False(loaded.IsDesktopFilterRack);
            Assert.Empty(loaded.AssignedFiles);
        });
    }

    // ---- hand-written raw values -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US", GoldenRack.RawStyle.Invariant)]
    [InlineData("pt-PT", GoldenRack.RawStyle.Invariant)]   // invariant text read under a comma culture: see the quirks class for what "0.5" does
    [InlineData("en-US", GoldenRack.RawStyle.CultureText)]
    [InlineData("pt-PT", GoldenRack.RawStyle.CultureText)] // "123,5": what a pt-PT install wrote
    [InlineData("en-US", GoldenRack.RawStyle.Qword)]
    [InlineData("pt-PT", GoldenRack.RawStyle.Qword)]
    public void Hand_written_values_in_every_stored_form_load_back(string culture, GoldenRack.RawStyle style)
    {
        RackRegistryFixture.WithCulture(culture, () =>
        {
            _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(style));

            var loaded = _reg.LoadSingle(GoldenRack.Name);

            var mismatches = GoldenRack.Mismatches(loaded);
            if (culture == "pt-PT" && style != GoldenRack.RawStyle.CultureText)
            {
                // "123.5" under pt-PT: not a number there, so today these doubles are ignored. P6b reads invariant text first.
                mismatches = mismatches.Where(m => !m.StartsWith("PosX") && !m.StartsWith("PosY") && !m.StartsWith("Width")
                    && !m.StartsWith("Height") && !m.StartsWith("IdleOpacity") && !m.StartsWith("AnimationSpeed")
                    && !m.StartsWith("MaxGrayScaleStrength") && !m.StartsWith("TitleFontSize")).ToList();
            }
            Assert.Empty(mismatches);
        });
    }

    [Fact]
    public void Each_name_is_applied_on_its_own()
    {
        // The import allow-list accepts exactly these names (RackLayoutImportValidationTests); here each one, alone in an
        // otherwise empty rack, must change the loaded rack.
        foreach (var name in GoldenRack.Names.Where(n => n is not ("Name" or "Folder")))
        {
            string key = "Only-" + name;
            var values = new List<(string, object, RegistryValueKind)>
            {
                ("Name", key, RegistryValueKind.String),
                ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            };
            values.AddRange(GoldenRack.RawAll(GoldenRack.RawStyle.Invariant, new[] { name }));
            _reg.SeedRack(key, values);

            var loaded = _reg.LoadSingle(key);
            Assert.Empty(GoldenRack.Mismatches(loaded, new[] { name }));
            _reg.Controller.Instances.Clear();
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_reg.RackPath(key));
        }
    }

    [Fact]
    public void A_lower_case_bool_and_a_padded_number_are_accepted()
    {
        _reg.SeedRack("Lax", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Lax", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("IsLocked", "true", RegistryValueKind.String),
            ("PinToTop", " True ", RegistryValueKind.String),
            ("IconSize", " 48 ", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("Lax");
        Assert.True(rack.IsLocked);
        Assert.True(rack.PinToTop);
        Assert.Equal(48, rack.IconSize);
    }

    // ---- identity ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_rack_name_comes_from_the_Name_value_not_from_the_key()
    {
        _reg.SeedRack("key-name", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Value Name", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
        });
        var rack = Assert.Single(_reg.Load());
        Assert.Equal("Value Name", rack.Name);
    }

    [Fact]
    public void A_rack_named_empty_is_the_placeholder_and_is_never_loaded()
    {
        _reg.SeedRack("empty", new (string, object, RegistryValueKind)[]
        {
            ("Name", "empty", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
        });
        Assert.Empty(_reg.Load());
    }

    [Fact]
    public void The_legacy_MoveFilesOnDrop_value_is_read_and_discarded()
    {
        _reg.SeedRack("Legacy", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Legacy", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("MoveFilesOnDrop", "True", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("Legacy");
        Assert.False(rack.LinkOnDrop);   // MoveFilesOnDrop=True used to mean "move": that is the default now
    }

    [Fact]
    public void Unknown_value_names_are_ignored()
    {
        _reg.SeedRack("Odd", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Odd", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("IsShortcutsOnly", "True", RegistryValueKind.String),   // written by the incremental path, never read back
            ("SomethingNew", "x", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("Odd");
        Assert.False(rack.IsShortcutsOnly);
    }

    // ---- garbage that every version ignores ------------------------------------------------------------------------------

    [Theory]
    [InlineData("PosX", "abc")]
    [InlineData("Width", "")]
    [InlineData("IdleOpacity", "x1")]
    [InlineData("TitleFontSize", "big")]
    [InlineData("IconSize", "huge")]
    [InlineData("GridSize", "1.5")]
    [InlineData("Opacity", "")]
    [InlineData("IsLocked", "yes")]
    [InlineData("ShowInGrid", "1")]
    [InlineData("LinkOnDrop", "")]
    [InlineData("TitleTextAlignment", "Diagonal")]
    [InlineData("TitleBarColor", "ContextColor file://attacker.example/share/p.icc 1,0,0,0")]
    [InlineData("TitleFontFamily", @"\\attacker.example\share\#Evil")]
    [InlineData("ItemFontFamily", "file://attacker.example/share/#Evil")]
    public void An_unusable_value_leaves_the_default_in_place(string name, string garbage)
    {
        var defaults = new Instance("fresh", false);
        _reg.SeedRack("G", new (string, object, RegistryValueKind)[]
        {
            ("Name", "G", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            (name, garbage, RegistryValueKind.String),
        });

        var rack = _reg.LoadSingle("G");

        Assert.True(GoldenRack.Same(GoldenRack.Get(defaults, name), GoldenRack.Get(rack, name)), $"{name} should have stayed at its default");
    }

    [Fact]
    public void The_virtual_desktop_list_with_one_bad_item_is_dropped_whole_today_but_others_still_load()
    {
        // Documented current behaviour of the list (P6b keeps the good items). The rack itself and its other values load.
        _reg.SeedRack("VD", new (string, object, RegistryValueKind)[]
        {
            ("Name", "VD", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("ShowOnVirtualDesktops", "1,x,3", RegistryValueKind.String),
            ("IsLocked", "True", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("VD");
        Assert.True(rack.IsLocked);
        Assert.NotNull(rack); // list handling: see RackSettingsLoadQuirksToday
    }

    [Theory]
    [InlineData("1,3", new[] { 1, 3 })]
    [InlineData("1;3", new[] { 1, 3 })]
    [InlineData("1 3", new[] { 1, 3 })]
    [InlineData("2", new[] { 2 })]
    [InlineData("", new int[0])]
    public void The_virtual_desktop_list_accepts_its_separators(string text, int[] expected)
    {
        _reg.SeedRack("VD2", new (string, object, RegistryValueKind)[]
        {
            ("Name", "VD2", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("ShowOnVirtualDesktops", text, RegistryValueKind.String),
        });
        Assert.Equal(expected, _reg.LoadSingle("VD2").ShowOnVirtualDesktops);
    }

    [Fact]
    public void Malformed_custom_order_items_are_dropped_and_the_rest_kept()
    {
        _reg.SeedRack("CO", new (string, object, RegistryValueKind)[]
        {
            ("Name", "CO", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("CustomOrderFiles", new[] { "a.txt,1", "no-comma", "b.txt,2" }, RegistryValueKind.MultiString),
        });
        Assert.Equal(new[] { Tuple.Create("a.txt", "1"), Tuple.Create("b.txt", "2") }, _reg.LoadSingle("CO").CustomOrderFiles);
    }

    [Fact]
    public void A_list_stored_with_the_wrong_kind_is_ignored()
    {
        _reg.SeedRack("WK", new (string, object, RegistryValueKind)[]
        {
            ("Name", "WK", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("LastAccessedFiles", "a.txt", RegistryValueKind.String),
            ("CustomOrderFiles", "a.txt,1", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("WK");
        Assert.Empty(rack.LastAccessedFiles);
        Assert.Empty(rack.CustomOrderFiles);
    }

    // ---- several racks -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Several_good_racks_all_load()
    {
        foreach (var name in new[] { "A", "B", "C" })
            _reg.SeedRack(name, new (string, object, RegistryValueKind)[]
            {
                ("Name", name, RegistryValueKind.String),
                ("Folder", Path.GetTempPath(), RegistryValueKind.String),
                ("PosX", "10", RegistryValueKind.String),
            });
        Assert.Equal(new[] { "A", "B", "C" }, _reg.Load().Select(r => r.Name).Order().ToArray());
    }

    // ---- the post pass ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_rack_whose_folder_is_gone_is_kept_and_flagged()
    {
        _reg.SeedRack("Gone", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Gone", RegistryValueKind.String),
            ("Folder", Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")), RegistryValueKind.String),
            ("PosX", "10", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("Gone");
        Assert.True(rack.IsFolderMissing);
    }

    [Fact]
    public void A_rack_that_points_at_the_Desktop_is_a_desktop_filter_rack()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _reg.SeedRack("Dsk", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Dsk", RegistryValueKind.String),
            ("Folder", desktop, RegistryValueKind.String),
        });
        Assert.True(_reg.LoadSingle("Dsk").IsDesktopFilterRack);
    }

    [Fact]
    public void The_two_one_shot_migrations_flip_their_values_and_persist_them()
    {
        string virtualRoot = Path.Combine(InstanceController.VirtualFramesRoot, "Mig");
        string tempFolder = Path.GetTempPath();
        _reg.SeedRack("Mig", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Mig", RegistryValueKind.String),
            ("Folder", virtualRoot, RegistryValueKind.String),
            ("LinkOnDrop", "True", RegistryValueKind.String),
            ("FolderOpenInsideFrame", "True", RegistryValueKind.String),
        });
        _reg.SeedRack("Plain", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Plain", RegistryValueKind.String),
            ("Folder", tempFolder, RegistryValueKind.String),
            ("LinkOnDrop", "True", RegistryValueKind.String),
            ("FolderOpenInsideFrame", "True", RegistryValueKind.String),
        });

        var racks = _reg.Load(revertMigration: true, folderOpenMigration: true);

        var mig = racks.Single(r => r.Name == "Mig");
        var plain = racks.Single(r => r.Name == "Plain");
        Assert.False(mig.LinkOnDrop);                 // virtual rack: link-on-drop reverted to move
        Assert.True(plain.LinkOnDrop);                // folder rack: untouched
        Assert.False(mig.FolderOpenInsideFrame);      // every rack: explorer navigation
        Assert.False(plain.FolderOpenInsideFrame);
        Assert.Equal("False", _reg.RawRack("Mig", "LinkOnDrop").Data);
        Assert.Equal("False", _reg.RawRack("Plain", "FolderOpenInsideFrame").Data);
        Assert.Equal("True", _reg.RawRack("Plain", "LinkOnDrop").Data);
    }

    // ---- global style defaults ----------------------------------------------------------------------------------------------------

    private static readonly (string Name, string Text, object Expected)[] GlobalDefaults =
    {
        ("IdleOpacity", "0.4", 0.4), ("AnimationSpeed", "0.5", 0.5), ("MaxGrayScaleStrength", "0.3", 0.3),
        ("GrayScaleEnabled", "True", true), ("GrayScaleEnabled_InactiveOnly", "True", true),
        ("TitleFontFamily", "Consolas", "Consolas"), ("ItemFontFamily", "Arial", "Arial"),
        ("ShowHiddenFiles", "True", true), ("ShowFileExtension", "True", true), ("ShowFileExtensionIcon", "True", true),
        ("ShowHiddenFilesIcon", "True", true), ("ShowDisplayName", "False", false),
        ("BorderEnabled", "True", true), ("ActiveBorderEnabled", "True", true),
        ("ActiveBackgroundEnabled", "True", true), ("ActiveTitleTextEnabled", "True", true),
        ("TitleTextAlignment", "Left", System.Windows.HorizontalAlignment.Left),
        ("FileFilterRegex", "^keep", "^keep"), ("FileFilterHideRegex", "^hide", "^hide"),
        ("TitleBarColor", "#112233", "#112233"), ("TitleTextColor", "#223344", "#223344"),
        ("ListViewBackgroundColor", "#44556677", "#44556677"), ("ActiveBackgroundColor", "#556677", "#556677"),
        ("ActiveTitleTextColor", "#778899", "#778899"), ("ActiveBorderColor", "#667788", "#667788"),
        ("ListViewFontColor", "#8899AA", "#8899AA"), ("ListViewFontShadowColor", "#99AABB", "#99AABB"),
        ("Opacity", "90", 90), ("SortBy", "3", 3), ("FolderOrder", "1", 1),
        ("TitleFontSize", "20", 20.0), ("IconSize", "48", 48),
    };

    [Fact]
    public void There_are_32_global_style_defaults_and_each_reaches_a_new_rack()
    {
        Assert.Equal(32, GlobalDefaults.Length);
        foreach (var (name, text, _) in GlobalDefaults) _reg.SeedRoot(name, text);

        var rack = new Instance("empty", false);

        foreach (var (name, _, expected) in GlobalDefaults)
            Assert.True(GoldenRack.Same(expected, GoldenRack.Get(rack, name)), $"{name}: expected {expected}, got {GoldenRack.Show(GoldenRack.Get(rack, name))}");
    }

    [Fact]
    public void Global_defaults_also_reach_a_default_style_instance_and_a_plain_rack_ignores_them()
    {
        _reg.SeedRoot("IconSize", "48");
        _reg.SeedRoot("TitleBarColor", "#112233");

        var defaultStyle = new Instance("Default Style", true);
        var plain = new Instance("plain", false);

        Assert.Equal(48, defaultStyle.IconSize);
        Assert.Equal("#112233", defaultStyle.TitleBarColor);
        Assert.Equal(32, plain.IconSize);
        Assert.Equal("#0C000000", plain.TitleBarColor);
    }

    [Fact]
    public void Unsafe_fonts_and_colors_stored_as_global_defaults_never_reach_a_new_rack()
    {
        _reg.SeedRoot("TitleFontFamily", @"\\attacker.example\share\#Evil");
        _reg.SeedRoot("ItemFontFamily", "file://attacker.example/share/#Evil");
        foreach (var color in new[]
                 {
                     "TitleBarColor", "TitleTextColor", "ListViewBackgroundColor", "ActiveBackgroundColor",
                     "ActiveTitleTextColor", "ActiveBorderColor", "ListViewFontColor", "ListViewFontShadowColor",
                 })
            _reg.SeedRoot(color, "ContextColor file://attacker.example/share/p.icc 1.0,0.5,0.5,0.5");

        var rack = new Instance("empty", false);
        var fresh = new Instance("fresh", false);

        Assert.Equal("Segoe UI", rack.TitleFontFamily);
        Assert.Equal("Segoe UI", rack.ItemFontFamily);
        foreach (var color in new[]
                 {
                     "TitleBarColor", "TitleTextColor", "ListViewBackgroundColor", "ActiveBackgroundColor",
                     "ActiveTitleTextColor", "ActiveBorderColor", "ListViewFontColor", "ListViewFontShadowColor",
                 })
            Assert.Equal(GoldenRack.Get(fresh, color), GoldenRack.Get(rack, color));
    }

    [Fact]
    public void Unsafe_fonts_and_colors_stored_on_a_rack_are_ignored_on_load()
    {
        _reg.SeedRack("Evil", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Evil", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("TitleFontFamily", @"\\attacker.example\share\#Evil", RegistryValueKind.String),
            ("ItemFontFamily", "file://attacker.example/share/#Evil", RegistryValueKind.String),
            ("BorderColor", "ContextColor \\\\attacker.example\\share\\p.icc 1.0,0.5,0.5,0.5", RegistryValueKind.String),
            ("ListViewFontColor", "#GGG", RegistryValueKind.String),
            ("TitleBarColor", "#112233", RegistryValueKind.String),
        });

        var rack = _reg.LoadSingle("Evil");

        Assert.Equal("Segoe UI", rack.TitleFontFamily);
        Assert.Equal("Segoe UI", rack.ItemFontFamily);
        Assert.Equal("#FFFFFF", rack.BorderColor);
        Assert.Equal("#FFFFFF", rack.ListViewFontColor);
        Assert.Equal("#112233", rack.TitleBarColor);   // a good value next to the bad ones still loads
    }

    [Fact]
    public void A_background_image_on_a_network_share_loads_as_text_and_is_refused_where_it_is_used()
    {
        // The setter keeps the text; the gate is SafeWpfValues.IsLocalDrivePath in RackWindow's background code.
        _reg.SeedRack("Bg", new (string, object, RegistryValueKind)[]
        {
            ("Name", "Bg", RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("BackgroundImagePath", @"\\attacker.example\share\bg.png", RegistryValueKind.String),
        });
        var rack = _reg.LoadSingle("Bg");
        Assert.False(Racks.Util.SafeWpfValues.IsLocalDrivePath(rack.BackgroundImagePath));
    }

    // ---- loading is repeatable --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Loading_twice_gives_the_same_racks()
    {
        RackRegistryFixture.WithCulture("pt-PT", () =>
        {
            _reg.SeedRack(GoldenRack.Name, GoldenRack.RawAll(GoldenRack.RawStyle.CultureText));
            var first = _reg.LoadSingle(GoldenRack.Name);
            var second = _reg.LoadSingle(GoldenRack.Name);
            Assert.Empty(GoldenRack.Mismatches(first));
            Assert.Empty(GoldenRack.Mismatches(second));
        });
    }
}

/// <summary>
/// Behaviour the CURRENT loader has that hardening P6b changes on purpose. Each test pins today's result and says what
/// replaces it; in P6b this class is replaced by the validation tests (<c>RackSettingsRepositoryTests</c>).
/// </summary>
[Collection("Registry")]
public sealed class RackSettingsLoadQuirksToday : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    private static IEnumerable<(string, object, RegistryValueKind)> Rack(string name, params (string, object, RegistryValueKind)[] more)
        => new (string, object, RegistryValueKind)[]
        {
            ("Name", name, RegistryValueKind.String),
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
        }.Concat(more);

    // INTENTIONAL CHANGE in P6b: one rack that makes the reader throw aborts the whole load (and, because the same
    // try also creates the windows, no rack of the session is shown). P6b skips only that rack and audits it.
    [Fact]
    public void Today_one_rack_that_makes_the_reader_throw_stops_all_the_racks_after_it()
    {
        // Registry keys come back in name order: "A-*" first, then the poisoned one, then "Z-*".
        _reg.SeedRack("A-good", Rack("A-good"));
        // A NUL inside the folder text makes Path.GetFullPath throw in the post pass.
        _reg.SeedRack("B-poison", Rack("B-poison", ("Folder", "C:\\a\0b", RegistryValueKind.String)));
        _reg.SeedRack("Z-good", Rack("Z-good"));

        _reg.Controller.Instances.Clear();
        var error = Record.Exception(() => _reg.Controller.LoadInstancesFromRegistry(false, false));

        Assert.NotNull(error);                                                      // InitInstances' try/catch swallows it and shows no window
        Assert.Equal(new[] { "A-good" }, _reg.Controller.Instances.Select(r => r.Name).ToArray());   // Z-good never loaded
    }

    // INTENTIONAL CHANGE in P6b: loading writes back while it reads, because the rack being filled is a persisting
    // Instance. The value KIND flips (REG_DWORD -> REG_SZ) and doubles are rewritten in the current culture's text.
    [Fact]
    public void Today_loading_rewrites_values_it_just_read()
    {
        RackRegistryFixture.WithCulture("pt-PT", () =>
        {
            _reg.SeedRack("RW", new (string, object, RegistryValueKind)[]
            {
                ("Name", "RW", RegistryValueKind.String),     // Name first: later values are written back
                ("Folder", Path.GetTempPath(), RegistryValueKind.String),
                ("IconSize", 64, RegistryValueKind.DWord),
                ("PosX", "123.5", RegistryValueKind.String),  // invariant text; pt-PT cannot read it (see the golden test)
                ("Width", "321,75", RegistryValueKind.String),
                ("IsLocked", "True", RegistryValueKind.String),
            });

            _reg.LoadSingle("RW");

            Assert.Equal((RegistryValueKind.String, "64"), (_reg.RawRack("RW", "IconSize").Kind!.Value, _reg.RawRack("RW", "IconSize").Data));
            Assert.Equal("321,75", _reg.RawRack("RW", "Width").Data);                 // rewritten with the pt-PT comma
        });
    }

    // INTENTIONAL CHANGE in P6b: the bulk writer stores doubles with the current culture, so a pt-PT install writes "123,5".
    [Fact]
    public void Today_the_bulk_writer_writes_doubles_in_the_current_culture()
    {
        RackRegistryFixture.WithCulture("pt-PT", () =>
        {
            _reg.Controller.WriteInstanceToKey(GoldenRack.Build());
            Assert.Equal("123,5", _reg.RawRack(GoldenRack.Name, "PosX").Data);
        });
    }

    // INTENTIONAL CHANGE in P6b: an undefined enum number is applied; NaN and Infinity are applied; out-of-range numbers are applied.
    [Fact]
    public void Today_nonsense_numbers_and_enums_are_applied_as_they_are()
    {
        _reg.SeedRack("Wild", Rack("Wild",
            ("TitleTextAlignment", "7", RegistryValueKind.String),
            ("PosX", "NaN", RegistryValueKind.String),
            ("PosY", "1e999", RegistryValueKind.String),
            ("IdleOpacity", "5", RegistryValueKind.String),
            ("Opacity", "9999", RegistryValueKind.String),
            ("SortBy", "-4", RegistryValueKind.String)));

        var rack = _reg.LoadSingle("Wild");

        Assert.Equal(7, (int)rack.TitleTextAlignment);
        Assert.True(double.IsNaN(rack.PosX));
        Assert.True(double.IsPositiveInfinity(rack.PosY));
        Assert.Equal(5.0, rack.IdleOpacity);
        Assert.Equal(9999, rack.Opacity);
        Assert.Equal(-4, rack.SortBy);
    }

    // INTENTIONAL CHANGE in P6b: one unusable item drops the whole list; P6b keeps the good items.
    [Fact]
    public void Today_one_bad_virtual_desktop_item_drops_the_whole_list()
    {
        _reg.SeedRack("VD", Rack("VD", ("ShowOnVirtualDesktops", "1,x,3", RegistryValueKind.String)));
        Assert.Null(_reg.LoadSingle("VD").ShowOnVirtualDesktops);
    }

    // INTENTIONAL CHANGE in P6b: a rack without a Name value gets no identity at all (empty key location).
    [Fact]
    public void Today_a_rack_without_a_Name_value_has_an_empty_name()
    {
        _reg.SeedRack("NoName", new (string, object, RegistryValueKind)[]
        {
            ("Folder", Path.GetTempPath(), RegistryValueKind.String),
            ("PosX", "10", RegistryValueKind.String),
        });
        var rack = Assert.Single(_reg.Load());
        Assert.Equal("", rack.Name);
        Assert.Equal("", rack.GetKeyLocation());
    }

    // INTENTIONAL CHANGE in P6b: the global defaults are read in one try, so the first bad value skips every later default.
    [Fact]
    public void Today_one_bad_global_default_skips_all_the_ones_after_it()
    {
        _reg.SeedRoot("IdleOpacity", "not a number");   // first in the list
        _reg.SeedRoot("IconSize", "48");                // last in the list

        var rack = new Instance("empty", false);

        Assert.Equal(32, rack.IconSize);                // 48 was never applied
    }

    // INTENTIONAL CHANGE in P6b: the global IconSize is applied without the 16-256 clamp the setter has.
    [Fact]
    public void Today_a_global_icon_size_is_not_clamped()
    {
        _reg.SeedRoot("IconSize", "5000");
        Assert.Equal(5000, new Instance("empty", false).IconSize);
    }
}
