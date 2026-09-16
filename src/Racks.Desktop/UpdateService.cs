using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
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
    private bool paused;
    public string Status { get; private set; } = "Check for the latest stable release on GitHub. Automatic installation is available in builds configured for signed updates.";
    public string CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Unknown";
    public bool Configured => updater != null;
    public bool Ready => ready != null;
    public bool Installing { get; private set; }
    public event Action? Changed;
    public event Action? CloseRequested;

    public UpdateService(Session session)
    {
        this.session = session;
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value);
        var key = metadata.GetValueOrDefault("UpdatePublicKey");
        var feed = metadata.GetValueOrDefault("UpdateFeedBase");
        if (!string.IsNullOrWhiteSpace(key) && Uri.TryCreate(feed, UriKind.Absolute, out var url) && url.Scheme == "https")
        {
            var platform = OperatingSystem.IsMacOS() ? "osx" : "win";
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            updater = new SparkleUpdater(url.ToString().TrimEnd('/') + $"/{platform}-{arch}/appcast.xml", new Ed25519Checker(SecurityMode.Strict, key))
            { UserInteractionMode = UserInteractionMode.DownloadNoInstall, RelaunchAfterUpdate = true, ShouldKillParentProcessWhenStartingInstaller = false };
            updater.DownloadFinished += (item, path) => Dispatcher.UIThread.Post(() => { ready = item; downloadedPath = path; SetStatus("Verified update ready. Restart to install when your work is finished."); });
            updater.DownloadHadError += (_, _, error) => SetStatus("Download failed: " + error.Message);
            updater.DownloadedFileIsCorrupt += (_, _) => SetStatus("Update rejected: its signature is invalid.");
            updater.DownloadedFileThrewWhileCheckingSignature += (_, _) => SetStatus("Update rejected: its signature could not be checked.");
            updater.DownloadMadeProgress += (_, _, progress) => SetStatus($"Downloading update · {progress.ProgressPercentage}%");
            updater.DownloadCanceled += (_, _) => SetStatus("Update download paused.");
            updater.PreparingToExit += (_, args) => args.Cancel = session.Operations.IsBusy || session.CurrentOperation != null;
            updater.CloseApplication += () => Dispatcher.UIThread.Post(() => CloseRequested?.Invoke());
            updater.InstallUpdateFailed += (reason, _) => { Installing = false; SetStatus("Update could not be installed: " + reason); return true; };
            Status = "Ready to check for signed updates.";
        }
        timer.Tick += async (_, _) => { if (session.Settings.AutoUpdates) await CheckAsync(); };
    }

    private void SetStatus(string value) => Dispatcher.UIThread.Post(() => { Status = value; Changed?.Invoke(); });
    public void Start() { if (!Configured) return; timer.Start(); if (session.Settings.AutoUpdates) _ = CheckAsync(); }
    public async Task CheckAsync()
    {
        if (checking || Ready || Installing) return;
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
            if (info.Status == UpdateStatus.UpdateAvailable && !paused && info.Updates.FirstOrDefault() is { } item && !updater.IsDownloadingItem(item))
                await updater.InitAndBeginDownload(item);
            if (info.Status == UpdateStatus.UpdateNotAvailable) SetStatus("You have the latest release.");
            else if (info.Status == UpdateStatus.CouldNotDetermine) SetStatus("Could not verify the release feed. Try again later.");
        }
        catch (OperationCanceledException) { SetStatus("The update check timed out. Check your connection and try again, or view releases in your browser."); }
        catch (Exception ex) { SetStatus("Update check failed: " + ex.Message + " You can try again or view releases in your browser."); }
        finally { checking = false; }
    }
    public void Pause() { paused = true; updater?.CancelFileDownload(); }
    public async Task InstallAsync()
    {
        if (updater == null || ready == null || downloadedPath == null) return;
        if (session.Operations.IsBusy || session.CurrentOperation != null) throw new InvalidOperationException("Finish or cancel the file operation before restarting.");
        session.Save(); Installing = true;
        try { await updater.InstallUpdate(ready, downloadedPath); }
        catch { Installing = false; throw; }
    }
    public void Dispose() { timer.Stop(); updater?.Dispose(); releaseClient.Dispose(); }
}
