using System.IO;
using System.Text.Json;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Logging;

namespace Racks.Tests.FileSystem;

public sealed class FileAuditSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "racks-audit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static AuditEvent Sample(string source = "C:\\a\\b.txt", string? dest = "C:\\a\\c.txt", string detail = "") =>
        new("abc123def456", new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), "Move", source, dest, "drop into rack", "Succeeded", detail);

    [Fact]
    public void Writes_one_json_line_per_event_to_audit_log()
    {
        var sink = new FileAuditSink(_dir);
        sink.Write(Sample());
        sink.Write(Sample(detail: "line one\nline two"));                 // a newline inside a value must not split the record

        var lines = File.ReadAllLines(Path.Combine(_dir, FileAuditSink.FileName));
        Assert.Equal(2, lines.Length);
        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.Equal("abc123def456", root.GetProperty("OperationId").GetString());
        Assert.Equal("Move", root.GetProperty("Kind").GetString());
        Assert.Equal("Succeeded", root.GetProperty("Status").GetString());
        Assert.Equal("drop into rack", root.GetProperty("Reason").GetString());
        Assert.Equal("C:\\a\\b.txt", root.GetProperty("Source").GetString());
        Assert.Equal("C:\\a\\c.txt", root.GetProperty("Destination").GetString());
        Assert.Equal(new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), root.GetProperty("TimestampUtc").GetDateTime().ToUniversalTime());
        Assert.Equal("line one\nline two", JsonDocument.Parse(lines[1]).RootElement.GetProperty("Detail").GetString());
    }

    [Fact]
    public void Does_not_record_the_user_profile_path_or_account_name()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string source = Path.Combine(profile, "Desktop", "secret plan.txt");
        var sink = new FileAuditSink(_dir);

        sink.Write(Sample(source, Path.Combine(profile, "Documents", "x.txt"), detail: "failed in " + source));

        string line = File.ReadAllText(Path.Combine(_dir, FileAuditSink.FileName));
        Assert.DoesNotContain(profile.Replace("\\", "\\\\"), line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", line);
        Assert.Contains("secret plan.txt", line);
    }

    [Fact]
    public void Rotates_like_the_crash_log()
    {
        var sink = new FileAuditSink(_dir, maxBytes: 600);
        for (int i = 0; i < 10; i++) sink.Write(Sample());

        string log = Path.Combine(_dir, FileAuditSink.FileName);
        Assert.True(File.Exists(log + ".1"));
        Assert.True(new FileInfo(log).Length <= 600);
    }

    [Fact]
    public void Never_throws_when_the_folder_cannot_be_created()
    {
        Directory.CreateDirectory(_dir);
        string aFile = Path.Combine(_dir, "file-not-folder");
        File.WriteAllText(aFile, "x");
        var sink = new FileAuditSink(Path.Combine(aFile, "inside"));       // a folder under a file: impossible

        var ex = Record.Exception(() => sink.Write(Sample()));

        Assert.Null(ex);
    }

    [Fact]
    public void Broker_with_the_file_sink_leaves_a_readable_trail()
    {
        Directory.CreateDirectory(_dir);
        var broker = new FileOperationBroker(new FileAuditSink(_dir));
        string src = Path.Combine(_dir, "a.txt");
        File.WriteAllText(src, "x");

        broker.Move(src, Path.Combine(_dir, "b.txt"), "trail test");
        broker.Rename(Path.Combine(_dir, "b.txt"), "..\\c.txt", "trail test 2");

        var records = File.ReadAllLines(Path.Combine(_dir, FileAuditSink.FileName))
            .Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(new[] { "Succeeded", "Rejected" }, records.Select(r => r.GetProperty("Status").GetString()));
        Assert.Equal(new[] { "Move", "Rename" }, records.Select(r => r.GetProperty("Kind").GetString()));
    }
}
