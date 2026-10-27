namespace Racks.Infrastructure.FileSystem;

/// <summary>
/// One requested operation. For <see cref="FileOperationKind.Rename"/>, <paramref name="Destination"/> is the
/// new leaf name (not a path); for delete kinds it is unused; for CreateDirectory <paramref name="Source"/> is
/// the directory to create.
/// </summary>
/// <param name="SameVolumeRenameOnly">Refuse anything that would need copy + delete (cross-volume). For paths that must only rename, like uninstall.</param>
/// <param name="NeverOverwrite">Must stay true: the broker never overwrites. Reserved so a caller cannot ask for it by accident.</param>
public sealed record FileOperationIntent(
    FileOperationKind Kind,
    string Source,
    string? Destination,
    string Reason,
    bool SameVolumeRenameOnly = false,
    bool NeverOverwrite = true);
