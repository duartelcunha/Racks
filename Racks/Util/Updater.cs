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
        private static string tag_name = "";
        private static int updateCount = 0;
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
                        string downloadUrl = root.GetProperty("assets")[0].GetProperty("browser_download_url").GetString()!;
                        _downloadUrl = downloadUrl;
                        string emoji = (name.ToLower().Contains("fix"), name.ToLower().Contains("feature")) switch
                        {
                            (true, true) => "🚀",
                            (true, false) => "🪛", // (screwdriver)
                            (false, true) => "✨",
                            _ => "🚀"
                        };

                        if (IsNewer(latestVersion, currentVersion))
                        {
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

        public static async Task InstallUpdate()
        {
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

                string tempFilePath = Path.Combine(Path.GetTempPath(), $"{Assembly.GetExecutingAssembly().GetName().Name}.exe");

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
