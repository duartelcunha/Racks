using System.IO;
using Racks.UseCases.Crash;
using Racks.Util;

namespace Racks.Tests.Startup;

public sealed class CrashReportingServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "racks-crashsvc-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _trace = new();
    private int _toasts;
    private bool _uninstaller;
    private bool _toastThrows;
    private DateTime _now = new(2026, 10, 27, 9, 8, 7);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private CrashReportingService Create() => new(
        () => _dir,
        (dir, entry) => CrashLog.Append(dir, entry),
        () => { _toasts++; if (_toastThrows) throw new InvalidOperationException("no toast host"); },
        () => _uninstaller,
        () => _now,
        _trace.Add);

    private string LogText => File.ReadAllText(Path.Combine(_dir, CrashLog.FileName));

    [Fact]
    public void A_ui_thread_exception_is_always_handled_and_logged_with_its_source()
    {
        var svc = Create();

        bool handled = svc.HandleDispatcherException(new InvalidOperationException("boom"));

        Assert.True(handled);
        string expected = "[2026-10-27 09:08:07] DispatcherUnhandledException" + Environment.NewLine
            + new InvalidOperationException("boom") + Environment.NewLine + Environment.NewLine;
        // The exception text carries a stack only when thrown; compare the first two lines and the blank-line ending.
        string log = LogText;
        Assert.StartsWith("[2026-10-27 09:08:07] DispatcherUnhandledException" + Environment.NewLine + "System.InvalidOperationException: boom", log);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, log);
        Assert.Equal(expected.Length, log.Length);
    }

    [Fact]
    public void A_non_ui_exception_is_logged_but_other_thrown_objects_are_ignored()
    {
        var svc = Create();

        svc.HandleDomainException("just a string");
        Assert.False(File.Exists(Path.Combine(_dir, CrashLog.FileName)));
        Assert.Equal(0, _toasts);

        svc.HandleDomainException(new IOException("disk"));
        Assert.Contains("] AppDomainUnhandledException" + Environment.NewLine + "System.IO.IOException: disk", LogText);
    }

    [Fact]
    public void An_unobserved_task_exception_is_observed_and_logged()
    {
        var svc = Create();

        bool observed = svc.HandleUnobservedTaskException(new AggregateException(new TimeoutException("slow")));

        Assert.True(observed);
        Assert.Contains("] UnobservedTaskException" + Environment.NewLine + "System.AggregateException", LogText);
        Assert.Contains("TimeoutException: slow", LogText);
    }

    [Fact]
    public void The_user_is_told_once_per_session_but_every_crash_is_logged()
    {
        var svc = Create();

        svc.HandleDispatcherException(new Exception("one"));
        svc.HandleDomainException(new Exception("two"));
        svc.HandleUnobservedTaskException(new AggregateException(new Exception("three")));

        Assert.Equal(1, _toasts);
        Assert.Equal(3, LogText.Split("] ").Length - 1);
    }

    [Fact]
    public void A_failing_toast_never_stops_the_logging_or_the_handled_flag()
    {
        _toastThrows = true;
        var svc = Create();

        Assert.True(svc.HandleDispatcherException(new Exception("x")));
        Assert.True(svc.HandleDispatcherException(new Exception("y")));

        Assert.Equal(1, _toasts);   // tried once, swallowed, not retried
        Assert.Contains("x", LogText);
    }

    [Fact]
    public void While_running_as_the_uninstaller_nothing_is_written_and_nobody_is_told()
    {
        _uninstaller = true;
        var svc = Create();

        Assert.True(svc.HandleDispatcherException(new Exception("late")));
        svc.HandleDomainException(new Exception("late too"));
        Assert.True(svc.HandleUnobservedTaskException(new AggregateException(new Exception("and this"))));

        Assert.False(Directory.Exists(_dir));    // %AppData%\Racks must not be recreated after the uninstaller removed it
        Assert.Equal(0, _toasts);
        Assert.Equal(3, _trace.Count);
    }

    [Fact]
    public void The_uninstaller_flag_is_read_at_crash_time_not_at_construction()
    {
        var svc = Create();
        _uninstaller = true;
        svc.HandleDispatcherException(new Exception("x"));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Every_handled_crash_is_also_traced()
    {
        var svc = Create();
        svc.HandleDispatcherException(new Exception("x"));
        Assert.Single(_trace);
        Assert.StartsWith("DispatcherUnhandledException: ", _trace[0]);
    }

    [Fact]
    public void The_source_names_are_the_ones_written_to_existing_crash_logs()
    {
        Assert.Equal("DispatcherUnhandledException", CrashReportingService.DispatcherSource);
        Assert.Equal("AppDomainUnhandledException", CrashReportingService.AppDomainSource);
        Assert.Equal("UnobservedTaskException", CrashReportingService.UnobservedTaskSource);
    }
}
