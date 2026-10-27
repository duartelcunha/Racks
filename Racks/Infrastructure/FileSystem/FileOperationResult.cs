namespace Racks.Infrastructure.FileSystem;

/// <param name="Status">How it ended.</param>
/// <param name="Reason">Human text for the user or the log. Empty on a clean success.</param>
/// <param name="Source">The path the operation started from (or the directory to create).</param>
/// <param name="Destination">Where it ended up (or was meant to). For a rename, the full target path when it could be built.</param>
/// <param name="OperationId">Matches the audit record.</param>
public sealed record FileOperationResult(
    OperationStatus Status,
    string Reason,
    string Source,
    string? Destination,
    string OperationId)
{
    /// <summary>Done completely.</summary>
    public bool Succeeded => Status == OperationStatus.Succeeded;

    /// <summary>The data is at the destination: <see cref="Succeeded"/>, or done with something left over. What callers that used to test <c>SafeMove.Result.Moved</c> want.</summary>
    public bool Completed => Status is OperationStatus.Succeeded or OperationStatus.PartiallyCompleted;
}
