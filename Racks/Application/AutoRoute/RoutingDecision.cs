namespace Racks.Application.AutoRoute;

public enum RoutingOutcome
{
    /// <summary>The entry was moved into the rack (or its workspace).</summary>
    Routed,
    /// <summary>A rack matched but the entry was not moved: not eligible, or the move was refused (name collision, protected folder).</summary>
    Skipped,
    /// <summary>No rack wants this entry. Nothing to say, nothing recorded.</summary>
    NoMatch,
    /// <summary>The move was attempted and failed; the entry is where it was.</summary>
    Failed,
    /// <summary>The file is still in use; the service will look again later. Not final.</summary>
    Deferred,
}

/// <summary>What auto-route decided for one Desktop entry.</summary>
/// <param name="Path">The Desktop path that was judged.</param>
/// <param name="TargetRack">Name of the rack that matched, if any.</param>
/// <param name="Destination">Where the entry was (or would have been) moved to: the full target path.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Reason">Short English text for the log; empty for a clean Routed or NoMatch.</param>
public sealed record RoutingDecision(
    string Path,
    string? TargetRack,
    string? Destination,
    RoutingOutcome Outcome,
    string Reason);
