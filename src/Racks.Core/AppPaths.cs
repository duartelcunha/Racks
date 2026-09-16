namespace Racks.Core;

public sealed class AppPaths
{
    public string Data { get; }
    public string Workspace { get; }
    public string Desktop { get; }
    public bool IsIsolated { get; }
    public string Settings => Path.Combine(Data, "settings.json");
    public string Operations => Path.Combine(Data, "operations");
    public string LastUndo => Path.Combine(Data, "last-undo.json");
    public string Startup => Path.Combine(Data, "startup.json");
    public string Updates => Path.Combine(Data, "updates");

    public AppPaths(string? profile = null)
    {
        IsIsolated = !string.IsNullOrWhiteSpace(profile);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Data = IsIsolated ? Path.GetFullPath(profile!) : OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "Racks")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RacksData");
        Workspace = IsIsolated ? Path.Combine(Data, "workspace") : Path.Combine(home, "RacksWorkspace");
        Desktop = IsIsolated ? Path.Combine(Data, "desktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(Desktop)) Desktop = Path.Combine(home, "Desktop");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(Operations);
        if (IsIsolated) Directory.CreateDirectory(Desktop);
    }
}
