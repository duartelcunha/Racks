using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Reflection;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using System.Net.Http;
using System.Globalization;
namespace Racks
{
    public class Updater
    {
        private static string _url = "";
        private static string _downloadUrl = "";
        private static long _expectedSize = -1;
        private static string tag_name = "";
        private static int updateCount = 0;

        // The only host/path the update binary may come from. TLS proves it's really GitHub;
        // this proves it's OUR repo's release asset and not an arbitrary URL from the JSON.
        private const string TrustedAssetHost = "github.com";
        private const string TrustedAssetPathPrefix = "/duartelcunha/Racks/releases/download/";
        public static async Task CheckUpdateAsync(string url, bool showToastIfNoUpdate)
        {
            _url = url;
            string currentVersion = Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion!.ToString();
            try
            {
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Racks", currentVersion));
                    var response = await httpClient.GetStringAsync(_url);
                    Debug.WriteLine("got response");
                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string latestVersion = root.GetProperty("tag_name").GetString()!;
                        tag_name = latestVersion;
                        string description = root.GetProperty("body").GetString()!;
                        string published_at = root.GetProperty("published_at").GetString()!;
                        string name = root.GetProperty("name").GetString()!;
                        string emoji = (name.ToLower().Contains("fix"), name.ToLower().Contains("feature")) switch
                        {
                            (true, true) => "🚀",
                            (true, false) => "🪛", // (screwdriver)
                            (false, true) => "✨",
                            _ => "🚀"
                        };

                        // Select the installer asset BY NAME (not blind assets[0], which an extra
                        // asset uploaded first could hijack) and only if its URL is our GitHub
                        // release path over HTTPS. If none qualifies, we don't offer the update.
                        if (IsNewer(latestVersion, currentVersion) && TrySelectTrustedAsset(root, out string durl, out long dsize))
                        {
                            _downloadUrl = durl;
                            _expectedSize = dsize;
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
                        else if (showToastIfNoUpdate)
                        {
                            var toastBuilder = new ToastContentBuilder()
                               .AddText("You are up to date!", AdaptiveTextStyle.Header)
                               .AddText("There is no available update.", AdaptiveTextStyle.Body);
                            toastBuilder.Show();
                        }
                    }
                }
                updateCount++;
            }
            catch (Exception e)
            {
                if (updateCount != 0)
                {
                    var toastBuilder = new ToastContentBuilder()
                               .AddText("Failed to update.", AdaptiveTextStyle.Header)
                               .AddText(e.Message, AdaptiveTextStyle.Body);
                    toastBuilder.Show();
                }
                Debug.WriteLine($"Update error: {e.Message}");
            }
        }
        // Is the released tag newer than what's running? The GitHub tag is like "v1.1.4" while
        // FileVersion is a 4-part "1.1.4.0" - a plain string Contains() compare misfires (it
        // reported an update even when identical). Normalize both to major.minor.build and
        // compare numerically so "up to date" is actually detected.
        private static bool IsNewer(string latestTag, string currentRaw)
        {
            return Version.TryParse(Norm3(latestTag), out var latest)
                && Version.TryParse(Norm3(currentRaw), out var current)
                && latest > current;
        }

        private static string Norm3(string s)
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

        // Pick the release's installer asset by name (Racks-Setup-*.exe) and validate its URL.
        // Returns false if no asset matches or the URL isn't our trusted GitHub release path.
        private static bool TrySelectTrustedAsset(JsonElement root, out string url, out long size)
        {
            url = ""; size = -1;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var asset in assets.EnumerateArray())
            {
                string aname = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (!aname.StartsWith("Racks-Setup-", StringComparison.OrdinalIgnoreCase)
                    || !aname.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                string durl = asset.TryGetProperty("browser_download_url", out var d) ? (d.GetString() ?? "") : "";
                if (!IsTrustedDownloadUrl(durl)) continue;
                url = durl;
                size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : -1;
                return true;
            }
            return false;
        }

        // HTTPS + github.com + our repo's /releases/download/ path. The GitHub API always returns
        // such a URL; the 302 to objects.githubusercontent.com happens at download time and is
        // covered by TLS. Note: the app is not code-signed, so we can't verify Authenticode; this
        // shrinks the risk to a genuine compromise of the GitHub repo/release itself.
        private static bool IsTrustedDownloadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            return string.Equals(uri.Host, TrustedAssetHost, StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith(TrustedAssetPathPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task InstallUpdate()
        {
            // Defense in depth: never download/run a URL that isn't our trusted release asset,
            // even if _downloadUrl was somehow set without going through TrySelectTrustedAsset.
            if (!IsTrustedDownloadUrl(_downloadUrl))
            {
                new ToastContentBuilder()
                    .AddText("Update blocked", AdaptiveTextStyle.Header)
                    .AddText("The update source could not be verified as an official Racks release.", AdaptiveTextStyle.Body)
                    .Show();
                return;
            }

            string tag = "update";
            string group = "downloads";

            var toast = new ToastContentBuilder()
                .AddText($"Updating to {tag_name}", AdaptiveTextStyle.Header)
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

            using (var httpClient = new HttpClient())
            using (var response = await httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                // Random per-download name (not a fixed %TEMP%\Racks.exe): closes the window
                // where another same-user process could pre-place a binary at a predictable path
                // and have the trusted updater launch it.
                string tempFilePath = Path.Combine(Path.GetTempPath(), $"Racks-update-{Guid.NewGuid():N}.exe");

                using (var inputStream = await response.Content.ReadAsStreamAsync())
                using (var outputStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;
                    int lastProgress = -1;

                    while ((read = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await outputStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        if (canReportProgress)
                        {
                            int progress = (int)(((long)totalRead * 100) / totalBytes);
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
                }
                ToastNotificationManagerCompat.CreateToastNotifier().Hide(notif);

                // Verify the downloaded size matches what the release API reported. Guards against
                // a truncated/incomplete download (disk full, dropped connection) being executed.
                if (_expectedSize > 0)
                {
                    long actual = new FileInfo(tempFilePath).Length;
                    if (actual != _expectedSize)
                    {
                        try { File.Delete(tempFilePath); } catch { }
                        new ToastContentBuilder()
                            .AddText("Update download incomplete", AdaptiveTextStyle.Header)
                            .AddText("The downloaded installer did not match the expected size and was discarded.", AdaptiveTextStyle.Body)
                            .Show();
                        return;
                    }
                }

                await RestartApplication(tempFilePath);
            }
        }
        private static async Task RestartApplication(string installerPath)
        {
            // installerPath is the freshly downloaded Racks-Setup-x.y.z.exe. Run it and quit.
            // The installer upgrades in place (same AppId), force-closes this running instance
            // (CloseApplications=force) and relaunches Racks from its finished page - per-user
            // install, so no admin prompt. We deliberately do NOT overwrite our own exe: the
            // old approach moved the installer on top of Racks.exe, which left a broken install
            // (Racks.exe = the installer) if setup was cancelled.
            await Task.CompletedTask;
            try
            {
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
                var toastBuilder = new ToastContentBuilder()
                    .AddText("Update failed to start", AdaptiveTextStyle.Header)
                    .AddText(ex.Message, AdaptiveTextStyle.Body);
                toastBuilder.Show();
                return;
            }
            Environment.Exit(0);
        }
    }
}
