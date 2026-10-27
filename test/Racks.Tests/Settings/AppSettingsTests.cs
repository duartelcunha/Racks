using Microsoft.Win32;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;

namespace Racks.Tests.Settings;

[Collection("Registry")]
public sealed class AppSettingsTests : IDisposable
{
    private const string RunName = "RacksTestApp";
    private readonly ThrowawayRegistry _reg = new();
    private readonly RecordingAuditSink _audit = new();

    public void Dispose() => _reg.Dispose();

    private AppSettings Settings() => new(_reg.Root, _reg.Run, () => RunName, _audit);

    private static AppSettings Settings(IRegistryRoot root, IRegistryRoot run, IAuditSink audit) =>
        new(root, run, () => RunName, audit);

    // name -> (getter, setter, default). One table drives the per-setting tests.
    public static IEnumerable<object[]> BoolSettings() =>
    [
        [AppSettings.DoubleClickToHideName, false],
        [AppSettings.IcePhysicsName, true],
        [AppSettings.HotCornerHideName, false],
        [AppSettings.AutoUpdateName, false],
        [AppSettings.HideDesktopIconsName, false],
    ];

    public static IEnumerable<object[]> BoolSettingNames() => BoolSettings().Select(row => new[] { row[0] });

    private static bool Get(AppSettings s, string name) => name switch
    {
        AppSettings.DoubleClickToHideName => s.DoubleClickToHide,
        AppSettings.IcePhysicsName => s.IcePhysics,
        AppSettings.HotCornerHideName => s.HotCornerHide,
        AppSettings.AutoUpdateName => s.AutoUpdate,
        AppSettings.HideDesktopIconsName => s.HideDesktopIcons ?? false,
        AppSettings.StartOnLoginName => s.StartOnLogin,
        _ => throw new ArgumentException(name),
    };

    private static bool Set(AppSettings s, string name, bool value) => name switch
    {
        AppSettings.DoubleClickToHideName => s.TrySetDoubleClickToHide(value),
        AppSettings.IcePhysicsName => s.TrySetIcePhysics(value),
        AppSettings.HotCornerHideName => s.TrySetHotCornerHide(value),
        AppSettings.AutoUpdateName => s.TrySetAutoUpdate(value),
        AppSettings.HideDesktopIconsName => s.TrySetHideDesktopIcons(value),
        _ => throw new ArgumentException(name),
    };

    // ---- defaults ----------------------------------------------------------------------------

    [Fact]
    public void Missing_values_give_the_documented_defaults_and_no_audit_record()
    {
        var s = Settings();
        Assert.False(s.StartOnLogin);
        Assert.False(s.DoubleClickToHide);
        Assert.False(s.HotCornerHide);
        Assert.False(s.AutoUpdate);
        Assert.Null(s.HideDesktopIcons);     // never saved: the caller leaves its toggle alone
        Assert.Null(s.BlurBackground);
        Assert.Null(s.ReadMainHwnd());
        Assert.Empty(_audit.Events);         // a fresh install is normal, not a finding
    }

    [Fact]
    public void Ice_physics_is_on_unless_the_user_turned_it_off()
    {
        var s = Settings();
        Assert.True(s.IcePhysics);                                  // nothing saved
        Assert.True(s.TrySetIcePhysics(false));
        Assert.False(s.IcePhysics);
        Assert.True(s.TrySetIcePhysics(true));
        Assert.True(s.IcePhysics);
    }

    // ---- round trips and the stored form -----------------------------------------------------

    [Theory]
    [MemberData(nameof(BoolSettings))]
    public void Every_switch_round_trips(string name, bool defaultOn)
    {
        var s = Settings();
        Assert.Equal(defaultOn, Get(s, name));
        Assert.True(Set(s, name, true));
        Assert.True(Get(s, name));
        Assert.True(Set(s, name, false));
        Assert.False(Get(s, name));
        Assert.Empty(_audit.Events);
    }

    [Theory]
    [MemberData(nameof(BoolSettingNames))]
    public void A_switch_is_stored_as_the_same_text_the_old_helper_wrote(string name)
    {
        // Old code: RegistryHelper.WriteToRegistryRoot(name, true|false|bool? boxed) = RegistryKey.SetValue(name, bool).
        string oldRoot = "RacksTests-" + Guid.NewGuid().ToString("N");
        try
        {
            var old = new RegistryHelper(oldRoot);
            foreach (bool value in new[] { true, false })
            {
                old.WriteToRegistryRoot(name, value);
                var (oldData, oldKind) = ThrowawayRegistry.Raw(@"SOFTWARE\" + oldRoot, name);

                Assert.True(Set(Settings(), name, value));
                var (newData, newKind) = _reg.RawRoot(name);

                Assert.Equal(oldKind, newKind);
                Assert.Equal(RegistryValueKind.String, newKind);
                Assert.Equal(oldData, newData);
                Assert.Equal(value ? "True" : "False", newData);
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\" + oldRoot, throwOnMissingSubKey: false); }
    }

    [Theory]
    [MemberData(nameof(BoolSettingNames))]
    public void Values_written_by_the_old_helper_are_read_back(string name)
    {
        var old = new RegistryHelper(_reg.RootName);
        old.WriteToRegistryRoot(name, true);
        Assert.True(Get(Settings(), name));
        old.WriteToRegistryRoot(name, false);
        Assert.False(Get(Settings(), name));
        Assert.Empty(_audit.Events);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" False ", false)]
    [InlineData("false", false)]
    public void Reading_is_as_lenient_as_bool_TryParse_was(string text, bool expected)
    {
        _reg.Seed(AppSettings.DoubleClickToHideName, text, RegistryValueKind.String);
        Assert.Equal(expected, Settings().DoubleClickToHide);
        Assert.Empty(_audit.Events);
    }

    // ---- wrong type / garbage ----------------------------------------------------------------

    public static IEnumerable<object[]> UnusableValues() =>
    [
        ["garbage", RegistryValueKind.String, "not True or False"],
        ["", RegistryValueKind.String, "not True or False"],
        ["1", RegistryValueKind.String, "not True or False"],
        [1, RegistryValueKind.DWord, "wrong value type"],
        [1L, RegistryValueKind.QWord, "wrong value type"],
        [new byte[] { 1, 2, 3 }, RegistryValueKind.Binary, "wrong value type"],
        [new[] { "True" }, RegistryValueKind.MultiString, "wrong value type"],
    ];

    [Theory]
    [MemberData(nameof(UnusableValues))]
    public void An_unusable_value_gives_the_default_and_one_audit_record(object data, RegistryValueKind kind, string reason)
    {
        _reg.Seed(AppSettings.DoubleClickToHideName, data, kind);
        _reg.Seed(AppSettings.IcePhysicsName, data, kind);
        var s = Settings();

        Assert.False(s.DoubleClickToHide);   // default off
        Assert.False(s.DoubleClickToHide);   // read again: still one record
        Assert.False(s.DoubleClickToHide);
        Assert.True(s.IcePhysics);           // default ON, whatever garbage is stored

        var events = _audit.Events;
        Assert.Equal(2, events.Count);       // one per name, not per read
        var ev = Assert.Single(events, e => e.Source.EndsWith(@"\DoubleClickToHide"));
        Assert.Equal("SettingFallback", ev.Kind);
        Assert.Equal("Rejected", ev.Status);
        Assert.Contains(reason, ev.Reason);
        Assert.Contains("fallback=False", ev.Detail);
        Assert.Contains(kind.ToString(), ev.Detail);
        Assert.Contains("fallback=True", Assert.Single(events, e => e.Source.EndsWith(@"\IcePhysics")).Detail);
        Assert.Null(ev.Destination);
        Assert.False(string.IsNullOrEmpty(ev.OperationId));
    }

    [Fact]
    public void The_audit_record_names_the_root_and_shortens_a_long_raw_value()
    {
        _reg.Seed(AppSettings.AutoUpdateName, new string('x', 500), RegistryValueKind.String);
        Assert.False(Settings().AutoUpdate);
        var ev = Assert.Single(_audit.Events);
        Assert.StartsWith(@"HKCU\SOFTWARE\RacksTests-", ev.Source);
        Assert.EndsWith(@"\AutoUpdate", ev.Source);
        Assert.True(ev.Detail.Length < 200, ev.Detail);
    }

    [Fact]
    public void Blur_background_is_read_only_and_follows_the_same_rules()
    {
        var s = Settings();
        Assert.Null(s.BlurBackground);
        _reg.Seed(AppSettings.BlurBackgroundName, "True", RegistryValueKind.String);
        Assert.True(s.BlurBackground);
        _reg.Seed(AppSettings.BlurBackgroundName, 7, RegistryValueKind.DWord);
        Assert.False(s.BlurBackground);
        Assert.Single(_audit.Events);
    }

    [Fact]
    public void An_unreadable_registry_gives_the_default_and_is_audited_once()
    {
        var s = Settings(new FailingRegistryRoot(failReads: true, failWrites: false), _reg.Run, _audit);
        Assert.True(s.IcePhysics);
        Assert.True(s.IcePhysics);
        Assert.False(s.StartOnLogin);
        var events = _audit.Events;
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("SettingFallback", e.Kind));
        Assert.Contains("read failed", events[0].Reason);
    }

    // ---- failed writes -----------------------------------------------------------------------

    [Fact]
    public void A_failed_write_is_reported_and_audited_every_time()
    {
        var s = Settings(new FailingRegistryRoot(failReads: false, failWrites: true), _reg.Run, _audit);

        Assert.False(s.TrySetDoubleClickToHide(true));
        Assert.False(s.TrySetDoubleClickToHide(false));
        Assert.False(s.TryPublishMainHwnd(42));

        var events = _audit.Events;
        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("SettingWriteFailed", e.Kind);
            Assert.Equal("Failed", e.Status);
            Assert.Contains("UnauthorizedAccessException", e.Reason);
        });
        Assert.Contains(@"\DoubleClickToHide", events[0].Source);
        Assert.Equal("value=True", events[0].Detail);
        Assert.Contains(@"\MainHwnd", events[2].Source);
    }

    [Fact]
    public void A_sink_that_throws_never_breaks_a_setting()
    {
        var s = Settings(new FailingRegistryRoot(), _reg.Run, new ThrowingAuditSink());
        Assert.False(s.TrySetAutoUpdate(true));
        Assert.False(s.AutoUpdate);
    }

    // ---- MainHwnd ----------------------------------------------------------------------------

    [Fact]
    public void MainHwnd_is_stored_as_the_same_decimal_text_the_E2E_tooling_reads()
    {
        var s = Settings();
        long hwnd = 0x0000_0001_2345_6789;                       // above int range: must stay a long
        Assert.True(s.TryPublishMainHwnd(hwnd));

        var (data, kind) = _reg.RawRoot("MainHwnd");
        Assert.Equal(RegistryValueKind.String, kind);               // SetValue(name, (long)x) stores text, not a QWORD
        Assert.Equal("4886718345", data);
        Assert.Equal(hwnd, s.ReadMainHwnd());

        // Same kind and value as the old call: WriteToRegistryRoot("MainHwnd", (long)hwnd).
        string oldRoot = "RacksTests-" + Guid.NewGuid().ToString("N");
        try
        {
            new RegistryHelper(oldRoot).WriteToRegistryRoot("MainHwnd", hwnd);
            Assert.Equal((data, kind), ThrowawayRegistry.Raw(@"SOFTWARE\" + oldRoot, "MainHwnd"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\" + oldRoot, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void A_MainHwnd_of_the_wrong_type_reads_as_missing_and_is_audited()
    {
        _reg.Seed("MainHwnd", 123, RegistryValueKind.DWord);
        Assert.Null(Settings().ReadMainHwnd());
        Assert.Contains("not a number", Assert.Single(_audit.Events).Reason);
    }

    // ---- start on login (Run key) ------------------------------------------------------------

    [Fact]
    public void Turning_start_on_login_on_writes_the_run_entry_and_the_switch()
    {
        var s = Settings();
        Assert.True(s.TrySetStartOnLogin(true, @"C:\Apps\Racks\Racks.exe"));

        Assert.Equal((@"C:\Apps\Racks\Racks.exe", (RegistryValueKind?)RegistryValueKind.String), _reg.RawRun(RunName));
        Assert.Equal(("True", (RegistryValueKind?)RegistryValueKind.String), _reg.RawRoot("startOnLogin"));
        Assert.True(s.StartOnLogin);
    }

    [Fact]
    public void Turning_it_off_removes_the_run_entry()
    {
        _reg.Run.Write(RunName, @"C:\Apps\Racks\Racks.exe", RegistryValueKind.String);
        _reg.Run.Write("SomethingElse", @"C:\Other.exe", RegistryValueKind.String);
        var s = Settings();

        Assert.True(s.TrySetStartOnLogin(false, null));

        Assert.Null(_reg.RawRun(RunName).Data);
        Assert.Equal(@"C:\Other.exe", _reg.RawRun("SomethingElse").Data);       // never touches other apps
        Assert.Equal(("False", (RegistryValueKind?)RegistryValueKind.String), _reg.RawRoot("startOnLogin"));
        Assert.False(s.StartOnLogin);
    }

    [Fact]
    public void Turning_it_off_when_nothing_is_there_is_not_a_failure()
    {
        Assert.True(Settings().TrySetStartOnLogin(false, null));
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public void Turning_it_on_without_a_path_is_audited_but_the_switch_is_still_saved()
    {
        var s = Settings();
        Assert.False(s.TrySetStartOnLogin(true, null));
        Assert.True(s.StartOnLogin);                                  // as before: the switch is saved regardless
        Assert.Null(_reg.RawRun(RunName).Data);
        Assert.Equal("SettingWriteFailed", Assert.Single(_audit.Events).Kind);
    }

    [Fact]
    public void A_failing_run_key_is_audited_and_the_switch_is_still_saved()
    {
        var s = Settings(_reg.Root, new FailingRegistryRoot(failReads: false, failWrites: true), _audit);
        Assert.False(s.TrySetStartOnLogin(true, @"C:\x.exe"));
        Assert.False(s.TrySetStartOnLogin(false, null));
        Assert.False(s.StartOnLogin);
        Assert.Contains(_audit.Events, e => e.Source.EndsWith(@"\" + RunName) && e.Kind == "SettingWriteFailed");
    }

    // ---- the app-wide instance ---------------------------------------------------------------

    [Fact]
    public void The_default_instance_follows_InstanceController_appName()
    {
        string previous = InstanceController.appName;
        InstanceController.appName = _reg.RootName;                  // how every registry test redirects the root
        try
        {
            Assert.True(AppSettings.Default.TrySetAutoUpdate(true));
            Assert.Equal("True", _reg.RawRoot("AutoUpdate").Data);
            Assert.True(AppSettings.Default.AutoUpdate);
            Assert.True(AppSettings.Default.TryPublishMainHwnd(7));
            Assert.Equal("7", _reg.RawRoot("MainHwnd").Data);
        }
        finally { InstanceController.appName = previous; }
    }
}
