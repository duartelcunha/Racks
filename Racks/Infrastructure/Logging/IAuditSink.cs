namespace Racks.Infrastructure.Logging;

/// <summary>
/// Where audit records of destructive file operations go. Implementations must never throw: an audit
/// failure must not turn a finished operation into an error (the broker also guards the call).
/// </summary>
public interface IAuditSink
{
    void Write(AuditEvent auditEvent);
}

/// <summary>Discards everything. Used by tests so they do not write to the user's real audit log.</summary>
public sealed class NullAuditSink : IAuditSink
{
    public static readonly NullAuditSink Instance = new();
    public void Write(AuditEvent auditEvent) { }
}
