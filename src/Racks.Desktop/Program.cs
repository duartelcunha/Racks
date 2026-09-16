using Avalonia;

namespace Racks.Desktop;

internal static class Program
{
    public static string? Profile { get; private set; }
    public static bool ForceSafeMode { get; private set; }
    public static bool SmokeTest { get; private set; }
    private static Mutex? instance;

    [STAThread]
    public static int Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile" && i + 1 < args.Length) Profile = Path.GetFullPath(args[++i]);
            if (args[i] == "--safe-mode") ForceSafeMode = true;
            if (args[i] == "--smoke-test") SmokeTest = true;
        }
        if (SmokeTest && Profile == null) throw new ArgumentException("Smoke tests require an isolated --profile.");
        var suffix = Profile == null ? "SingleInstance-2C9D" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Profile)))[..16];
        instance = new Mutex(true, (OperatingSystem.IsWindows() ? @"Local\" : "") + "Racks-" + suffix, out var created);
        if (!created) { instance.Dispose(); return 0; }
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); return Environment.ExitCode; }
        finally { instance.ReleaseMutex(); instance.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
