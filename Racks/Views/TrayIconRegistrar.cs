using System.Diagnostics;
using System.Windows;


namespace Racks.Views
{
    /// <summary>
    /// The tray icon is the only way back into a hidden app, so registration is guarded (a throw here used to skip the rest
    /// of <c>OnSourceInitialized</c> too) and always forces the icon visible. <c>Register()</c> is idempotent, so the startup
    /// retry and the TaskbarCreated handler can call this freely.
    /// </summary>
    public static class TrayIconRegistrar
    {
        public static void Register(Wpf.Ui.Tray.Controls.NotifyIcon trayIcon)
        {
            try
            {
                try
                {
                    int iconSize = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
                    string resName = iconSize switch
                    {
                        <= 16 => "tray_16.png",
                        <= 20 => "tray_20.png",
                        <= 24 => "tray_24.png",
                        _ => "tray_32.png"
                    };
                    trayIcon.Icon = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri($"/Icon/{resName}", UriKind.Relative));
                }
                catch { /* fallback to XAML default */ }

                trayIcon.Register();
                trayIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Debug.WriteLine($"TrayIcon register failed: {ex.Message}"); }
        }
    }
}
