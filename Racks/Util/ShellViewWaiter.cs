using Racks.Core.Abstractions;

namespace Racks.Util;

/// <summary>
/// Waits for the desktop window (SHELLDLL_DefView) to exist without blocking anything. Explorer has
/// no DefView for a moment while it restarts; the rack used to sleep on the UI thread for up to 10
/// seconds waiting for it, which froze every rack and the tray. This polls on a scheduler instead.
/// </summary>
public sealed class ShellViewWaiter : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    public const int MaxAttempts = 30;

    private readonly Func<IntPtr> _find;
    private readonly IDelayScheduler _scheduler;
    private readonly Action<IntPtr> _found;
    private readonly Action _gaveUp;
    private IDisposable? _pending;
    private bool _running;
    private bool _disposed;

    public ShellViewWaiter(Func<IntPtr> find, IDelayScheduler scheduler, Action<IntPtr> found, Action gaveUp)
    {
        _find = find;
        _scheduler = scheduler;
        _found = found;
        _gaveUp = gaveUp;
    }

    /// <summary>The desktop window (SHELLDLL_DefView), or zero when Explorer has none right now.</summary>
    public static IntPtr FindDesktopView()
    {
        IntPtr result = IntPtr.Zero;
        Interop.EnumWindows((top, _) =>
        {
            IntPtr candidate = Interop.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null!);
            if (candidate == IntPtr.Zero) return true;
            result = candidate;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Starts polling. Does nothing if a wait is already in progress.</summary>
    public void Start()
    {
        if (_running || _disposed) return;
        _running = true;
        Attempt(1);
    }

    private void Attempt(int number)
    {
        if (_disposed) return;
        IntPtr handle = _find();
        if (handle != IntPtr.Zero)
        {
            _running = false;
            _found(handle);
            return;
        }
        if (number >= MaxAttempts)
        {
            _running = false;
            _gaveUp();
            return;
        }
        _pending = _scheduler.Schedule(Interval, () => Attempt(number + 1));
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.Dispose();
        _pending = null;
    }
}
