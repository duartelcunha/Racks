using System.IO;
using System.Text.Json;
using Racks.Util;

namespace Racks.Infrastructure.Logging;

/// <summary>
/// Appends one JSON line per event to <c>%AppData%\Racks\audit.log</c>. Rotation (audit.log.1 once the
/// size cap is reached) is <see cref="CrashLog.Append"/>'s. Local only: nothing here is ever uploaded.
/// Paths under the user profile are written as <c>%USERPROFILE%\...</c> so the log can be pasted in a bug
/// report without the account name. Never throws.
/// </summary>
public sealed class FileAuditSink : IAuditSink
{
    public const string FileName = "audit.log";
    public const long MaxBytes = 2_000_000;

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _directory;
    private readonly long _maxBytes;

    public FileAuditSink(string? directory = null, long maxBytes = MaxBytes)
    {
        _directory = directory ?? DefaultDirectory();
        _maxBytes = maxBytes;
    }

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Racks");

    public void Write(AuditEvent auditEvent)
    {
        try
        {
            var shareable = auditEvent with
            {
                Source = Redact(auditEvent.Source),
                Destination = auditEvent.Destination == null ? null : Redact(auditEvent.Destination),
                Detail = Redact(auditEvent.Detail),
            };
            string line = JsonSerializer.Serialize(shareable, Json) + "\n";
            lock (Gate) CrashLog.Append(_directory, line, _maxBytes, FileName);
        }
        catch
        {
            // Auditing is best-effort; it must never turn a finished operation into an error.
        }
    }

    private static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? text : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}
