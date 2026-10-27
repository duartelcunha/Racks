namespace Racks.UseCases.DoubleClickHide;

/// <summary>A double-click reported by the global mouse hook, in physical screen pixels.</summary>
public readonly record struct MouseDoubleClick(bool IsLeftButton, int X, int Y);

/// <summary>The global (low-level) mouse hook. The real one wraps H.Hooks; events arrive on the hook's own thread.</summary>
public interface IGlobalMouseHook : IDisposable
{
    event Action<MouseDoubleClick>? DoubleClick;
    void Start();
    void Stop();
}

/// <summary>What is under the pointer. The real one uses <c>WindowFromPoint</c> and UI Automation.</summary>
public interface IDesktopHitTest
{
    /// <summary>The window at the point (zero if none).</summary>
    IntPtr WindowAt(int x, int y);
    /// <summary>The desktop icon list window (zero if Explorer is not running).</summary>
    IntPtr DesktopIconList();
    /// <summary>True when the point is on an icon (a list item) of the desktop list.</summary>
    bool IsIconAt(int x, int y);
}

/// <summary>
/// "Double-click the empty desktop to hide or show the racks". Owns the hook's life (created when the switch goes on,
/// stopped when it goes off, stopped and disposed on exit) and the decision for each double-click, which is pure: left
/// button only, at most one per 0.3 s, and the window under the pointer must be the desktop icon list with no icon at
/// the point. The decision runs on the hook's thread; <c>toggleRacks</c> is expected to marshal to the UI thread.
/// Moved unchanged out of <c>MainWindow</c>.
/// </summary>
public sealed class DoubleClickHideService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(0.3);

    private readonly Func<IGlobalMouseHook> _createHook;
    private readonly IDesktopHitTest _hitTest;
    private readonly Action _toggleRacks;
    private readonly Func<DateTime> _clock;
    private IGlobalMouseHook? _hook;
    private bool _enabled;
    private DateTime _lastDoubleClickTime = DateTime.MinValue;

    public DoubleClickHideService(Func<IGlobalMouseHook> createHook, IDesktopHitTest hitTest, Action toggleRacks, Func<DateTime>? clock = null)
    {
        _createHook = createHook;
        _hitTest = hitTest;
        _toggleRacks = toggleRacks;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>
    /// The switch. Turning it on replaces any earlier hook with a new started one; turning it off only stops the hook
    /// (it is disposed on exit). Setting the value it already has does nothing.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled)
            {
                _hook?.Dispose();
                _hook = _createHook();
                _hook.DoubleClick += OnDoubleClick;
                _hook.Start();
            }
            else
            {
                _hook?.Stop();
            }
        }
    }

    internal void OnDoubleClick(MouseDoubleClick click)
    {
        if (!click.IsLeftButton) return;
        var now = _clock();
        if ((now - _lastDoubleClickTime) < Debounce) return;
        _lastDoubleClickTime = now;

        IntPtr underCursor = _hitTest.WindowAt(click.X, click.Y);
        IntPtr desktopList = _hitTest.DesktopIconList();

        if (underCursor == desktopList && !_hitTest.IsIconAt(click.X, click.Y))
        {
            _toggleRacks();
        }
    }

    /// <summary>Stops and disposes the hook (exit).</summary>
    public void Dispose()
    {
        if (_hook != null)
        {
            _hook.Stop();
            _hook.Dispose();
        }
    }
}
