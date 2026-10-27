using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Update;
using Racks.Util;

namespace Racks.UseCases.Updates;

/// <summary>
/// Self-update. INV-UPDATE-1: an installer is launched only when
/// <list type="number">
/// <item>its release carries Racks-Setup-&lt;v&gt;.manifest.json + .sig, signed by the key compiled into this build
/// (<see cref="UpdateTrust"/>), for exactly this release tag and a newer version, from our GitHub release path over HTTPS, and</item>
/// <item>the downloaded bytes have the signed size and SHA-256, checked through a handle that stays open (no writes, no deletes)
/// until the installer has started (<see cref="InstallerExecutor"/>).</item>
/// </list>
/// Anything missing or mismatched fails closed: a message, an audit record, nothing launched. GitHub (TLS, host, repo path) is
/// only the transport; trust comes from the signature. This class is the orchestration: which release to offer
/// (<see cref="ReleaseManifestValidator"/>), what is fetched and how much (<see cref="ReleaseCatalogClient"/>), the download and
/// launch (<see cref="InstallerExecutor"/>), what the user sees (<see cref="IUpdateNotifier"/>, <see cref="UpdateMessages"/>).
///
/// Never throws to its callers (a toast that fails is logged, not propagated). Calls on the interfaces happen on the caller's
/// context (the UI thread in production, where the awaits resume); only <see cref="IAppShutdown"/> marshals itself.
/// </summary>
internal sealed class UpdateService
{
    private readonly IUpdateNotifier _notifier;
    private readonly ReleaseCatalogClient _catalog;
    private readonly InstallerExecutor _installer;
    private readonly IAuditSink _audit;
    private readonly string _publicKeySpkiBase64;
    private readonly Func<string> _runningVersion;

    private readonly object _gate = new();
    // Set only after the manifest verified; Install uses nothing else. Guarded by _gate; the record is immutable.
    private PendingUpdate? _pending;
    // 0 or 1: one install at a time.
    private int _installing;

    public UpdateService(IUpdateNotifier notifier, ReleaseCatalogClient catalog, InstallerExecutor installer, IAuditSink audit,
                         string publicKeySpkiBase64, Func<string> runningVersion)
    {
        _notifier = notifier;
        _catalog = catalog;
        _installer = installer;
        _audit = audit;
        _publicKeySpkiBase64 = publicKeySpkiBase64;
        _runningVersion = runningVersion;
    }

    /// <summary>The current offer (null when none). A snapshot; for tests.</summary>
    internal PendingUpdate? Pending { get { lock (_gate) return _pending; } }

    private void SetPending(PendingUpdate? value) { lock (_gate) _pending = value; }

    /// <summary>
    /// Reads the first page of releases (up to 30, newest first as GitHub lists them) and offers the NEWEST release that is
    /// correctly signed and newer than this build. Looking past /releases/latest means one broken or tampered release (a
    /// missing .sig, a swapped installer) cannot hide an older good update. Only a release whose signed manifest verified is
    /// ever named (its signed version); nothing else from the API (tag, name, body) is shown, and a release that does not
    /// verify is never named or linked.
    /// </summary>
    public async Task CheckAsync(string url, bool showToastIfNoUpdate, CancellationToken ct = default)
    {
        // No key compiled in: nothing can be verified, so nothing is fetched or named. Only a check the user asked for says
        // so (not every startup).
        if (!UpdateTrust.IsConfigured(_publicKeySpkiBase64))
        {
            SetPending(null);
            if (showToastIfNoUpdate)
                Notify(() => _notifier.Show(UpdateMessages.NoKeyHeader, UpdateMessages.NoKeyBody));
            return;
        }

        try
        {
            string currentVersion = _runningVersion();
            using var http = _catalog.CreateClient(currentVersion);
            string feed = await _catalog.GetCatalogAsync(http, url, ct);
            using JsonDocument doc = JsonDocument.Parse(feed);
            var candidates = ReleaseManifestValidator.SelectCandidates(doc.RootElement, currentVersion, out string? newestVersion);

            if (newestVersion == null)
            {
                SetPending(null);   // nothing newer than this build is listed any more: an older toast offers nothing
                if (showToastIfNoUpdate)
                    Notify(() => _notifier.Show(UpdateMessages.UpToDateHeader, UpdateMessages.UpToDateBody));
                return;
            }

            var (offer, failure) = await ReleaseManifestValidator.ChooseVerifiedAsync(candidates,
                assets => FetchAndVerifyManifestAsync(http, assets, currentVersion, ct));

            if (offer != null)
            {
                SetPending(offer);
                Notify(() => _notifier.ShowOffer(UpdateMessages.OfferHeader(offer.Manifest), UpdateMessages.OfferBody));
                return;
            }

            SetPending(null);
            Debug.WriteLine($"Newest release not offered: {failure}");
            UpdateAudit.Write(_audit, UpdateAudit.CheckFailedKind, url, UpdateVerifier.Describe(failure), "Rejected", $"reason={failure}");
            if (showToastIfNoUpdate)
                Notify(() => _notifier.Show(UpdateMessages.NotVerifiedHeader, UpdateMessages.NotVerifiedBody));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // A check the user asked for must never fail silently (being offline on the first click used to show nothing).
            // The automatic startup check stays quiet on screen, but the reason is in the audit log.
            Debug.WriteLine($"Update error: {e.Message}");
            // A check that failed (offline, bad feed, anything) withdraws the earlier offer: an old toast must not install an
            // offer that this check could not confirm.
            SetPending(null);
            UpdateAudit.Write(_audit, UpdateAudit.CheckFailedKind, url, e.Message, "Failed", $"exception={e.GetType().Name}");
            if (showToastIfNoUpdate)
                Notify(() => _notifier.Show(UpdateMessages.CheckFailedHeader, e.Message));
        }
    }

    private async Task<(UpdateCheck, UpdateManifest?)> FetchAndVerifyManifestAsync(
        HttpClient http, ReleaseAssets assets, string currentVersion, CancellationToken ct)
    {
        string releaseTag = "v" + assets.Version;
        byte[]? manifestBytes = await GetSmallAssetAsync(http, assets.ManifestUrl, UpdateManifest.MaxManifestBytes, ct);
        if (manifestBytes == null) return (UpdateCheck.MissingManifest, null);
        byte[]? sigBytes = await GetSmallAssetAsync(http, assets.SignatureUrl, UpdateManifest.MaxSignatureBytes, ct);
        if (sigBytes == null) return (UpdateCheck.MissingSignature, null);
        string sig = Encoding.ASCII.GetString(sigBytes);
        var result = ReleaseManifestValidator.VerifyReleaseManifest(manifestBytes, sig, releaseTag, currentVersion, out var manifest, _publicKeySpkiBase64);
        return (result, manifest);
    }

    // Only URLs on our release path are ever fetched; anything else counts as a missing asset.
    private Task<byte[]?> GetSmallAssetAsync(HttpClient http, string url, int maxBytes, CancellationToken ct)
        => ReleaseManifestValidator.IsTrustedDownloadUrl(url)
            ? _catalog.GetSmallAssetAsync(http, url, maxBytes, ct)
            : Task.FromResult<byte[]?>(null);

    /// <summary>
    /// Downloads and starts the update that the last check verified, if its bytes match the signed size and SHA-256. One
    /// install at a time: a second call while one runs does nothing (the first one shows progress).
    /// </summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _installing, 1, 0) != 0)
        {
            UpdateAudit.Write(_audit, UpdateAudit.InstallRefusedKind, null, "An update is already being installed.", "Rejected", "reason=AlreadyRunning");
            return;
        }
        try
        {
            await InstallCoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The executor reports its own failures as outcomes; this is the last net so nothing reaches the dispatcher.
            Debug.WriteLine($"Update install error: {ex.Message}");
            UpdateAudit.Write(_audit, UpdateAudit.InstallFailedKind, null, ex.Message, "Failed", $"exception={ex.GetType().Name}");
            Notify(() => _notifier.Show(UpdateMessages.FailedHeader, ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    private async Task InstallCoreAsync(CancellationToken ct)
    {
        // Only an update whose manifest verified in CheckAsync can be installed. A toast clicked after a restart (state gone)
        // or a tampered URL fails closed.
        PendingUpdate? pending = Pending;
        if (pending == null || !ReleaseManifestValidator.IsTrustedDownloadUrl(pending.Assets.InstallerUrl))
        {
            UpdateAudit.Write(_audit, UpdateAudit.InstallRefusedKind, pending?.Assets.InstallerUrl,
                "No verified update is waiting to be installed.", "Rejected", "reason=NoVerifiedOffer");
            Notify(() => _notifier.Show(UpdateMessages.BlockedHeader, UpdateMessages.BlockedNoOfferBody));
            return;
        }

        IUpdateProgress progress = StartProgress(pending.Tag);
        string runningVersion = SafeRunningVersion();

        var outcome = await _installer.RunAsync(pending.Assets.InstallerUrl, pending.Manifest, runningVersion, new SafeProgress(progress), ct);

        string installerUrl = pending.Assets.InstallerUrl;
        switch (outcome.Status)
        {
            case InstallStatus.Launched:
                // The offer is used up; the app is shutting down.
                lock (_gate) { if (ReferenceEquals(_pending, pending)) _pending = null; }
                // outcome.Message is only set when the shutdown call failed after the start: still a launch, recorded, not a failure.
                UpdateAudit.Write(_audit, UpdateAudit.InstallStartedKind, installerUrl, "The verified installer was started.", "Succeeded",
                    outcome.Message.Length == 0 ? $"version={pending.Manifest.Version}" : $"version={pending.Manifest.Version}; shutdown failed: {outcome.Message}");
                break;
            case InstallStatus.VerificationFailed:
                UpdateAudit.Write(_audit, UpdateAudit.InstallRefusedKind, installerUrl, UpdateVerifier.Describe(outcome.Check), "Rejected", $"reason={outcome.Check}");
                Notify(() => _notifier.Show(UpdateMessages.BlockedHeader, UpdateMessages.BlockedBody(outcome.Check)));
                break;
            case InstallStatus.LaunchFailed:
                UpdateAudit.Write(_audit, UpdateAudit.InstallFailedKind, installerUrl, outcome.Message, "Failed", "reason=LaunchFailed");
                Notify(() => _notifier.Show(UpdateMessages.FailedToStartHeader, outcome.Message));
                break;
            default:
                UpdateAudit.Write(_audit, UpdateAudit.InstallFailedKind, installerUrl, outcome.Message, "Failed", "reason=DownloadFailed");
                Notify(() => _notifier.Show(UpdateMessages.FailedHeader, outcome.Message));
                break;
        }
    }

    // The download identifies itself with the version that is RUNNING (like the check), not with the release tag.
    private string SafeRunningVersion()
    {
        try { return _runningVersion(); }
        catch (Exception ex) { Debug.WriteLine($"Running version unknown: {ex.Message}"); return "0"; }
    }

    // A toast that fails to appear must not stop the update (or crash the dispatcher).
    private IUpdateProgress StartProgress(string verifiedTag)
    {
        try { return _notifier.StartProgress(UpdateMessages.ProgressHeader(verifiedTag)); }
        catch (Exception ex) { Debug.WriteLine($"Progress toast failed: {ex.Message}"); return NoProgress.Instance; }
    }

    private static void Notify(Action show)
    {
        try { show(); }
        catch (Exception ex) { Debug.WriteLine($"Update toast failed: {ex.Message}"); }
    }

    private sealed class NoProgress : IUpdateProgress
    {
        public static readonly NoProgress Instance = new();
        public void Report(int percent) { }
        public void Hide() { }
    }

    // Progress reporting is cosmetic: an exception from the toast must not fail a download.
    private sealed class SafeProgress(IUpdateProgress inner) : IUpdateProgress
    {
        public void Report(int percent) { try { inner.Report(percent); } catch (Exception ex) { Debug.WriteLine($"Progress toast failed: {ex.Message}"); } }
        public void Hide() { try { inner.Hide(); } catch (Exception ex) { Debug.WriteLine($"Progress toast failed: {ex.Message}"); } }
    }
}
