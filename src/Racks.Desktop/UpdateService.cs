using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using Racks.Core;

namespace Racks.Desktop;

public sealed class UpdateService : IDisposable
{
    private readonly Session session;
    private readonly SparkleUpdater? updater;
    private readonly HttpClient releaseClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(6) };
    private AppCastItem? ready;
    private string? downloadedPath;
    private bool checking;
    private volatile bool downloadPending;
    private volatile bool paused;
    public string Status { get; private set; } = "Check for the latest stable release on GitHub. Automatic installation is available in builds configured for signed updates.";
    public string CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Unknown";
    public bool Configured => updater != null;
    public bool Ready => ready != null;
    public bool Installing { get; private set; }
    public event Action? Changed;
    public event Action? CloseRequested;
    public event Action? PreparingInstall;

    public UpdateService(Session session) : this(session, CreateConfiguredUpdater(session.Paths)) { }

    private static SparkleUpdater? CreateConfiguredUpdater(AppPaths paths)
    {
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value);
        var key = metadata.GetValueOrDefault("UpdatePublicKey");
        var feed = metadata.GetValueOrDefault("UpdateFeedBase");
        if (!string.IsNullOrWhiteSpace(key) && Uri.TryCreate(feed, UriKind.Absolute, out var url) && url.Scheme == "https")
        {
            if (!Convert.TryFromBase64String(key, new byte[32], out var keyLength) || keyLength != 32) return null;
            var platform = OperatingSystem.IsMacOS() ? "osx" : "win";
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            return CreateUpdater(url.ToString().TrimEnd('/') + $"/{platform}-{arch}/appcast.xml", key, paths.Updates);
        }
        return null;
    }

    internal UpdateService(Session session, SparkleUpdater? configuredUpdater)
    {
        this.session = session;
        updater = configuredUpdater;
        if (updater != null)
        {
            updater.DownloadFinished += (item, path) => { downloadPending = false; Dispatcher.UIThread.Post(() => { ready = item; downloadedPath = path; SetStatus("Verified update ready. Restart to install when your work is finished."); }); };
            updater.DownloadHadError += (_, _, error) => { downloadPending = false; SetStatus(paused ? "Update download paused. Check for updates to resume." : "Download failed: " + error.Message); };
            updater.DownloadedFileIsCorrupt += (_, _) => { downloadPending = false; SetStatus("Update rejected: its signature is invalid."); };
            updater.DownloadedFileThrewWhileCheckingSignature += (_, _) => { downloadPending = false; SetStatus("Update rejected: its signature could not be checked."); };
            updater.DownloadMadeProgress += (_, _, progress) => { if (!paused) SetStatus($"Downloading update · {progress.ProgressPercentage}%"); };
            updater.DownloadCanceled += (_, _) => { downloadPending = false; SetStatus(paused ? "Update download paused. Check for updates to resume." : "Download interrupted. Check for updates to retry."); };
            updater.PreparingToExit += (_, args) =>
            {
                args.Cancel = session.Operations.IsBusy || session.CurrentOperation != null;
                if (args.Cancel) { Installing = false; SetStatus("Finish or cancel the file operation before restarting."); }
            };
            updater.CloseApplicationAsync += async () => await Dispatcher.UIThread.InvokeAsync(() => CloseRequested?.Invoke());
            updater.InstallUpdateFailed += (reason, _) =>
            {
                Dispatcher.UIThread.Post(() => { Installing = false; ready = null; downloadedPath = null; SetStatus("Update could not be installed: " + reason + ". Check for updates to retry."); });
                return true;
            };
            Status = "Ready to check for signed updates.";
        }
        timer.Tick += async (_, _) => { if (session.Settings.AutoUpdates) await CheckAsync(); };
    }

    internal static SparkleUpdater CreateUpdater(string feed, string publicKey, string cacheDirectory)
    {
        var updater = new SparkleUpdater(feed, new Ed25519Checker(SecurityMode.Strict, publicKey))
        {
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            RelaunchAfterUpdate = true,
            // NetSparkle waits for our graceful CloseApplicationAsync callback.
            // False would launch the installer immediately without requesting shutdown.
            ShouldKillParentProcessWhenStartingInstaller = true,
            Configuration = new DefaultConfiguration(new AssemblyDiagnosticsAccessor(typeof(UpdateService).Assembly.Location)),
            UpdateDownloader = new UpdateDownloader(),
            TmpDownloadFilePath = cacheDirectory,
            CheckServerFileName = false,
            RestartExecutableName = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Racks.Next.exe" : "Racks.Next")
        };
        // The Mac ZIP contains Racks.app. Extract beside the bundle, not inside Contents/MacOS.
        if (OperatingSystem.IsMacOS())
        {
            var executableDirectory = new DirectoryInfo(AppContext.BaseDirectory);
            if (executableDirectory.Name == "MacOS" && executableDirectory.Parent?.Name == "Contents" &&
                executableDirectory.Parent.Parent is { } bundle && bundle.Name.EndsWith(".app", StringComparison.Ordinal))
                updater.RestartExecutablePath = bundle.Parent!.FullName;
        }
        return updater;
    }

    private void SetStatus(string value) => Dispatcher.UIThread.Post(() => { Status = value; Changed?.Invoke(); });
    public void Start() { if (!Configured) return; timer.Start(); if (session.Settings.AutoUpdates) _ = CheckAsync(); }
    public async Task CheckAsync()
    {
        if (checking || downloadPending || Ready || Installing) return;
        checking = true; paused = false; SetStatus(Configured ? "Checking signed release feed…" : "Checking GitHub for the latest stable release…");
        try
        {
            if (updater == null)
            {
                var latest = await ReleaseCheck.LatestStableVersionAsync(releaseClient);
                SetStatus(latest == null ? "No stable release has been published yet. You can view previews on the releases page." :
                    $"Latest stable release: {latest}. You’re running {CurrentVersion}. View releases for downloads and release notes; previews may be newer than the stable release.");
                return;
            }
            var info = await updater.CheckForUpdatesQuietly();
            if (info.Status == UpdateStatus.UpdateAvailable && !paused && info.Updates.FirstOrDefault() is { } item && updater.UpdateDownloader?.IsDownloading != true)
            {
                downloadPending = true;
                await updater.InitAndBeginDownload(item);
            }
            if (info.Status == UpdateStatus.UpdateNotAvailable) SetStatus("You have the latest release.");
            else if (info.Status == UpdateStatus.CouldNotDetermine) SetStatus("Could not verify the release feed. Try again later.");
        }
        catch (OperationCanceledException) { downloadPending = false; SetStatus("The update check timed out. Check your connection and try again, or view releases in your browser."); }
        catch (Exception ex) { downloadPending = false; SetStatus("Update check failed: " + ex.Message + " You can try again or view releases in your browser."); }
        finally { checking = false; }
    }
    public void Pause()
    {
        paused = true;
        // Cancel even while HTTP headers are pending: NetSparkle's convenience
        // method only cancels once its downloader has started writing the body.
        if (downloadPending) updater?.UpdateDownloader?.CancelDownload();
        if (Configured && !Ready) SetStatus("Update download paused. Check for updates to resume.");
    }
    public async Task InstallAsync()
    {
        if (Installing) return;
        if (updater == null || ready == null || downloadedPath == null) return;
        if (session.Operations.IsBusy || session.CurrentOperation != null) throw new InvalidOperationException("Finish or cancel the file operation before restarting.");
        PreparingInstall?.Invoke(); session.Save(); Installing = true;
        try { await updater.InstallUpdate(ready, downloadedPath); }
        catch { Installing = false; throw; }
    }
    public void Dispose() { timer.Stop(); updater?.Dispose(); releaseClient.Dispose(); }
}
