using System.Diagnostics;

namespace Racks.UseCases.Crash;

/// <summary>
/// The last line of defense. Racks is a background tray app meant to run for the whole session: one bad rename, a bad saved
/// regex or a stray null reference in a click handler must not take every open rack down with it. This logs what happened
/// and keeps going instead of vanishing with no explanation. Moved unchanged out of <c>App</c>.
///
/// <list type="bullet">
/// <item>UI-thread exceptions are always marked handled: the app keeps running.</item>
/// <item>Non-UI-thread exceptions are fatal on .NET; this cannot stop that, only leave a record.</item>
/// <item>Unobserved task exceptions are marked observed.</item>
/// <item>Each is written to crash.log (the entry format is pinned by <c>CrashLogTests</c>: a timestamped source line, the
/// exception, a blank line) and, once per session, a toast says something went wrong and where the details are.</item>
/// <item>Nothing is written while running as the uninstaller (it must not recreate %AppData%\Racks after the uninstaller
/// removed it), and no toast is shown.</item>
/// </list>
/// </summary>
public sealed class CrashReportingService
{
    public const string DispatcherSource = "DispatcherUnhandledException";
    public const string AppDomainSource = "AppDomainUnhandledException";
    public const string UnobservedTaskSource = "UnobservedTaskException";

    private readonly Func<string> _logDirectory;
    private readonly Action<string, string> _append;
    private readonly Action _showToast;
    private readonly Func<bool> _runningForUninstaller;
    private readonly Func<DateTime> _now;
    private readonly Action<string> _trace;
    private int _toldUser;

    /// <param name="logDirectory">Folder of crash.log (%AppData%\Racks in the app).</param>
    /// <param name="append">Writes an entry to a folder (<c>CrashLog.Append</c>, which never throws).</param>
    /// <param name="showToast">Shows the "Racks hit a problem" toast; failures are swallowed here.</param>
    public CrashReportingService(
        Func<string> logDirectory,
        Action<string, string> append,
        Action showToast,
        Func<bool> runningForUninstaller,
        Func<DateTime>? now = null,
        Action<string>? trace = null)
    {
        _logDirectory = logDirectory;
        _append = append;
        _showToast = showToast;
        _runningForUninstaller = runningForUninstaller;
        _now = now ?? (() => DateTime.Now);
        _trace = trace ?? (m => Debug.WriteLine(m));
    }

    /// <summary>Logs a UI-thread exception. Always true: mark it handled.</summary>
    public bool HandleDispatcherException(Exception ex)
    {
        Log(ex, DispatcherSource);
        return true;
    }

    /// <summary>Logs a non-UI-thread exception (only exceptions are logged; other thrown objects are ignored).</summary>
    public void HandleDomainException(object? exceptionObject)
    {
        if (exceptionObject is Exception ex) Log(ex, AppDomainSource);
    }

    /// <summary>Logs an unobserved task exception. Always true: mark it observed.</summary>
    public bool HandleUnobservedTaskException(Exception ex)
    {
        Log(ex, UnobservedTaskSource);
        return true;
    }

    private void Log(Exception ex, string source)
    {
        if (_runningForUninstaller())
        {
            _trace($"{source}: {ex}");
            return;
        }
        _append(_logDirectory(),
            $"[{_now():yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        _trace($"{source}: {ex}");
        TellUserOnce();
    }

    // The exception is swallowed so Racks keeps running, which also meant the user never knew anything went wrong. Say so
    // once per session, with where the details are.
    private void TellUserOnce()
    {
        if (Interlocked.Exchange(ref _toldUser, 1) != 0) return;
        try { _showToast(); }
        catch { /* best-effort */ }
    }
}
