using Racks.Infrastructure.Logging;

namespace Racks.UseCases.Layout;

/// <summary>
/// The audit records of layout import and export, in the existing audit line format (no new fields): Kind
/// <see cref="ImportKind"/> / <see cref="ExportKind"/> with status Succeeded, Rejected or Failed, and
/// <see cref="RollbackFailedKind"/> (status Failed) when the previous racks could not be fully put back after a failed import.
/// Source = the file (or a note), Destination = where it went, Reason = the message the user saw. Never throws.
/// </summary>
public static class LayoutAudit
{
    public const string ImportKind = "LayoutImport";
    public const string ExportKind = "LayoutExport";
    public const string RollbackFailedKind = "LayoutImportRollbackFailed";

    public static void Write(IAuditSink sink, string kind, string source, string? destination, string reason, string status, string detail)
    {
        try
        {
            sink.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, kind, source, destination, reason, status, detail));
        }
        catch
        {
            // Auditing is best-effort; it must never turn an import or an export into an error.
        }
    }
}
