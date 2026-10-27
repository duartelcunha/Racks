namespace Racks.Infrastructure.FileSystem;

/// <summary>
/// Who-what-why of one operation, shared by the result and its audit record. Deliberately holds no user
/// name or machine name: the audit log is meant to be shareable in a bug report.
/// </summary>
/// <param name="Id">Short unique id, also on the result and in the audit record.</param>
/// <param name="TimestampUtc">When the operation started.</param>
/// <param name="Reason">Why this operation is happening ("drop into rack", "auto-route"). Never empty.</param>
public sealed record OperationContext(string Id, DateTime TimestampUtc, string Reason)
{
    public static OperationContext Create(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An operation needs a reason.", nameof(reason));
        return new OperationContext(Guid.NewGuid().ToString("N")[..12], DateTime.UtcNow, reason.Trim());
    }
}
