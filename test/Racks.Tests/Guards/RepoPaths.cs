using System.IO;

namespace Racks.Tests.Guards;

internal static class RepoPaths
{
    public static readonly string Root = FindRepoRoot();

    public static string App(params string[] parts) => Path.Combine([Root, "Racks", .. parts]);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Racks.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racks.sln not found above " + AppContext.BaseDirectory);
    }
}
