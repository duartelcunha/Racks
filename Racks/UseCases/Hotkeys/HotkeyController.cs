using System.Diagnostics;

namespace Racks.UseCases.Hotkeys;

/// <summary>The Win32 hotkey calls (<c>RegisterHotKey</c> / <c>UnregisterHotKey</c>). Tests replace it.</summary>
public interface IHotkeyApi
{
    /// <summary>False (it does not throw) when another app already owns the combination.</summary>
    bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    bool Unregister(IntPtr hwnd, int id);
}

/// <summary>What each hotkey does. The app supplies them; the controller only decides which one a message means.</summary>
public sealed record HotkeyActions(Action OpenQuickFinder, Action NewRack, Action ToggleDevDiagnostics);

/// <summary>
/// The three global hotkeys of the hidden host window: Ctrl+Shift+Space (cross-rack quick finder), Ctrl+Shift+N (new virtual
/// rack) and Ctrl+Shift+D (dev diagnostics). Registers them, reports a conflict on the first two through a callback (the
/// app shows a toast), turns a <c>WM_HOTKEY</c> message into the matching action, and unregisters them on exit.
/// The ids and the key combinations are part of the app's behavior and must not change.
/// Moved out of <c>MainWindow</c>; the order, the per-hotkey try/catch and the log messages are unchanged.
/// </summary>
public sealed class HotkeyController
{
    public const int QuickFinderId = 0xB0CC;
    public const int NewRackId = 0xB0CD;
    public const int DevDiagnosticsId = 0xB0CE;

    internal const uint ModControl = 0x2;
    internal const uint ModShift = 0x4;
    internal const uint VkSpace = 0x20;
    internal const uint VkN = 0x4E;
    internal const uint VkD = 0x44;
    internal const int WmHotkey = 0x0312;

    private readonly IHotkeyApi _api;
    private readonly HotkeyActions _actions;
    private IntPtr _hwnd = IntPtr.Zero;

    public HotkeyController(IHotkeyApi api, HotkeyActions actions)
    {
        _api = api;
        _actions = actions;
    }

    /// <summary>
    /// Registers the three hotkeys on <paramref name="hwnd"/>. <paramref name="onConflict"/> is called with
    /// (quick finder ok, new rack ok) when either of those two could not be registered; the dev diagnostics hotkey is not reported.
    /// An exception from the API counts as a failed registration of that one hotkey. Returns the same two flags.
    /// </summary>
    public (bool QuickFinderOk, bool NewRackOk) RegisterAll(IntPtr hwnd, Action<bool, bool>? onConflict = null)
    {
        _hwnd = hwnd;
        // RegisterHotKey returns false (it does not throw) when another app already owns the combination, so check
        // the result and tell the user instead of failing silently.
        bool quickFinderOk = false, newRackOk = false;
        try { quickFinderOk = _api.Register(hwnd, QuickFinderId, ModControl | ModShift, VkSpace); }
        catch (Exception ex) { Debug.WriteLine($"RegisterHotKey QuickFinder failed: {ex.Message}"); }
        try { newRackOk = _api.Register(hwnd, NewRackId, ModControl | ModShift, VkN); }
        catch (Exception ex) { Debug.WriteLine($"RegisterHotKey NewRack failed: {ex.Message}"); }
        try { _api.Register(hwnd, DevDiagnosticsId, ModControl | ModShift, VkD); }
        catch (Exception ex) { Debug.WriteLine($"RegisterHotKey DevDiagnostics failed: {ex.Message}"); }
        if (!quickFinderOk || !newRackOk)
        {
            try { onConflict?.Invoke(quickFinderOk, newRackOk); }
            catch (Exception ex) { Debug.WriteLine($"Hotkey conflict toast failed: {ex.Message}"); }
        }
        return (quickFinderOk, newRackOk);
    }

    /// <summary>
    /// Handles a window message. True when it was one of our hotkeys (the caller marks the message handled).
    /// A failing quick finder or new-rack action is logged and swallowed; the dev diagnostics action is not wrapped.
    /// </summary>
    public bool TryDispatch(int msg, IntPtr wParam)
    {
        if (msg != WmHotkey) return false;
        int id = wParam.ToInt32();
        if (id == QuickFinderId)
        {
            try { _actions.OpenQuickFinder(); }
            catch (Exception ex) { Debug.WriteLine($"QuickFinder open failed: {ex.Message}"); }
            return true;
        }
        if (id == NewRackId)
        {
            try { _actions.NewRack(); }
            catch (Exception ex) { Debug.WriteLine($"New rack hotkey failed: {ex.Message}"); }
            return true;
        }
        if (id == DevDiagnosticsId)
        {
            _actions.ToggleDevDiagnostics();
            return true;
        }
        return false;
    }

    /// <summary>Unregisters the three hotkeys (only if <see cref="RegisterAll"/> ran with a window). Never throws.</summary>
    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;
        try { _api.Unregister(_hwnd, QuickFinderId); } catch { }
        try { _api.Unregister(_hwnd, NewRackId); } catch { }
        try { _api.Unregister(_hwnd, DevDiagnosticsId); } catch { }
    }
}
