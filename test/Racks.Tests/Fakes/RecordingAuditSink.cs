using System.IO;
using Racks.Infrastructure.Logging;

namespace Racks.Tests.Fakes;

/// <summary>Keeps every audit event in memory so a test can assert what a broker call recorded.</summary>
public sealed class RecordingAuditSink : IAuditSink
{
    private readonly List<AuditEvent> _events = [];

    public IReadOnlyList<AuditEvent> Events { get { lock (_events) return _events.ToList(); } }

    public void Write(AuditEvent auditEvent) { lock (_events) _events.Add(auditEvent); }
}

/// <summary>An audit sink that always fails, to prove an audit problem never changes an operation's outcome.</summary>
public sealed class ThrowingAuditSink : IAuditSink
{
    public void Write(AuditEvent auditEvent) => throw new IOException("audit disk is full");
}
