using Racks.UseCases.Crash;
using Application = System.Windows.Application;

namespace Racks.Infrastructure.Startup;

/// <summary>Where the three process-wide exception events come from; each handler's result says what to tell the runtime.</summary>
internal interface ICrashHookSource
{
    /// <summary>UI-thread exception; the handler returns true when it must be marked handled.</summary>
    void OnDispatcherException(Func<Exception, bool> handler);
    /// <summary>Non-UI-thread exception (the runtime then ends the process).</summary>
    void OnDomainException(Action<object?> handler);
    /// <summary>Unobserved task exception; the handler returns true when it must be marked observed.</summary>
    void OnUnobservedTaskException(Func<Exception, bool> handler);
}

/// <summary>Connects <see cref="CrashReportingService"/> to the real WPF, AppDomain and TaskScheduler events.</summary>
internal static class CrashHooks
{
    /// <summary>Called first in the App constructor, so the hooks exist before anything else there can throw.</summary>
    public static void Attach(Application app, CrashReportingService service) => Wire(new WpfCrashHookSource(app), service);

    internal static void Wire(ICrashHookSource source, CrashReportingService service)
    {
        source.OnDispatcherException(service.HandleDispatcherException);
        source.OnDomainException(service.HandleDomainException);
        source.OnUnobservedTaskException(service.HandleUnobservedTaskException);
    }

    private sealed class WpfCrashHookSource(Application app) : ICrashHookSource
    {
        public void OnDispatcherException(Func<Exception, bool> handler) =>
            app.DispatcherUnhandledException += (_, e) => e.Handled = handler(e.Exception);

        public void OnDomainException(Action<object?> handler) =>
            AppDomain.CurrentDomain.UnhandledException += (_, e) => handler(e.ExceptionObject);

        public void OnUnobservedTaskException(Func<Exception, bool> handler) =>
            TaskScheduler.UnobservedTaskException += (_, e) => { if (handler(e.Exception)) e.SetObserved(); };
    }
}
