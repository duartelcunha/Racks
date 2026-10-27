namespace Racks.Infrastructure.FileSystem;

/// <summary>What a <see cref="FileOperationBroker"/> call does to the user's files.</summary>
public enum FileOperationKind
{
    Move,
    Rename,
    DeleteToRecycleBin,
    DeleteTree,
    CreateDirectory,
}
