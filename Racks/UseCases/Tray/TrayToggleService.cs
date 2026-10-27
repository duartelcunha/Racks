using Racks.Infrastructure.Registry;
using Racks.Infrastructure.Shell;
using Racks.UseCases.HotCorner;

namespace Racks.UseCases.Tray;

/// <summary>The racks and their windows, as the tray toggles and the hot corner need them (the app's adapter wraps <c>InstanceController</c>).</summary>
public interface IRackWindowControl
{
    /// <summary>The racks, in order.</summary>
    IReadOnlyList<Instance> Racks { get; }
    /// <summary>Applies each rack's locked state to its window; one window failing does not stop the others (logged).</summary>
    void ApplyLockedStateToWindows();
}

/// <summary>
/// What each switch in the tray menu does once it has flipped: save the setting, then apply it. The switches themselves
/// (the <c>ToggleSwitch</c> controls and their events) stay in <c>MainWindow</c>. Moved unchanged out of the handlers.
///
/// Characterized behavior, kept on purpose:
/// <list type="bullet">
/// <item>The constructor of <c>MainWindow</c> sets <c>IsChecked</c> on the switches from the saved values, which fires their
/// events, so these methods run once at startup and write the value they just read back to the registry.</item>
/// <item>The <c>WM_RACKS_TOGGLE_*</c> messages set <c>IsChecked</c> (event, handler runs) AND call the handler by hand, so each
/// method can run twice for one message. Every method is idempotent for that reason: the second call writes the same
/// value and applies the same state (the hot corner starts only once; lock-all and hide-icons re-apply).</item>
/// </list>
/// </summary>
public sealed class TrayToggleService
{
    private readonly AppSettings _settings;
    private readonly ShellOperationGateway _shell;
    private readonly HotCornerService _hotCorner;
    private readonly IRackWindowControl _racks;
    private readonly Action<bool> _applyIcePhysics;
    private readonly Func<string?> _exePath;

    /// <param name="applyIcePhysics">Turns the live physics on or off (<c>RackPhysics.Enabled</c> in the app).</param>
    /// <param name="exePath">Full path of the running executable, asked only when "start on login" is turned on.</param>
    public TrayToggleService(
        AppSettings settings,
        ShellOperationGateway shell,
        HotCornerService hotCorner,
        IRackWindowControl racks,
        Action<bool> applyIcePhysics,
        Func<string?> exePath)
    {
        _settings = settings;
        _shell = shell;
        _hotCorner = hotCorner;
        _racks = racks;
        _applyIcePhysics = applyIcePhysics;
        _exePath = exePath;
    }

    /// <summary>
    /// What the lock-all switch should show: on only when there is at least one rack and every rack is locked
    /// (racks can be locked one by one from their own menu).
    /// </summary>
    public bool AllRacksLocked() => _racks.Racks.Count > 0 && _racks.Racks.All(i => i.IsLocked);

    /// <summary>
    /// Flips <c>IsLocked</c> on every rack at once. Persists per rack (the setter writes through) AND tells each running rack
    /// window to apply the new chrome: without it the lock would only "stick" after a relaunch.
    /// </summary>
    public void SetLockAll(bool locked)
    {
        foreach (var inst in _racks.Racks)
        {
            inst.IsLocked = locked;
        }
        _racks.ApplyLockedStateToWindows();
    }

    /// <summary>Global on/off for the ice-rink physics. Persists and applies live (no restart).</summary>
    public void SetIcePhysics(bool on)
    {
        _settings.TrySetIcePhysics(on);
        _applyIcePhysics(on);
    }

    /// <summary>Hides or shows the Windows desktop icons (racks stay visible) and remembers the choice.</summary>
    public void SetHideDesktopIcons(bool hide)
    {
        _settings.TrySetHideDesktopIcons(hide);
        _shell.SetDesktopIconsHidden(hide);
    }

    /// <summary>
    /// Adds or removes the Windows Run entry (off removes it), then saves the switch. The
    /// executable path is only looked up when turning on.
    /// </summary>
    public void SetStartOnLogin(bool on)
    {
        _settings.TrySetStartOnLogin(on, on ? _exePath() : null);
    }

    /// <summary>Saves the hot-corner switch and starts or stops the watch.</summary>
    public void SetHotCorner(bool on)
    {
        _settings.TrySetHotCornerHide(on);
        if (on) _hotCorner.Start(); else _hotCorner.Stop();
    }
}
