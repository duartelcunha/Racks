using System.Diagnostics;
using Racks.Core.Abstractions;

namespace Racks.UseCases.HotCorner;

/// <summary>Where the pointer is and where the primary monitor's top-right corner is, both in physical pixels of the virtual screen.</summary>
public interface IHotCornerProbe
{
    (int X, int Y) CursorPosition { get; }
    /// <summary>Right edge and top edge of the PRIMARY monitor (the corner is never on another monitor).</summary>
    (int Right, int Top) PrimaryTopRight { get; }
}

/// <summary>
/// Hot-corner peek: poll the pointer every 100 ms. When it enters a 16 px square at the top-right of the primary monitor
/// the racks are hidden; when it leaves (and is also outside a 64 px margin, so jitter does not flicker them) they are shown
/// again. A two-state machine keeps it from hiding or showing twice. Moved unchanged out of <c>MainWindow</c>.
///
/// Threading: <see cref="Start"/>, <see cref="Stop"/> and the ticks run on the UI thread (the app's scheduler is a
/// <c>DispatcherTimer</c>). A tick that throws is logged and the watch carries on.
/// </summary>
public sealed class HotCornerService : IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    internal const int ZoneSize = 16;
    internal const int LeaveMargin = 64;

    private readonly IRepeatingScheduler _scheduler;
    private readonly IHotCornerProbe _probe;
    private readonly Action _hideRacks;
    private readonly Action _showRacks;
    private IDisposable? _timer;
    private bool _hidden; // whether the racks are currently hidden by the hot corner

    public HotCornerService(IRepeatingScheduler scheduler, IHotCornerProbe probe, Action hideRacks, Action showRacks)
    {
        _scheduler = scheduler;
        _probe = probe;
        _hideRacks = hideRacks;
        _showRacks = showRacks;
    }

    public bool IsWatching => _timer != null;

    /// <summary>True while the racks are hidden because the pointer is in (or just left) the corner.</summary>
    public bool RacksHidden => _hidden;

    /// <summary>Starts polling. Calling it while already watching does nothing (the startup path calls it twice).</summary>
    public void Start()
    {
        if (_timer != null) return;
        _timer = _scheduler.Every(PollInterval, Tick);
    }

    /// <summary>Stops polling. If the racks are hidden by the corner they are shown again (exceptions from that propagate, as before).</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (_hidden)
        {
            _hidden = false;
            _showRacks();
        }
    }

    public void Dispose() => Stop();

    internal void Tick()
    {
        try
        {
            var (x, y) = _probe.CursorPosition;
            var (right, top) = _probe.PrimaryTopRight;
            // 16x16 pixel hot zone at the top-right corner.
            bool inCorner = x >= right - ZoneSize && x <= right
                          && y >= top && y <= top + ZoneSize;
            if (inCorner && !_hidden)
            {
                _hidden = true;
                _hideRacks();
            }
            else if (!inCorner && _hidden)
            {
                // 64-pixel "leave" margin to avoid flicker on jitter.
                bool nearCorner = x >= right - LeaveMargin && y <= top + LeaveMargin;
                if (!nearCorner)
                {
                    _hidden = false;
                    _showRacks();
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"HotCorner tick failed: {ex.Message}"); }
    }
}
