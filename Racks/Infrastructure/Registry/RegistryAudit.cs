using System.Collections.Concurrent;
using System.Globalization;
using Racks.Infrastructure.Logging;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// Writes the audit records of the settings layer, in the existing audit line format (no new fields):
/// <c>SettingFallback</c> (status Rejected) when a stored value could not be used and the documented default
/// applied, at most once per root and name for the life of the object, and <c>SettingWriteFailed</c>
/// (status Failed) for every failed write. Source = the registry path and value name; Detail = the raw value
/// (shortened) and the fallback. Never throws.
/// </summary>
internal sealed class RegistryAudit(IAuditSink sink)
{
    public const string FallbackKind = "SettingFallback";
    public const string WriteFailedKind = "SettingWriteFailed";
    private const int MaxRawChars = 100;

    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A stored value was unusable. Recorded the first time for this name only.</summary>
    public void Fallback(IRegistryRoot root, string name, string raw, string reason, string fallback)
    {
        string source = Source(root, name);
        if (!_reported.TryAdd(source, 0)) return;
        Write(FallbackKind, source, reason, "Rejected", $"raw={raw}; fallback={fallback}");
    }

    public void WriteFailed(IRegistryRoot root, string name, string attempted, Exception ex) =>
        Write(WriteFailedKind, Source(root, name), $"{ex.GetType().Name}: {ex.Message}", "Failed", $"value={attempted}");

    public static string Describe(RegistryValue value) => value.Data switch
    {
        string s => $"{value.Kind} \"{Shorten(s)}\"",
        byte[] bytes => $"{value.Kind} ({bytes.Length} bytes)",
        string[] lines => $"{value.Kind} ({lines.Length} strings)",
        _ => $"{value.Kind} {Shorten(Convert.ToString(value.Data, CultureInfo.InvariantCulture) ?? "")}",
    };

    private static string Shorten(string text) =>
        text.Length <= MaxRawChars ? text : text[..MaxRawChars] + "...";

    private static string Source(IRegistryRoot root, string name) => root.Path + @"\" + name;

    private void Write(string kind, string source, string reason, string status, string detail)
    {
        try
        {
            sink.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, kind, source, null, reason, status, detail));
        }
        catch
        {
            // Auditing is best-effort; it must never turn a settings read or write into an error.
        }
    }
}
