namespace Racks.Infrastructure.FileSystem;

/// <summary>How a broker call ended. Expected failures are a status, never an exception.</summary>
public enum OperationStatus
{
    /// <summary>Done, and the result was checked on disk.</summary>
    Succeeded,
    /// <summary>Nothing changed because the target already exists (or there was nothing to do).</summary>
    Skipped,
    /// <summary>Refused on policy: bad name or path, protected folder, missing source. Nothing changed.</summary>
    Rejected,
    /// <summary>Allowed, but the system call failed. Nothing is known to be lost.</summary>
    Failed,
    /// <summary>The data reached its destination but part of the work is left over (for example the original could not be fully deleted).</summary>
    PartiallyCompleted,
}
