using System.Windows;
using Racks.Infrastructure.Startup;
using Racks.UseCases.Startup;
using Application = System.Windows.Application;

namespace Racks
{
    /// <summary>
    /// Bootstraps the app and exposes the WPF lifecycle. The work lives in <c>Racks.UseCases.Startup</c> (the ordered startup
    /// steps, migrations, single instance, uninstall mode) and <c>Racks.UseCases.Crash</c> (the last-resort exception logging).
    /// </summary>
    public partial class App : Application
    {
        private readonly AppBootstrapService _bootstrap;
        private StartupEventArgs? _startupArgs;

        public App()
        {
            // Crash handlers are wired in the constructor so they are active before OnStartup does anything.
            var services = AppComposition.Create(this, RaiseBaseStartup);
            services.Crash.Attach(this);
            _bootstrap = services.Bootstrap;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _startupArgs = e;
            _bootstrap.Run(e.Args);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _bootstrap.Exit();
            base.OnExit(e);
        }

        private void RaiseBaseStartup() => base.OnStartup(_startupArgs!);
    }
}
