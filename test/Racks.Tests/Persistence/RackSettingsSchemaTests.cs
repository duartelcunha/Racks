using System.IO;
using Racks.Infrastructure.Registry;
using Racks.Util;

namespace Racks.Tests.Persistence;

/// <summary>
/// The schema is the one table behind the loader, the writers, the import allow-list and the global defaults (INV-REG-2).
/// These tests keep it honest without reading any source text: every row must have a typed DTO property and an
/// <c>Instance</c> member of the right type, a default equal to what a fresh rack has, and must survive a round trip.
/// </summary>
[Collection("Registry")]
public sealed class RackSettingsSchemaTests : IDisposable
{
    private readonly RackRegistryFixture _reg = new();

    public void Dispose() => _reg.Dispose();

    private static Type ExpectedClrType(SettingDescriptor d) => d.Kind switch
    {
        SettingKind.Double => typeof(double),
        SettingKind.Int => typeof(int),
        SettingKind.Bool => typeof(bool),
        SettingKind.Enum => d.EnumType!,
        SettingKind.String or SettingKind.Path or SettingKind.RackName or SettingKind.FontFamily or SettingKind.Color => typeof(string),
        SettingKind.MultiString or SettingKind.PipeList => typeof(List<string>),
        SettingKind.IntList => typeof(int[]),
        SettingKind.PairList => typeof(List<Tuple<string, string>>),
        _ => throw new InvalidOperationException(d.Name),
    };

    [Fact]
    public void The_schema_has_67_persisted_values_one_legacy_name_and_32_global_defaults()
    {
        Assert.Equal(67, RackSettingsSchema.Persisted.Count());
        Assert.Equal(68, RackSettingsSchema.All.Count);
        Assert.Equal(68, RackSettingsSchema.All.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(new[] { "MoveFilesOnDrop" }, RackSettingsSchema.All.Where(d => !d.IsPersisted).Select(d => d.Name).ToArray());
        Assert.Equal(32, RackSettingsSchema.GlobalDefaults.Count());
        Assert.All(RackSettingsSchema.GlobalDefaults, d => Assert.True(d.IsPersisted));
        // None of the rack-only values is a global default.
        Assert.DoesNotContain(RackSettingsSchema.GlobalDefaults, d => Instance.NotGlobalProperties.Contains(d.Name));
    }

    [Fact]
    public void The_persisted_names_are_exactly_the_golden_table()
    {
        Assert.Equal(GoldenRack.Names, RackSettingsSchema.Persisted.Select(d => d.Name).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Every_descriptor_has_a_dto_property_and_an_Instance_member_of_the_right_type()
    {
        foreach (var d in RackSettingsSchema.Persisted)
        {
            var expected = ExpectedClrType(d);
            Assert.True(RackSettings.HasProperty(d.Name), $"{d.Name}: no RackSettings property");
            Assert.True(RackSettingsMapper.HasMember(d.Name), $"{d.Name}: no Instance member");
            Assert.Equal(expected, RackSettings.PropertyType(d.Name));
            Assert.Equal(expected, RackSettingsMapper.MemberType(d.Name));
        }
        // and the DTO has nothing the schema does not know
        var dtoNames = typeof(RackSettings).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Equal(RackSettingsSchema.Persisted.Select(d => d.Name).Order().ToList(), dtoNames.Order().ToList());
    }

    [Fact]
    public void Every_default_is_what_a_fresh_rack_has()
    {
        var fresh = new Instance("fresh", false);
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Default != null && d.Name != "Name"))   // a rack gets its name from its constructor
            Assert.True(GoldenRack.Same(d.Default, GoldenRack.Get(fresh, d.Name)), $"{d.Name}: schema says {GoldenRack.Show(d.Default)}, a fresh rack has {GoldenRack.Show(GoldenRack.Get(fresh, d.Name))}");
        // lists start empty and the screen-dependent values are not fixed numbers
        Assert.Empty(fresh.AssignedFiles);
        Assert.Empty(fresh.LastAccessedFiles);
        Assert.Empty(fresh.CustomOrderFiles);
        Assert.Null(fresh.ShowOnVirtualDesktops);
        foreach (var name in new[] { "PosX", "PosY" }) Assert.Null(RackSettingsSchema.Persisted.Single(d => d.Name == name).Default);
    }

    [Fact]
    public void Limits_are_sane_and_clamping_is_only_where_the_setter_clamps()
    {
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Range != RangePolicy.None))
        {
            Assert.True(d.Kind is SettingKind.Double or SettingKind.Int, d.Name);
            Assert.True(d.Min < d.Max, d.Name);
            if (d.Default is IConvertible c) Assert.InRange(Convert.ToDouble(c), d.Min, d.Max);
        }
        var clamped = RackSettingsSchema.Persisted.Where(d => d.Range == RangePolicy.Clamp).ToList();
        Assert.Equal(new[] { "GridSize", "IconSize" }, clamped.Select(d => d.Name).Order().ToArray());

        // The limits equal what the Instance setters do on their own.
        using var rack = new ClampProbe();
        foreach (var d in clamped)
        {
            GoldenRack.Set(rack.Rack, d.Name, int.MaxValue);
            Assert.Equal((int)d.Max, (int)GoldenRack.Get(rack.Rack, d.Name)!);
            GoldenRack.Set(rack.Rack, d.Name, int.MinValue);
            Assert.Equal((int)d.Min, (int)GoldenRack.Get(rack.Rack, d.Name)!);
        }
    }

    private sealed class ClampProbe : IDisposable
    {
        public Instance Rack { get; } = new("probe", false);
        private readonly IDisposable _scope;
        public ClampProbe() => _scope = Rack.SuspendPersistence();
        public void Dispose() => _scope.Dispose();
    }

    [Fact]
    public void Fonts_and_colors_in_the_schema_are_exactly_the_members_whose_setters_refuse_unsafe_text()
    {
        const string Unsafe = @"\\attacker.example\share\#Evil ContextColor file://x";
        var safeKinds = RackSettingsSchema.Persisted
            .Where(d => d.Kind is SettingKind.FontFamily or SettingKind.Color)
            .Select(d => d.Name).ToHashSet();

        var refusing = new HashSet<string>();
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Kind == SettingKind.String || safeKinds.Contains(d.Name)))
        {
            var rack = new Instance("probe", false);
            using var noWrites = rack.SuspendPersistence();
            var before = GoldenRack.Get(rack, d.Name);
            GoldenRack.Set(rack, d.Name, Unsafe);
            if (Equals(before, GoldenRack.Get(rack, d.Name))) refusing.Add(d.Name);
        }
        Assert.Equal(safeKinds.Order().ToList(), refusing.Order().ToList());
    }

    [Fact]
    public void Every_value_survives_instance_to_settings_to_instance_without_sharing_lists()
    {
        var source = GoldenRack.Build();
        var settings = RackSettingsMapper.ToSettings(source);
        foreach (var d in RackSettingsSchema.Persisted) Assert.NotNull(settings.Get(d.Name));

        var copy = RackSettingsMapper.ToInstance(settings);
        Assert.Empty(GoldenRack.Mismatches(copy));
        Assert.Equal(GoldenRack.Name, copy.Name);

        Assert.NotSame(source.AssignedFiles, copy.AssignedFiles);
        Assert.NotSame(source.LastAccessedFiles, copy.LastAccessedFiles);
        Assert.NotSame(source.CustomOrderFiles, copy.CustomOrderFiles);
        Assert.NotSame(source.ShowOnVirtualDesktops, copy.ShowOnVirtualDesktops);
        Assert.NotSame(settings.AssignedFiles, copy.AssignedFiles);
    }

    [Fact]
    public void Applying_settings_never_writes_to_the_registry()
    {
        var settings = RackSettingsMapper.ToSettings(GoldenRack.Build());
        string before = _reg.SnapshotAll();
        _ = RackSettingsMapper.ToInstance(settings);
        Assert.Equal(before, _reg.SnapshotAll());
        Assert.Empty(_reg.Audit.Events);
    }

    [Fact]
    public void Every_value_round_trips_through_the_registry_on_its_own()
    {
        // Write each value alone with the full-save writer, read it back with the real loader, and compare the typed value.
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Name != "Name" && d.Name != "Folder"))
        {
            var rack = GoldenRack.Build("Solo", only: new[] { d.Name });
            _reg.Controller.WriteInstanceToKey(rack);
            var loaded = _reg.LoadSingle("Solo");
            Assert.Empty(GoldenRack.Mismatches(loaded, new[] { d.Name }));
            _reg.Controller.Instances.Clear();
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_reg.RackPath("Solo"));
        }
    }

    [Fact]
    public void The_import_allow_list_is_the_schema()
    {
        var accepted = RackSettingsSchema.All.Where(d => d.ImportAccepted).Select(d => d.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(accepted, RackLayoutIO.AllowedValueNames.Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("MoveFilesOnDrop", accepted);                        // the discarded legacy name stays importable
        Assert.Equal(68, accepted.Length);
    }

    [Theory]
    [InlineData("Folder", ImportSection.Path)]
    [InlineData("BackgroundImagePath", ImportSection.Path)]
    [InlineData("Name", ImportSection.RackName)]
    [InlineData("TitleFontFamily", ImportSection.FontFamily)]
    [InlineData("BorderColor", ImportSection.Color)]
    [InlineData("LastAccessedFiles", ImportSection.MultiString)]
    [InlineData("CustomOrderFiles", ImportSection.MultiString)]
    [InlineData("PosX", ImportSection.Scalar)]
    [InlineData("AssignedFiles", ImportSection.Scalar)]
    [InlineData("ShowOnVirtualDesktops", ImportSection.Scalar)]
    [InlineData("TitleTextAlignment", ImportSection.Scalar)]
    [InlineData("MoveFilesOnDrop", ImportSection.Scalar)]
    public void Each_kind_maps_to_the_import_section_it_always_had(string name, ImportSection expected)
    {
        Assert.True(RackSettingsSchema.TryGet(name, out var d));
        Assert.Equal(expected, d.ImportSection);
    }

    [Theory]
    [InlineData("Work", true)]
    [InlineData("My Rack (2)", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    [InlineData(@"a\b", false)]
    [InlineData("a/b", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    public void The_rack_name_rule_is_the_one_import_uses(string? name, bool valid)
    {
        Assert.Equal(valid, RackSettingsSchema.IsValidRackName(name));
        if (name == null) return;
        // Import rejects exactly the same names (a bad name anywhere rejects the file).
        string json = System.Text.Json.JsonSerializer.Serialize(new { Racks = new[] { new { Name = name, Values = new { } } } });
        if (valid) Assert.Single(RackLayoutIO.ParseAndValidate(json));
        else Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate(json));
    }

    [Fact]
    public void Names_are_looked_up_without_regard_to_case_like_the_registry()
    {
        Assert.True(RackSettingsSchema.TryGet("posx", out var d));
        Assert.Equal("PosX", d.Name);
        Assert.False(RackSettingsSchema.TryGet("Nope", out _));
        Assert.False(RackSettingsSchema.TryGet(null, out _));
    }

    [Fact]
    public void The_validator_and_the_safe_values_agree_on_fonts_and_colors()
    {
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Kind == SettingKind.FontFamily))
        {
            Assert.Equal(ParseStatus.Accepted, RackSettingsValidator.Parse(d, Str("Segoe UI Variable Display, Segoe UI")).Status);
            Assert.Equal(ParseStatus.Rejected, RackSettingsValidator.Parse(d, Str(@"\\host\share\#Font")).Status);
            Assert.Equal(ParseStatus.Absent, RackSettingsValidator.Parse(d, Str("")).Status);
            Assert.True(SafeWpfValues.IsSafeFontFamily("Segoe UI"));
        }
        foreach (var d in RackSettingsSchema.Persisted.Where(d => d.Kind == SettingKind.Color))
        {
            Assert.Equal(ParseStatus.Accepted, RackSettingsValidator.Parse(d, Str("#0C000000")).Status);
            Assert.Equal(ParseStatus.Rejected, RackSettingsValidator.Parse(d, Str("ContextColor file://x/y.icc 1,0,0,0")).Status);
            Assert.Equal(ParseStatus.Absent, RackSettingsValidator.Parse(d, Str("")).Status);
        }
    }

    private static RegistryValue Str(string text) => new(text, Microsoft.Win32.RegistryValueKind.String);
}
