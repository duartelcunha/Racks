using System.Globalization;
using Microsoft.Toolkit.Uwp.Notifications;
using Racks.UseCases.Updates;
using Windows.UI.Notifications;

namespace Racks.Infrastructure.Update;

/// <summary>The updater's Windows toasts (the WinRT code that used to sit inside Updater). The Install button comes back as the "install_update" action.</summary>
internal sealed class ToastUpdateNotifier : IUpdateNotifier
{
    private const string ToastTag = "update";
    private const string ToastGroup = "downloads";

    public void Show(string header, string body)
        => new ToastContentBuilder()
            .AddText(header, AdaptiveTextStyle.Header)
            .AddText(body, AdaptiveTextStyle.Body)
            .Show();

    public void ShowOffer(string header, string body)
        => new ToastContentBuilder()
            .AddText(header, AdaptiveTextStyle.Header)
            .AddText(body, AdaptiveTextStyle.Body)
            .AddButton(new ToastButton()
                .SetContent("Install")
                .AddArgument("action", "install_update")
                .SetBackgroundActivation())
            .AddButton(new ToastButton()
                .SetContent("Close")
                .AddArgument("action", "close")
                .SetBackgroundActivation())
            .Show();

    public IUpdateProgress StartProgress(string header)
    {
        var toast = new ToastContentBuilder()
            .AddText(header, AdaptiveTextStyle.Header)
            .AddVisualChild(new AdaptiveProgressBar()
            {
                Title = "Progress",
                Value = new BindableProgressBarValue("progressValue"),
                ValueStringOverride = new BindableString("progressValueString"),
                Status = new BindableString("progressStatus")
            })
            .GetToastContent();
        var notif = new ToastNotification(toast.GetXml()) { Tag = ToastTag, Group = ToastGroup };
        ToastNotificationManagerCompat.CreateToastNotifier().Show(notif);
        return new Progress(notif);
    }

    private sealed class Progress(ToastNotification notif) : IUpdateProgress
    {
        public void Report(int percent)
        {
            var data = new NotificationData
            {
                SequenceNumber = (uint)percent,
                Values =
                {
                    ["progressValue"] = (percent / 100.0).ToString("0.##", CultureInfo.InvariantCulture),
                    ["progressValueString"] = $"{percent}%",
                    ["progressStatus"] = "Downloading..."
                }
            };
            ToastNotificationManagerCompat.CreateToastNotifier().Update(data, ToastTag, ToastGroup);
        }

        public void Hide() => ToastNotificationManagerCompat.CreateToastNotifier().Hide(notif);
    }
}
