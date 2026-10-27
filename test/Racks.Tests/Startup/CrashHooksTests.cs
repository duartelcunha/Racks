using Racks.Infrastructure.Startup;
using Racks.UseCases.Crash;

namespace Racks.Tests.Startup;

public sealed class CrashHooksTests
{
    private sealed class FakeSource : ICrashHookSource
    {
        public Func<Exception, bool>? Dispatcher;
        public Action<object?>? Domain;
        public Func<Exception, bool>? Unobserved;
        public void OnDispatcherException(Func<Exception, bool> h) => Dispatcher = h;
        public void OnDomainException(Action<object?> h) => Domain = h;
        public void OnUnobservedTaskException(Func<Exception, bool> h) => Unobserved = h;
    }

    private readonly List<string> _entries = new();
    private int _toasts;
    private bool _uninstaller;

    private FakeSource Wire()
    {
        var service = new CrashReportingService(() => "dir", (_, e) => _entries.Add(e), () => _toasts++, () => _uninstaller, trace: _ => { });
        var source = new FakeSource();
        CrashHooks.Wire(source, service);
        return source;
    }

    [Fact]
    public void All_three_hooks_are_wired()
    {
        var s = Wire();
        Assert.NotNull(s.Dispatcher);
        Assert.NotNull(s.Domain);
        Assert.NotNull(s.Unobserved);
    }

    [Fact]
    public void A_dispatcher_exception_is_marked_handled_and_an_unobserved_one_is_marked_observed()
    {
        var s = Wire();
        Assert.True(s.Dispatcher!(new Exception("ui")));                                   // e.Handled = true
        Assert.True(s.Unobserved!(new AggregateException(new Exception("task"))));          // e.SetObserved() is called
        Assert.Equal(2, _entries.Count);
    }

    [Fact]
    public void The_domain_handler_logs_only_exception_objects()
    {
        var s = Wire();
        s.Domain!("not an exception");
        s.Domain!(null);
        Assert.Empty(_entries);
        s.Domain!(new InvalidOperationException("worker"));
        Assert.Single(_entries);
        Assert.Contains("AppDomainUnhandledException", _entries[0]);
    }

    [Fact]
    public void The_uninstaller_flag_silences_log_and_toast_but_the_flags_stay_the_same()
    {
        _uninstaller = true;
        var s = Wire();
        Assert.True(s.Dispatcher!(new Exception("a")));
        s.Domain!(new Exception("b"));
        Assert.True(s.Unobserved!(new AggregateException(new Exception("c"))));
        Assert.Empty(_entries);
        Assert.Equal(0, _toasts);
    }
}
