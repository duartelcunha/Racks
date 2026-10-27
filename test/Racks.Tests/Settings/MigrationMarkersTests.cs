using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;
using Racks.Tests.Fakes;

namespace Racks.Tests.Settings;

[Collection("Registry")]
public sealed class MigrationMarkersTests : IDisposable
{
    private readonly ThrowawayRegistry _reg = new();
    private readonly RecordingAuditSink _audit = new();

    public void Dispose() => _reg.Dispose();

    private MigrationMarkers Markers() => new(_reg.Root, _audit);

    public static IEnumerable<object[]> All() =>
    [
        [MigrationMarkers.InstallAnimationShownV2],
        [MigrationMarkers.QuickAccessMirrorPinned],
        [MigrationMarkers.FirstRunWelcomeShownV2],
        [MigrationMarkers.VirtualRackLinkOnDropRevertedToMove],
        [MigrationMarkers.FolderOpenInsideFrameDefaultedToExplorer],
    ];

    [Fact]
    public void The_marker_names_are_the_strings_older_builds_wrote()
    {
        // Renaming one would run its one-shot step a second time on every existing install.
        Assert.Equal("InstallAnimationShownV2", MigrationMarkers.InstallAnimationShownV2);
        Assert.Equal("QuickAccessMirrorPinned", MigrationMarkers.QuickAccessMirrorPinned);
        Assert.Equal("FirstRunWelcomeShownV2", MigrationMarkers.FirstRunWelcomeShownV2);
        Assert.Equal("VirtualRackLinkOnDropRevertedToMove", MigrationMarkers.VirtualRackLinkOnDropRevertedToMove);
        Assert.Equal("FolderOpenInsideFrameDefaultedToExplorer", MigrationMarkers.FolderOpenInsideFrameDefaultedToExplorer);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void A_marker_is_unset_until_it_is_set(string name)
    {
        var m = Markers();
        Assert.False(m.IsSet(name));
        Assert.True(m.Set(name));
        Assert.True(m.IsSet(name));
        Assert.True(Markers().IsSet(name));                          // a new object sees it: it is persisted
        Assert.Empty(_audit.Events);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Set_writes_what_the_old_code_wrote(string name)
    {
        Assert.True(Markers().Set(name));
        var (data, kind) = _reg.RawRoot(name);
        Assert.Equal(RegistryValueKind.String, kind);

        // reg.WriteToRegistryRoot(marker, true)
        string oldRoot = "RacksTests-" + Guid.NewGuid().ToString("N");
        try
        {
            new RegistryHelper(oldRoot).WriteToRegistryRoot(name, true);
            Assert.Equal((data, kind), ThrowawayRegistry.Raw(@"SOFTWARE\" + oldRoot, name));
            Assert.Equal("True", data);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\" + oldRoot, throwOnMissingSubKey: false); }
    }

    [Theory]
    [InlineData(RegistryValueKind.String)]
    [InlineData(RegistryValueKind.DWord)]
    [InlineData(RegistryValueKind.QWord)]
    public void A_marker_counts_as_set_whatever_type_an_old_install_stored(RegistryValueKind kind)
    {
        object data = kind switch { RegistryValueKind.String => "True", RegistryValueKind.DWord => 1, _ => 1L };
        _reg.Seed(MigrationMarkers.QuickAccessMirrorPinned, data, kind);
        Assert.True(Markers().IsSet(MigrationMarkers.QuickAccessMirrorPinned));   // KeyExistsRoot only checked "not null"
    }

    [Fact]
    public void Markers_written_by_the_old_helper_are_seen()
    {
        new RegistryHelper(_reg.RootName).WriteToRegistryRoot(MigrationMarkers.FirstRunWelcomeShownV2, true);
        Assert.True(Markers().IsSet(MigrationMarkers.FirstRunWelcomeShownV2));
    }

    [Fact]
    public void An_unknown_marker_name_is_refused()
    {
        var m = Markers();
        Assert.Throws<ArgumentException>(() => m.IsSet("startOnLogin"));
        Assert.Throws<ArgumentException>(() => m.Set("SomethingNew"));
        Assert.Null(_reg.RawRoot("SomethingNew").Data);
    }

    [Fact]
    public void A_failed_write_is_returned_and_audited()
    {
        var m = new MigrationMarkers(new FailingRegistryRoot(failReads: false, failWrites: true), _audit);
        Assert.False(m.Set(MigrationMarkers.InstallAnimationShownV2));
        var ev = Assert.Single(_audit.Events);
        Assert.Equal("SettingWriteFailed", ev.Kind);
        Assert.EndsWith(@"\InstallAnimationShownV2", ev.Source);
    }

    [Fact]
    public void An_unreadable_registry_reads_as_not_set_and_is_audited_once()
    {
        var m = new MigrationMarkers(new FailingRegistryRoot(failReads: true, failWrites: false), _audit);
        Assert.False(m.IsSet(MigrationMarkers.QuickAccessMirrorPinned));
        Assert.False(m.IsSet(MigrationMarkers.QuickAccessMirrorPinned));
        Assert.Equal("SettingFallback", Assert.Single(_audit.Events).Kind);
    }
}

// AuditLog.Use swaps a process-wide sink, so tests that do it must not run in parallel with each other.
file sealed class ThreadFilterSink(IAuditSink inner, int threadId) : IAuditSink
{
    public void Write(AuditEvent auditEvent)
    {
        if (Environment.CurrentManagedThreadId == threadId) inner.Write(auditEvent);
    }
}

[Collection("AuditLog")]
public sealed class SettingsAuditRecordTests : IDisposable
{
    private readonly ThrowawayRegistry _reg = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "racks-audit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _reg.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Without_an_injected_sink_records_go_to_the_app_audit_log()
    {
        // The sink is process-wide and tests outside the AuditLog collection keep running in parallel, so keep only
        // what this test's own thread writes (the code under test is synchronous).
        var recording = new RecordingAuditSink();
        var mine = new ThreadFilterSink(recording, Environment.CurrentManagedThreadId);
        using (AuditLog.Use(mine))
        {
            var s = new AppSettings(_reg.Root, _reg.Run, () => "RacksTestApp");
            _reg.Seed(AppSettings.HotCornerHideName, "banana", RegistryValueKind.String);
            Assert.False(s.HotCornerHide);
            Assert.Equal("SettingFallback", Assert.Single(recording.Events).Kind);

            var m = new MigrationMarkers(new FailingRegistryRoot());
            Assert.False(m.Set(MigrationMarkers.InstallAnimationShownV2));
            Assert.Equal(2, recording.Events.Count);
        }
    }

    [Fact]
    public void The_record_keeps_the_audit_line_format_of_the_file_sink()
    {
        var sink = new FileAuditSink(_dir);
        var s = new AppSettings(_reg.Root, _reg.Run, () => "RacksTestApp", sink);
        _reg.Seed(AppSettings.IcePhysicsName, 5, RegistryValueKind.DWord);
        Assert.True(s.IcePhysics);

        string line = File.ReadAllLines(Path.Combine(_dir, FileAuditSink.FileName)).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal(
            ["OperationId", "TimestampUtc", "Kind", "Source", "Destination", "Reason", "Status", "Detail"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("SettingFallback", root.GetProperty("Kind").GetString());
        Assert.Equal("Rejected", root.GetProperty("Status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Destination").ValueKind);
    }
}
