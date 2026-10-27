namespace Racks.UseCases.Display;

/// <summary>
/// Turns the burst of display/settings window messages into one reposition pass of the racks. Moved unchanged out of
/// <c>MainWindow.WndProc</c>, quirks included:
/// <list type="bullet">
/// <item><c>WM_DISPLAYCHANGE</c> (0x007E) starts a pass and arms the "reseted" flag; messages while racks are still
/// loading (<c>isInitializingInstances</c>) are ignored, and then the flag stays as it was.</item>
/// <item>The second message of the pair only counts while the flag is armed, and disarms it. The code has always tested
/// 0x001C and called it WM_WININICHANGE; that value is actually <c>WM_ACTIVATEAPP</c> (WM_WININICHANGE is 0x001A). Kept as it
/// is: changing it would change when racks are repositioned.</item>
/// <item>A pass already waiting to run swallows later requests (the debounce, <c>_dummyWindowPending</c> before). The pass
/// is posted to the UI thread; the flag is cleared after it ran, even when it threw.</item>
/// </list>
/// A genuine display change used to spawn a throwaway window per message, which fed a message storm; that stays removed.
/// </summary>
public sealed class DisplayChangeDebouncer
{
    public const int WmDisplayChange = 0x007E;
    public const int WmSecondChangeMessage = 0x001C;

    private readonly Func<bool> _isInitializing;
    private readonly Action<Action> _postToUi;
    private readonly Action _reposition;
    private bool _reseted;
    private bool _passPending;

    /// <param name="isInitializing">True while the racks are being loaded.</param>
    /// <param name="postToUi">Queues an action on the UI thread (<c>Dispatcher.InvokeAsync</c> in the app).</param>
    /// <param name="reposition">Repositions every rack against the new working area.</param>
    public DisplayChangeDebouncer(Func<bool> isInitializing, Action<Action> postToUi, Action reposition)
    {
        _isInitializing = isInitializing;
        _postToUi = postToUi;
        _reposition = reposition;
    }

    /// <summary>Feeds one window message in; messages that are not ours are ignored.</summary>
    public void OnMessage(int msg)
    {
        if (msg == WmDisplayChange)
        {
            if (!_isInitializing())
            {
                RequestPass();
                _reseted = true;
            }
        }
        if (msg == WmSecondChangeMessage && _reseted)
        {
            if (!_isInitializing())
            {
                _reseted = false;
                RequestPass();
            }
        }
    }

    private void RequestPass()
    {
        if (_passPending) return;
        _passPending = true;
        _postToUi(() =>
        {
            try { _reposition(); }
            finally { _passPending = false; }
        });
    }
}
