using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using Racks.Util;
using Racks.UseCases.Updates;

namespace Racks.Infrastructure.Update;

internal enum InstallStatus
{
    /// <summary>The verified installer was started and the application was asked to shut down.</summary>
    Launched,
    /// <summary>The download did not have the signed size or SHA-256 (<see cref="InstallOutcome.Check"/>). It was deleted.</summary>
    VerificationFailed,
    /// <summary>Network, disk or timeout trouble. Whatever was downloaded was deleted.</summary>
    DownloadFailed,
    /// <summary>The installer verified but could not be started. The download was deleted and the app keeps running.</summary>
    LaunchFailed,
}

internal sealed record InstallOutcome(InstallStatus Status, UpdateCheck Check, string Message)
{
    public static InstallOutcome Launched() => new(InstallStatus.Launched, UpdateCheck.Ok, "");
}

/// <summary>
/// Downloads the installer that a verified manifest describes and starts it, only if the bytes match (INV-UPDATE-1, INV-EXEC-1):
/// <list type="number">
/// <item>into a fresh, random, owner-only folder (nothing can be pre-placed at a predictable path), created with CreateNew and
/// FileShare.None;</item>
/// <item>never more than the signed size is written: the first read past it aborts the download;</item>
/// <item>every read has a timeout, and the caller's token cancels the download;</item>
/// <item>the file is re-opened read-only denying writers and deleters, size and SHA-256 are checked THROUGH that handle, and the
/// handle stays open until the installer process has started: the bytes that run are the bytes checked;</item>
/// <item>on any failure the folder is removed. The only folder that stays is the one the installer runs from after a successful
/// start (it cannot be deleted while it runs; <see cref="UpdateCleanupService"/> removes it later).</item>
/// </list>
/// The URL must already have been checked against the trusted release path by the caller (the use case does, right before this).
/// </summary>
internal sealed class InstallerExecutor
{
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);

    private readonly UpdateHttpClientFactory _http;
    private readonly IInstallerLauncher _launcher;
    private readonly IAppShutdown _shutdown;
    private readonly string _tempRoot;
    private readonly TimeSpan _readTimeout;

    public InstallerExecutor(UpdateHttpClientFactory http, IInstallerLauncher launcher, IAppShutdown shutdown,
                             string? tempRoot = null, TimeSpan? readTimeout = null)
    {
        _http = http;
        _launcher = launcher;
        _shutdown = shutdown;
        _tempRoot = tempRoot ?? Path.GetTempPath();
        _readTimeout = readTimeout ?? DefaultReadTimeout;
    }

    public async Task<InstallOutcome> RunAsync(string installerUrl, UpdateManifest manifest, string userAgentVersion,
                                               IUpdateProgress progress, CancellationToken ct = default)
    {
        string dir = "";
        FileStream? locked = null;
        try
        {
            dir = CreatePrivateTempDir(_tempRoot);
            string installerPath = Path.Combine(dir, manifest.FileName);
            bool tooBig = false;

            using (var http = _http.Create(userAgentVersion))
            using (var response = await http.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = manifest.Size;

                using var inputStream = await response.Content.ReadAsStreamAsync(ct);
                using var outputStream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                int lastProgress = -1;

                while ((read = await ReadWithTimeoutAsync(inputStream, buffer, ct)) > 0)
                {
                    totalRead += read;
                    // Never write more than the signed size.
                    if (totalRead > manifest.Size)
                    {
                        tooBig = true;
                        break;
                    }
                    await outputStream.WriteAsync(buffer.AsMemory(0, read), ct);

                    int percent = (int)((totalRead * 100) / totalBytes);
                    if (percent != lastProgress)
                    {
                        lastProgress = percent;
                        progress.Report(percent);
                    }
                }
            }
            if (tooBig) return Refuse(UpdateCheck.SizeMismatch);
            progress.Hide();

            // Re-open read-only and deny writers and deleters, verify THROUGH this handle, and keep it open until the
            // installer has started: the bytes that run are the bytes checked.
            locked = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = UpdateVerifier.VerifyInstaller(manifest, locked);
            if (result != UpdateCheck.Ok) return Refuse(result);

            try
            {
                // `locked` is still open (share: read only), so the file cannot be swapped between the hash check and
                // process creation. Once the process runs, its mapped image cannot be written either.
                _launcher.Start(installerPath);
            }
            catch (Exception ex)
            {
                // Couldn't even launch the installer - stay alive rather than exit into nothing.
                Debug.WriteLine($"Launching updater failed: {ex.Message}");
                locked.Dispose();
                locked = null;
                UpdateCleanupService.TryDeleteDir(dir);
                return new InstallOutcome(InstallStatus.LaunchFailed, UpdateCheck.Ok, ex.Message);
            }
            locked.Dispose();
            locked = null;

            // Orderly shutdown instead of Environment.Exit: Exit skipped OnExit, which releases the single-instance mutex (the
            // relaunched Racks could be refused) and restores state. The installer force-closes anything still running, so a
            // stuck window cannot block the update. The folder stays: the installer runs from it.
            _shutdown.Shutdown();
            return InstallOutcome.Launched();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            progress.Hide();
            locked?.Dispose();
            locked = null;
            UpdateCleanupService.TryDeleteDir(dir);
            return new InstallOutcome(InstallStatus.DownloadFailed, UpdateCheck.Ok, ex.Message);
        }

        InstallOutcome Refuse(UpdateCheck why)
        {
            progress.Hide();
            locked?.Dispose();
            locked = null;
            UpdateCleanupService.TryDeleteDir(dir);
            return new InstallOutcome(InstallStatus.VerificationFailed, why, "");
        }
    }

    // A read that stalls longer than the read timeout fails the download instead of hanging it; the caller's token still wins.
    private async Task<int> ReadWithTimeoutAsync(Stream input, byte[] buffer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_readTimeout);
        try
        {
            return await input.ReadAsync(buffer.AsMemory(), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The download stalled and was stopped.");
        }
    }

    // <root>\Racks-update-<guid>, DACL: current user only, not inherited. Root defaults to %TEMP%.
    internal static string CreatePrivateTempDir(string? tempRoot = null)
    {
        string dir = Path.Combine(tempRoot ?? Path.GetTempPath(), $"Racks-update-{Guid.NewGuid():N}");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var me = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).Create(security);
        return dir;
    }
}
