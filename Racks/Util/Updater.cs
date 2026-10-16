using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using System.Net.Http;
using System.Globalization;
using Racks.Util;
namespace Racks
{
    // Self-updater. INV-UPDATE-1: an installer is launched only when
    //   1. its release carries Racks-Setup-<v>.manifest.json + .sig, signed by the key compiled
    //      into this build (UpdateTrust), for exactly this release tag and a newer version, and
    //   2. the downloaded bytes have the signed size and SHA-256, checked through a handle that
    //      stays open (no writes, no deletes) until the installer has started.
    // Anything missing or mismatched fails closed with a message and nothing is launched. GitHub
    // (TLS, host, repo path) is only the transport; trust comes from the signature.
    public class Updater
    {
        internal sealed record ReleaseAssets(string Version, string InstallerUrl, string ManifestUrl, string SignatureUrl);
        private sealed record PendingUpdate(string Tag, ReleaseAssets Assets, UpdateManifest Manifest);

        // Set only after the manifest verified; Install uses nothing else.
        private static PendingUpdate? _pending;

        // The only host/path the update binary may come from. TLS proves it's really GitHub;
        // this proves it's OUR repo's release asset and not an arbitrary URL from the JSON.
        private const string TrustedAssetHost = "github.com";
        private const string TrustedAssetPathPrefix = "/duartelcunha/Racks/releases/download/";
        // Releases are read from this repository only.
        public const string LatestReleaseApi = "https://api.github.com/repos/duartelcunha/Racks/releases/latest";
        public const string ReleasesPage = "https://github.com/duartelcunha/Racks/releases";

        public static async Task CheckUpdateAsync(string url, bool showToastIfNoUpdate)
        {
            string currentVersion = Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion!.ToString();
            try
            {
                using (var httpClient = NewHttpClient(currentVersion))
                {
                    var response = await httpClient.GetStringAsync(url);
                    Debug.WriteLine("got response");
                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string latestVersion = root.GetProperty("tag_name").GetString()!;
                        string description = root.GetProperty("body").GetString()!;
                        string name = root.GetProperty("name").GetString()!;
                        string emoji = (name.ToLower().Contains("fix"), name.ToLower().Contains("feature")) switch
                        {
                            (true, true) => "🚀",
                            (true, false) => "🪛", // (screwdriver)
                            (false, true) => "✨",
                            _ => "🚀"
                        };

                        if (!IsNewer(latestVersion, currentVersion))
                        {
                            if (showToastIfNoUpdate)
                                ShowToast("You are up to date!", "There is no available update.");
                            return;
                        }

                        // A newer release exists. Offer it only if it is a correctly signed Racks
                        // release; otherwise say why (manual check) or stay quiet (startup check).
                        UpdateCheck check;
                        UpdateManifest? manifest = null;
                        if (!TrySelectSignedRelease(root, latestVersion, out var assets))
                            check = UpdateCheck.MissingManifest;
                        else
                            (check, manifest) = await FetchAndVerifyManifestAsync(httpClient, assets, latestVersion, currentVersion);

                        if (check == UpdateCheck.Ok && manifest != null)
                        {
                            _pending = new PendingUpdate(latestVersion, assets, manifest);
                            var toastBuilder = new ToastContentBuilder()
                                 .AddText($"{emoji} New release! {name}", AdaptiveTextStyle.Header)
                                 .AddText(description, AdaptiveTextStyle.Body)
                                 .AddButton(new ToastButton()
                                     .SetContent("Install")
                                     .AddArgument("action", "install_update")
                                     .SetBackgroundActivation())
                                 .AddButton(new ToastButton()
                                     .SetContent("Close")
                                     .AddArgument("action", "close")
                                     .SetBackgroundActivation());
                            toastBuilder.Show();
                        }
                        else
                        {
                            _pending = null;
                            Debug.WriteLine($"Update {latestVersion} not offered: {check}");
                            // Without a key the user should still learn that a new version exists.
                            if (showToastIfNoUpdate || check == UpdateCheck.NoTrustedKey)
                                ShowToast($"Racks {latestVersion} is available", $"{UpdateVerifier.Describe(check)} {ReleasesPage}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // A check the user asked for must never fail silently (being offline on the first
                // click used to show nothing). The automatic startup check stays quiet.
                if (showToastIfNoUpdate)
                    ShowToast("Failed to update.", e.Message);
                Debug.WriteLine($"Update error: {e.Message}");
            }
        }

        // Is the released tag newer than what's running? The GitHub tag is like "v1.1.4" while
        // FileVersion is a 4-part "1.1.4.0" - a plain string Contains() compare misfires (it
        // reported an update even when identical). Normalize both to major.minor.build and
        // compare numerically so "up to date" is actually detected.
        internal static bool IsNewer(string latestTag, string currentRaw)
        {
            return Version.TryParse(Norm3(latestTag), out var latest)
                && Version.TryParse(Norm3(currentRaw), out var current)
                && latest > current;
        }

        internal static string Norm3(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            s = s.Substring(0, i);
            var parts = s.Split('.');
            int Get(int idx) => parts.Length > idx && int.TryParse(parts[idx], out var v) ? v : 0;
            return $"{Get(0)}.{Get(1)}.{Get(2)}";
        }

        // Pick the release's three assets BY EXACT NAME for the tag's version (never assets[0],
        // which an extra asset uploaded first could hijack) and only on our GitHub release path
        // over HTTPS. A tag that is not "v<major>.<minor>.<patch>" has no signed assets.
        internal static bool TrySelectSignedRelease(JsonElement root, string releaseTag, out ReleaseAssets assets)
        {
            assets = new ReleaseAssets("", "", "", "");
            string version = releaseTag != null && releaseTag.StartsWith('v') ? releaseTag.Substring(1) : releaseTag ?? "";
            if (!UpdateManifest.IsValidVersion(version)) return false;
            if (!TryFindAsset(root, UpdateManifest.InstallerName(version), out string installer)
                || !TryFindAsset(root, UpdateManifest.ManifestName(version), out string manifest)
                || !TryFindAsset(root, UpdateManifest.SignatureName(version), out string signature))
                return false;
            assets = new ReleaseAssets(version, installer, manifest, signature);
            return true;
        }

        // The first asset with exactly this name whose URL is our trusted release path and ends in
        // that name. A bad asset does not hide a good later one.
        internal static bool TryFindAsset(JsonElement root, string assetName, out string url)
        {
            url = "";
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                string aname = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? (n.GetString() ?? "") : "";
                if (!string.Equals(aname, assetName, StringComparison.Ordinal)) continue;
                string durl = asset.TryGetProperty("browser_download_url", out var d) && d.ValueKind == JsonValueKind.String ? (d.GetString() ?? "") : "";
                if (!IsTrustedDownloadUrl(durl)) continue;
                if (!new Uri(durl).AbsolutePath.EndsWith("/" + assetName, StringComparison.Ordinal)) continue;
                url = durl;
                return true;
            }
            return false;
        }

        // HTTPS + github.com + our repo's /releases/download/ path. The GitHub API always returns
        // such a URL; the 302 to GitHub's asset storage happens at download time and is covered by
        // TLS. This is transport hygiene only: the signature is what makes an installer trusted.
        internal static bool IsTrustedDownloadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            return string.Equals(uri.Host, TrustedAssetHost, StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith(TrustedAssetPathPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // The manifest decision with the key compiled into this build. While UpdateTrust holds the
        // placeholder this returns NoTrustedKey for every release.
        internal static UpdateCheck VerifyReleaseManifest(byte[]? manifestBytes, string? signatureText, string releaseTag,
                                                          string currentVersion, out UpdateManifest? manifest)
            => UpdateVerifier.VerifyManifest(manifestBytes, signatureText, UpdateTrust.PublicKeySpkiBase64,
                                             releaseTag, currentVersion, out manifest);

        private static async Task<(UpdateCheck, UpdateManifest?)> FetchAndVerifyManifestAsync(
            HttpClient http, ReleaseAssets assets, string releaseTag, string currentVersion)
        {
            // No key, nothing to verify against: don't even download.
            if (!UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64)) return (UpdateCheck.NoTrustedKey, null);
            byte[]? manifestBytes = await GetSmallAssetAsync(http, assets.ManifestUrl, UpdateManifest.MaxManifestBytes);
            if (manifestBytes == null) return (UpdateCheck.MissingManifest, null);
            byte[]? sigBytes = await GetSmallAssetAsync(http, assets.SignatureUrl, UpdateManifest.MaxSignatureBytes);
            if (sigBytes == null) return (UpdateCheck.MissingSignature, null);
            string sig = Encoding.ASCII.GetString(sigBytes);
            var result = VerifyReleaseManifest(manifestBytes, sig, releaseTag, currentVersion, out var manifest);
            return (result, manifest);
        }

        // Download a small asset (manifest/signature) with a hard size cap. Null on any failure.
        private static async Task<byte[]?> GetSmallAssetAsync(HttpClient http, string url, int maxBytes)
        {
            if (!IsTrustedDownloadUrl(url)) return null;
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > maxBytes) return null;
                using var input = await response.Content.ReadAsStreamAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[4096];
                int read;
                while ((read = await input.ReadAsync(chunk)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > maxBytes) return null;
                }
                return buffer.ToArray();
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                Debug.WriteLine($"Update asset download failed: {ex.Message}");
                return null;
            }
        }

        public static async Task InstallUpdate()
        {
            // Only an update whose manifest verified in CheckUpdateAsync can be installed. A toast
            // clicked after a restart (state gone) or a tampered URL fails closed.
            var pending = _pending;
            if (pending == null || !IsTrustedDownloadUrl(pending.Assets.InstallerUrl))
            {
                ShowToast("Update blocked", "The update could not be verified as an official Racks release. Check for updates again.");
                return;
            }
            var manifest = pending.Manifest;

            string tag = "update";
            string group = "downloads";

            var toast = new ToastContentBuilder()
                .AddText($"Updating to {pending.Tag}", AdaptiveTextStyle.Header)
                .AddVisualChild(new AdaptiveProgressBar()
                {
                    Title = $"Progress",
                    Value = new BindableProgressBarValue("progressValue"),
                    ValueStringOverride = new BindableString("progressValueString"),
                    Status = new BindableString("progressStatus")
                })
                .GetToastContent();

            var notif = new ToastNotification(toast.GetXml())
            {
                Tag = tag,
                Group = group
            };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(notif);

            // Fresh, random, owner-only folder per download (INV-EXEC-1): nothing can be pre-placed
            // at a predictable path, and other accounts cannot reach it.
            string dir = "";
            FileStream? locked = null;
            try
            {
                dir = CreatePrivateTempDir();
                string installerPath = Path.Combine(dir, manifest.FileName);

                using (var httpClient = NewHttpClient(pending.Tag))
                using (var response = await httpClient.GetAsync(pending.Assets.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = manifest.Size;
                    bool tooBig = false;

                    using (var inputStream = await response.Content.ReadAsStreamAsync())
                    using (var outputStream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        int lastProgress = -1;

                        while ((read = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            totalRead += read;
                            // Never write more than the signed size.
                            if (totalRead > manifest.Size)
                            {
                                tooBig = true;
                                break;
                            }
                            await outputStream.WriteAsync(buffer, 0, read);

                            int progress = (int)((totalRead * 100) / totalBytes);
                            if (progress != lastProgress)
                            {
                                lastProgress = progress;

                                var data = new NotificationData
                                {
                                    SequenceNumber = (uint)progress,
                                    Values =
                                        {
                                            ["progressValue"] = (progress / 100.0).ToString("0.##", CultureInfo.InvariantCulture),
                                            ["progressValueString"] = $"{progress}%",
                                            ["progressStatus"] = "Downloading..."
                                        }
                                };

                                ToastNotificationManagerCompat.CreateToastNotifier().Update(data, tag, group);
                            }
                        }
                    }
                    if (tooBig)
                    {
                        Fail(UpdateCheck.SizeMismatch);
                        return;
                    }
                }
                ToastNotificationManagerCompat.CreateToastNotifier().Hide(notif);

                // Re-open read-only and deny writers and deleters, verify THROUGH this handle, and keep
                // it open until the installer has started: the bytes that run are the bytes checked.
                locked = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var result = UpdateVerifier.VerifyInstaller(manifest, locked);
                if (result != UpdateCheck.Ok)
                {
                    Fail(result);
                    return;
                }

                var handle = locked;
                locked = null; // RestartApplication owns it now
                await RestartApplication(installerPath, handle, dir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update download failed: {ex.Message}");
                ToastNotificationManagerCompat.CreateToastNotifier().Hide(notif);
                locked?.Dispose();
                locked = null;
                TryDeleteDir(dir);
                ShowToast("Update failed", ex.Message);
            }

            void Fail(UpdateCheck why)
            {
                ToastNotificationManagerCompat.CreateToastNotifier().Hide(notif);
                locked?.Dispose();
                locked = null;
                TryDeleteDir(dir);
                ShowToast("Update blocked", UpdateVerifier.Describe(why));
            }
        }

        private static async Task RestartApplication(string installerPath, FileStream verifiedHandle, string dir)
        {
            // installerPath is the freshly verified Racks-Setup-x.y.z.exe. Run it and quit.
            // The installer upgrades in place (same AppId), force-closes this running instance
            // (CloseApplications=force) and relaunches Racks from its finished page - per-user
            // install, so no admin prompt. We deliberately do NOT overwrite our own exe: the
            // old approach moved the installer on top of Racks.exe, which left a broken install
            // (Racks.exe = the installer) if setup was cancelled.
            await Task.CompletedTask;
            try
            {
                // verifiedHandle is still open (share: read only), so the file cannot be swapped
                // between the hash check and process creation. Once the process runs, its mapped
                // image cannot be written either.
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Couldn't even launch the installer - stay alive rather than exit into nothing.
                Debug.WriteLine($"Launching updater failed: {ex.Message}");
                verifiedHandle.Dispose(); // (finally disposes again; harmless) release before deleting
                TryDeleteDir(dir);
                ShowToast("Update failed to start", ex.Message);
                return;
            }
            finally
            {
                verifiedHandle.Dispose();
            }
            // Orderly shutdown instead of Environment.Exit: Exit skipped OnExit, which releases the
            // single-instance mutex (the relaunched Racks could be refused) and restores state. The
            // installer force-closes anything still running, so a stuck window cannot block the update.
            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
        }

        private static HttpClient NewHttpClient(string version)
        {
            var httpClient = new HttpClient();
            string v = string.IsNullOrWhiteSpace(version) ? "0" : version.Trim();
            try { httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Racks", v)); }
            catch (FormatException) { httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Racks", "0")); }
            return httpClient;
        }

        // %TEMP%\Racks-update-<guid>, DACL: current user only, not inherited.
        internal static string CreatePrivateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"Racks-update-{Guid.NewGuid():N}");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var me = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
            security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(dir).Create(security);
            return dir;
        }

        // Our own download folder holds one file and no subfolders: delete that, then the empty folder.
        internal static void TryDeleteDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
                Directory.Delete(dir);
            }
            catch { }
        }

        private static void ShowToast(string header, string body)
            => new ToastContentBuilder()
                .AddText(header, AdaptiveTextStyle.Header)
                .AddText(body, AdaptiveTextStyle.Body)
                .Show();
    }
}
