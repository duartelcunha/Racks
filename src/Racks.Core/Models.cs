using System.Text.Json.Serialization;

namespace Racks.Core;

public enum RackKind { Owned, Folder }
public enum CollisionChoice { KeepBoth, Skip, Cancel }
public enum OperationKind { Move, Rename, Shortcut }
public enum ItemOutcome { Pending, Completed, Skipped, Failed, Undone, UndoPending }

public sealed class RackDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New rack";
    public RackKind Kind { get; set; }
    public string Folder { get; set; } = "";
    // Only legacy desktop-filter racks share a directory and need a name filter.
    public List<string>? IncludedNames { get; set; }
    public double X { get; set; } = 80;
    public double Y { get; set; } = 80;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 360;
    public bool Collapsed { get; set; }
    public bool Locked { get; set; }
    public bool ListView { get; set; }
    public bool Snap { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Descending { get; set; }
    public string Sort { get; set; } = "Name";
    public string Accent { get; set; } = "#58C4AD";
    public string Background { get; set; } = "#18252B";
    public string Foreground { get; set; } = "#F1F5F5";
    public string FontFamily { get; set; } = "Inter";
    public double FontSize { get; set; } = 13;
    public int IconSize { get; set; } = 32;
    public double Opacity { get; set; } = .94;
    public string BackgroundImage { get; set; } = "";
    // Preserve settings that don't yet have a cross-platform presentation equivalent.
    public Dictionary<string, string> LegacyAppearance { get; set; } = new();
    public override string ToString() => Title;
}

public sealed class RoutingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";
    public string Source { get; set; } = "";
    public Guid DestinationRackId { get; set; }
    public string Extensions { get; set; } = "";
    public string NameContains { get; set; } = "";
    public string Regex { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Shortcut { get; set; }
    public string LastError { get; set; } = "";
}

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public bool Onboarded { get; set; }
    public bool LegacyImported { get; set; }
    public string Theme { get; set; } = "System";
    public bool ReduceMotion { get; set; }
    public bool Physics { get; set; } = true;
    public bool DesktopIntegration { get; set; } = true;
    public bool AutoUpdates { get; set; } = true;
    public bool RoutingPaused { get; set; }
    public List<RackDefinition> Racks { get; set; } = new();
    public List<RoutingRule> Rules { get; set; } = new();
}

public sealed record CatalogItem(Guid RackId, string RackTitle, string Path, string Name,
    bool IsDirectory, long Size, DateTime Modified)
{
    public string Kind => IsDirectory ? "Folder" : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public string Detail => $"{RackTitle} · {Kind}";
    public override string ToString() => Name;
}

public sealed class OperationItem
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public Guid? SourceRackId { get; set; }
    public Guid? DestinationRackId { get; set; }
    public ItemOutcome Outcome { get; set; }
    public string Error { get; set; } = "";
    public long Length { get; set; }
    public string? UndoDestination { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public bool IsDirectory { get; set; }
}

public sealed class OperationRecord
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public OperationKind Kind { get; set; }
    public string Label { get; set; } = "Move files";
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public bool Finished { get; set; }
    public bool SettingsPending { get; set; }
    public List<OperationItem> Items { get; set; } = new();
    public List<RackDefinition> CreatedRacks { get; set; } = new();
    public RackDefinition? RemovedRack { get; set; }
    [JsonIgnore] public int Completed => Items.Count(x => x.Outcome == ItemOutcome.Completed);
    [JsonIgnore] public bool NeedsAttention => SettingsPending || !Finished || Items.Any(x => x.Outcome is ItemOutcome.Pending or ItemOutcome.Failed or ItemOutcome.UndoPending || x.Outcome == ItemOutcome.Completed && x.Error.Length > 0);
}

public sealed class OrganizeGroup
{
    public string Name { get; set; } = "";
    public List<string> Paths { get; set; } = new();
}
