namespace Racks.Infrastructure.Logging;

/// <summary>
/// One line of the audit log: a destructive file operation and how it ended. Strings, not the broker's
/// enums, so Logging does not depend on FileSystem. There are no user-name or machine-name fields; the
/// paths and names themselves are recorded as they are (see <see cref="FileAuditSink"/>).
/// </summary>
/// <param name="OperationId">Same id as the <c>FileOperationResult</c>.</param>
/// <param name="Kind">Move, Rename, DeleteToRecycleBin, DeleteTree or CreateDirectory.</param>
/// <param name="Reason">Why the app did it ("drop into rack").</param>
/// <param name="Status">Succeeded, Skipped, Rejected, Failed or PartiallyCompleted.</param>
/// <param name="Detail">Human text of the outcome (why it was refused or failed). Empty on a clean success.</param>
public sealed record AuditEvent(
    string OperationId,
    DateTime TimestampUtc,
    string Kind,
    string Source,
    string? Destination,
    string Reason,
    string Status,
    string Detail);
