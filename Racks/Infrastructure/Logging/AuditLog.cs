namespace Racks.Infrastructure.Logging;

/// <summary>
/// Holds the app-wide audit sink. Production uses <see cref="FileAuditSink"/>; tests swap in a recording
/// or null sink with <see cref="Use"/>. <see cref="Forwarder"/> is what the default broker writes to, so a
/// swap takes effect without rebuilding the broker.
/// </summary>
public static class AuditLog
{
    private static IAuditSink s_current = new FileAuditSink();

    public static IAuditSink Current
    {
        get => Volatile.Read(ref s_current);
        set => Volatile.Write(ref s_current, value ?? NullAuditSink.Instance);
    }

    /// <summary>A sink that always writes to <see cref="Current"/>.</summary>
    public static IAuditSink Forwarder { get; } = new ForwardingSink();

    /// <summary>Replaces <see cref="Current"/> until the returned scope is disposed.</summary>
    public static IDisposable Use(IAuditSink sink)
    {
        var previous = Current;
        Current = sink;
        return new Scope(previous);
    }

    private sealed class ForwardingSink : IAuditSink
    {
        public void Write(AuditEvent auditEvent) => Current.Write(auditEvent);
    }

    private sealed class Scope(IAuditSink previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
