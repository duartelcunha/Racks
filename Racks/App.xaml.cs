using System.Windows;
using Racks.Infrastructure.Startup;
using Application = System.Windows.Application;

namespace Racks
{
    /// <summary>
    /// Bootstraps the app and exposes the WPF lifecycle. The work lives in <c>Racks.UseCases.Startup</c> (the ordered startup
    /// steps, migrations, single instance, uninstall mode) and <c>Racks.UseCases.Crash</c> (the last-resort exception logging).
    /// </summary>
    public partial class App : Application
    {
        private readonly AppServices _services;
        private StartupEventArgs? _startupArgs;

        public App()
        {
            // The crash hooks come first, so they are attached before anything below can throw. The uninstaller flag is
            // read when a crash happens, by which time the services exist.
            CrashHooks.Attach(this, AppComposition.CreateCrashService(() => _services?.Uninstall.IsRunningForUninstaller ?? false));
            _services = AppComposition.Create(this, RaiseBaseStartup);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _startupArgs = e;
            _services.Bootstrap.Run(e.Args);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _services.Bootstrap.Exit();
            base.OnExit(e);
        }

        private void RaiseBaseStartup() => base.OnStartup(_startupArgs!);
    }
}
