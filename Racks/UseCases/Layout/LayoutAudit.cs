using Racks.Infrastructure.Logging;

namespace Racks.UseCases.Layout;

/// <summary>
/// The audit records of layout import and export, in the existing audit line format (no new fields): Kind
/// <see cref="ImportKind"/> / <see cref="ExportKind"/> with status Succeeded, Rejected or Failed, and
/// <see cref="RollbackFailedKind"/> (status Failed) when the previous racks could not be fully put back after a failed import,
/// <see cref="ValueDroppedKind"/> (status Skipped, one per value not used as written, rack and field in the detail) and
/// <see cref="DialogFailedKind"/> (an exception that reached the dialog layer).
/// Source = the file (or a note), Destination = where it went, Reason = the message the user saw. Never throws.
/// </summary>
public static class LayoutAudit
{
    public const string ImportKind = "LayoutImport";
    public const string ExportKind = "LayoutExport";
    public const string RollbackFailedKind = "LayoutImportRollbackFailed";
    public const string ValueDroppedKind = "LayoutValueDropped";
    public const string DialogFailedKind = "LayoutDialogFailed";

    /// <summary>An exception that reached the dialog layer (a dialog that failed to open, a message box that threw).</summary>
    public static void DialogFailed(IAuditSink sink, bool import, Exception ex) =>
        Write(sink, DialogFailedKind, import ? "import dialog" : "export dialog", null, ex.Message, "Failed", $"exception={ex.GetType().Name}");

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
