using System.Globalization;
using Microsoft.Win32;
using Racks.Infrastructure.Logging;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// Typed access to the app-wide switches stored as loose values under <c>HKCU\SOFTWARE\Racks</c> (not the
/// per-rack <c>Instances\*</c> keys; that schema is P6). Persisted format is unchanged from earlier builds, so an
/// old install keeps working and an old build reads what this one writes:
/// <list type="bullet">
/// <item>A switch is a REG_SZ with the text <c>True</c> or <c>False</c> (what <c>RegistryKey.SetValue(name, bool)</c>
/// has always stored). Reads accept any case and surrounding spaces, as <c>bool.TryParse</c> does.</item>
/// <item><c>MainHwnd</c> is a REG_SZ with the decimal handle (an Int64 passed to <c>SetValue</c> is stored as text, not a
/// QWORD); <c>tools/ComprehensiveE2ERunner.ps1</c> reads it by name.</item>
/// <item>The Windows autorun entry is a REG_SZ with the executable path under the Run key, named like the product;
/// turning it off also removes the legacy <c>DeskFrame</c> entry.</item>
/// </list>
/// A getter never throws. A value that is not a boolean text (a DWORD, a binary, garbage) or a registry that cannot
/// be read gives the documented default and one audit record per name (<see cref="RegistryAudit"/>). A missing value
/// is normal (fresh install): the default applies, nothing is audited. A setter never throws either: a failed write
/// is audited and reported by the <c>bool</c> it returns, and the UI carries on as it always did.
/// Defaults: every switch is off except <see cref="IcePhysics"/> (on).
/// </summary>
public sealed class AppSettings
{
    public const string StartOnLoginName = "startOnLogin";
    public const string DoubleClickToHideName = "DoubleClickToHide";
    public const string IcePhysicsName = "IcePhysics";
    public const string HideDesktopIconsName = "HideDesktopIcons";
    public const string HotCornerHideName = "HotCornerHide";
    public const string AutoUpdateName = "AutoUpdate";
    public const string BlurBackgroundName = "blurBackground";
    public const string MainHwndName = "MainHwnd";

    /// <summary>The app-wide instance over the real registry; follows <c>InstanceController.appName</c>.</summary>
    public static AppSettings Default { get; } = new(
        HkcuRegistryRoot.ForApp(),
        HkcuRegistryRoot.ForAutoRun(),
        () => InstanceController.appName,
        AuditLog.Forwarder);

    private readonly IRegistryRoot _root;
    private readonly IRegistryRoot _autoRun;
    private readonly Func<string> _autoRunName;
    private readonly RegistryAudit _audit;

    /// <param name="root">Where the switches live.</param>
    /// <param name="autoRun">The Run key (a throwaway one in tests).</param>
    /// <param name="autoRunName">Name of this app's Run value (the product name).</param>
    /// <param name="audit">Where audit records go; null = <see cref="AuditLog.Forwarder"/>.</param>
    public AppSettings(IRegistryRoot root, IRegistryRoot autoRun, Func<string> autoRunName, IAuditSink? audit = null)
    {
        _root = root;
        _autoRun = autoRun;
        _autoRunName = autoRunName;
        _audit = new RegistryAudit(audit ?? AuditLog.Forwarder);
    }

    // ---- switches ------------------------------------------------------------------------------

    /// <summary>"Start on login" as last saved. Default off.</summary>
    public bool StartOnLogin => ReadBool(StartOnLoginName, false) ?? false;

    /// <summary>Double-click on the desktop hides the racks. Default off.</summary>
    public bool DoubleClickToHide => ReadBool(DoubleClickToHideName, false) ?? false;

    /// <summary>Ice-rink physics. Default ON: only a saved <c>False</c> turns it off.</summary>
    public bool IcePhysics => ReadBool(IcePhysicsName, true) ?? true;

    /// <summary>Hot-corner peek. Default off.</summary>
    public bool HotCornerHide => ReadBool(HotCornerHideName, false) ?? false;

    /// <summary>Check for a new release at startup. Default off.</summary>
    public bool AutoUpdate => ReadBool(AutoUpdateName, false) ?? false;

    /// <summary>
    /// Hide the Windows desktop icons. Null when it was never saved (the caller then leaves its toggle alone, as
    /// before); an unusable saved value gives false.
    /// </summary>
    public bool? HideDesktopIcons => ReadBool(HideDesktopIconsName, false);

    /// <summary>
    /// Blurred rack background (read-only: nothing in the app writes it any more, an old install may still have it).
    /// Null when never saved; an unusable saved value gives false.
    /// </summary>
    public bool? BlurBackground => ReadBool(BlurBackgroundName, false);

    public bool TrySetDoubleClickToHide(bool value) => WriteBool(DoubleClickToHideName, value);
    public bool TrySetIcePhysics(bool value) => WriteBool(IcePhysicsName, value);
    public bool TrySetHotCornerHide(bool value) => WriteBool(HotCornerHideName, value);
    public bool TrySetHideDesktopIcons(bool value) => WriteBool(HideDesktopIconsName, value);
    public bool TrySetAutoUpdate(bool value) => WriteBool(AutoUpdateName, value);

    // ---- start on login ------------------------------------------------------------------------

    /// <summary>
    /// Turns "start on login" on or off: adds or removes the Windows Run entry, then saves the switch (the switch is
    /// saved even when the Run entry failed, as before). Off removes the product entry and the legacy
    /// <c>DeskFrame</c> one. Returns false when anything failed (each failure is audited).
    /// </summary>
    /// <param name="exePath">Full path of the running executable; only used (and required) when turning on.</param>
    public bool TrySetStartOnLogin(bool enabled, string? exePath)
    {
        bool ok = enabled ? TryAddAutoRun(exePath) : TryRemoveAutoRun();
        ok &= WriteBool(StartOnLoginName, enabled);
        return ok;
    }

    private bool TryAddAutoRun(string? exePath)
    {
        string name = _autoRunName();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            _audit.WriteFailed(_autoRun, name, "", new ArgumentException("no executable path to register"));
            return false;
        }
        return TryWrite(_autoRun, name, exePath, RegistryValueKind.String);
    }

    private bool TryRemoveAutoRun()
    {
        bool ok = TryDelete(_autoRun, _autoRunName());
        ok &= TryDelete(_autoRun, InstanceController.LegacyAppName);
        return ok;
    }

    // ---- MainHwnd ------------------------------------------------------------------------------

    /// <summary>
    /// Publishes the main window handle for the E2E tooling as REG_SZ holding the decimal number. That is what
    /// <c>SetValue("MainHwnd", (long)hwnd)</c> has always stored (<c>RegistryKey.SetValue</c> keeps an Int64 as text,
    /// not a QWORD); <c>[IntPtr][long]$val</c> in the runner parses it.
    /// </summary>
    public bool TryPublishMainHwnd(long hwnd) =>
        TryWrite(_root, MainHwndName, hwnd.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);

    /// <summary>The published handle, or null when missing or not a number (a REG_QWORD is accepted too).</summary>
    public long? ReadMainHwnd()
    {
        var value = Read(MainHwndName, "none");
        if (value is null) return null;
        if (value.Value.Data is long qword && value.Value.Kind == RegistryValueKind.QWord) return qword;
        if (IsText(value.Value.Kind) && value.Value.Data is string text
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }
        _audit.Fallback(_root, MainHwndName, RegistryAudit.Describe(value.Value), "not a number", "none");
        return null;
    }

    // ---- plumbing ------------------------------------------------------------------------------

    private static bool IsText(RegistryValueKind kind) => kind is RegistryValueKind.String or RegistryValueKind.ExpandString;

    private bool? ReadBool(string name, bool fallback)
    {
        string fallbackText = fallback ? "True" : "False";
        var value = Read(name, fallbackText);
        if (value is null) return null;
        if (IsText(value.Value.Kind) && value.Value.Data is string text && bool.TryParse(text, out bool parsed))
        {
            return parsed;
        }
        string reason = IsText(value.Value.Kind)
            ? "not True or False"
            : $"wrong value type ({value.Value.Kind}), expected a True/False text";
        _audit.Fallback(_root, name, RegistryAudit.Describe(value.Value), reason, fallbackText);
        return fallback;
    }

    // Null = missing, or unreadable (audited): either way the caller's own default applies, so a registry that
    // cannot be read behaves like an empty one, as it always did.
    private RegistryValue? Read(string name, string fallbackText)
    {
        try
        {
            return _root.Read(name);
        }
        catch (Exception ex)
        {
            _audit.Fallback(_root, name, "(unreadable)", $"read failed: {ex.GetType().Name}: {ex.Message}", fallbackText);
            return null;
        }
    }

    private bool WriteBool(string name, bool value) => TryWrite(_root, name, value ? "True" : "False", RegistryValueKind.String);

    private bool TryWrite(IRegistryRoot root, string name, object data, RegistryValueKind kind)
    {
        try
        {
            root.Write(name, data, kind);
            return true;
        }
        catch (Exception ex)
        {
            _audit.WriteFailed(root, name, Convert.ToString(data, CultureInfo.InvariantCulture) ?? "", ex);
            return false;
        }
    }

    private bool TryDelete(IRegistryRoot root, string name)
    {
        try
        {
            root.Delete(name);
            return true;
        }
        catch (Exception ex)
        {
            _audit.WriteFailed(root, name, "(delete)", ex);
            return false;
        }
    }
}
