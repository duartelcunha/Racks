using System;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
using Application = System.Windows.Application;

namespace Racks.Util
{
    // First-launch welcome. The signature "logo drops into the tray" animation already
    // plays on every launch (Views/StartupAnimationWindow), so first run doesn't replay
    // it - it just follows up with a one-time toast telling the user where Racks lives.
    //
    // Runs at most once per machine, gated by HKCU\SOFTWARE\Racks\FirstRunWelcomeShownV2.
    // Failures are swallowed; the app must never crash because the welcome flow misbehaved.
    public static class FirstRunWelcome
    {
        private const string MarkerKey = "FirstRunWelcomeShownV2";

        public static void ShowIfFirstRun(RegistryHelper reg)
        {
            try
            {
                if (reg.KeyExistsRoot(MarkerKey)) return;
                reg.WriteToRegistryRoot(MarkerKey, true);
            }
            catch { return; }

            // Let the startup animation play out first, then follow with the toast.
            Task.Delay(3000).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ShowToast(); } catch { /* toast is best-effort */ }
                }));
            });
        }

        private static void ShowToast()
        {
            new ToastContentBuilder()
                .AddText("Racks is in your system tray")
                .AddText("Right-click the tray icon to create a rack, open the quick finder (Ctrl+Shift+Space), or change settings.")
                .Show();
        }
    }
}
