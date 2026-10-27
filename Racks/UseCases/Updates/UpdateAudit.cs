using System.Text.RegularExpressions;
using Racks.Infrastructure.Logging;

namespace Racks.UseCases.Updates;

/// <summary>
/// Why an update was not offered or not installed, in the existing audit line format (no new fields), so a failure that the
/// user may never have seen (the automatic check is silent) still leaves a trace in <c>%AppData%\Racks\audit.log</c>:
/// <see cref="CheckFailedKind"/> (the feed could not be read, or a newer release did not verify; status Failed or Rejected),
/// <see cref="InstallRefusedKind"/> (nothing verified to install, another install running, size or SHA-256 mismatch; Rejected),
/// <see cref="InstallFailedKind"/> (network, disk, timeout or the installer would not start; Failed) and
/// <see cref="InstallStartedKind"/> (the verified installer was started; Succeeded).
/// Source = the URL without its query or fragment, Reason = the text the user would read or the exception message, Detail =
/// the machine-readable reason. No secrets are involved and queries are removed from every field, even inside exception
/// messages. Never throws.
/// </summary>
internal static class UpdateAudit
{
    public const string CheckFailedKind = "UpdateCheckFailed";
    public const string InstallRefusedKind = "UpdateInstallRefused";
    public const string InstallFailedKind = "UpdateInstallFailed";
    public const string InstallStartedKind = "UpdateInstallStarted";

    private static readonly Regex UrlQuery = new(
        @"(?<url>\bhttps?://[^\s?#]*)[?#]\S*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    public static void Write(IAuditSink sink, string kind, string? sourceUrl, string reason, string status, string detail)
    {
        try
        {
            sink.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, kind,
                Source: SafeUrl(sourceUrl), Destination: null, Reason: StripQueries(reason), Status: status, Detail: StripQueries(detail)));
        }
        catch
        {
            // Auditing is best-effort; it must never turn an update check or install into an error.
        }
    }

    /// <summary>The URL without query or fragment ("(none)" when there is no URL, "(invalid url)" when it does not parse).</summary>
    internal static string SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped)   // no user info, query or fragment
            : "(invalid url)";
    }

    internal static string StripQueries(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        try { return UrlQuery.Replace(text, "${url}"); }
        catch (RegexMatchTimeoutException) { return "(unreadable)"; }
    }
}
