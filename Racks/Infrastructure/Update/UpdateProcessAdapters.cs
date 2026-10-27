using System.Diagnostics;
using Racks.UseCases.Updates;

namespace Racks.Infrastructure.Update;

/// <summary>Starts the verified installer through the shell, as the updater always has (per-user installer, no elevation).</summary>
internal sealed class ShellInstallerLauncher : IInstallerLauncher
{
    public void Start(string installerPath)
        => Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true });
}

/// <summary>Orderly WPF shutdown on the UI thread, whichever thread asks.</summary>
internal sealed class WpfAppShutdown : IAppShutdown
{
    public void Shutdown()
        => System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
}
