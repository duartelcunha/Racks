using System.Diagnostics;
using Racks.Infrastructure.Logging;
using Racks.UseCases.Updates;
using Racks.Util;

namespace Racks.Infrastructure.Update;

/// <summary>
/// The production wiring of the update use case: real toasts, the default HTTP handler, ShellExecute, the WPF dispatcher,
/// %TEMP%, the audit log and the public key compiled into this build. The one place that builds an <see cref="UpdateService"/>
/// for the app; tests build their own with fakes.
/// </summary>
internal static class UpdateComposition
{
    private static readonly Lazy<UpdateService> Shared = new(Create);

    public static UpdateService Service => Shared.Value;

    private static UpdateService Create()
    {
        var http = new UpdateHttpClientFactory();
        return new UpdateService(
            new ToastUpdateNotifier(),
            new ReleaseCatalogClient(http),
            new InstallerExecutor(http, new ShellInstallerLauncher(), new WpfAppShutdown()),
            AuditLog.Forwarder,
            UpdateTrust.PublicKeySpkiBase64,
            () => Process.GetCurrentProcess().MainModule!.FileVersionInfo.FileVersion!.ToString());
    }
}
