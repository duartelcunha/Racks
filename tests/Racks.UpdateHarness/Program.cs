using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Racks.Desktop;

// Executes the real app's updater only on a disposable hosted Windows runner.
internal static class Program
{
    internal static string[] Inputs = [];
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" || args.Length != 6)
            throw new InvalidOperationException("Update installation testing is restricted to a disposable GitHub-hosted Windows runner.");
        var feed = new Uri(args[1]);
        if (!feed.IsLoopback) throw new InvalidOperationException("The fixture feed must be local.");
        Inputs = args;
        using var instance = new Mutex(true, @"Local\Racks-SingleInstance-2C9D", out var created);
        if (!created) throw new InvalidOperationException("A Racks instance is already running.");
        try { AppBuilder.Configure<UpdateApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); return Environment.ExitCode; }
        finally { instance.ReleaseMutex(); }
    }
}

internal sealed class UpdateApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) throw new InvalidOperationException();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Title = "Racks isolated update verification", Width = 440, Height = 180 };
        desktop.MainWindow = window;
        var session = new Session(Program.Inputs[0], true);
        var updater = UpdateService.CreateUpdater(Program.Inputs[1], Program.Inputs[2], session.Paths.Updates);
        updater.RestartExecutablePath = Program.Inputs[5];
        updater.RestartExecutableName = Path.Combine(Program.Inputs[5], "Racks.Next.exe");
        updater.RelaunchAfterUpdateCommandSuffix = "--profile \"" + Program.Inputs[0] + "\" --smoke-test";
        updater.CustomInstallerArguments = Program.Inputs[3];
        var updates = new UpdateService(session, updater);
        var resultPath = Program.Inputs[4];
        void Fail(string error) => Dispatcher.UIThread.Post(() => { File.WriteAllText(resultPath, "FAILED: " + error); desktop.Shutdown(1); });
        updater.DownloadHadError += (_, _, error) => Fail(error.ToString());
        updater.InstallUpdateFailed += (reason, _) => { Fail(reason.ToString()); return true; };
        updates.CloseRequested += () =>
        {
            File.WriteAllText(resultPath, "Graceful shutdown requested after verified download");
            desktop.Shutdown(0);
        };
        updates.Changed += async () =>
        {
            if (!updates.Ready || updates.Installing) return;
            try { await updates.InstallAsync(); }
            catch (Exception error) { Fail(error.ToString()); }
        };
        window.Opened += async (_, _) =>
        {
            try { await session.InitializeAsync(); await updates.CheckAsync(); }
            catch (Exception error) { Fail(error.ToString()); }
        };
        desktop.Exit += (_, _) => { updates.Dispose(); session.Dispose(); };
        base.OnFrameworkInitializationCompleted();
    }
}
