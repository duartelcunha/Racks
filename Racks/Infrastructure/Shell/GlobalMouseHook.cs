using System.Windows.Automation;
using H.Hooks;
using Racks.UseCases.DoubleClickHide;
using static Racks.Util.Interop;

namespace Racks.Infrastructure.Shell;

/// <summary>The H.Hooks low-level mouse hook behind <see cref="DoubleClickHideService"/>; the only place that touches H.Hooks.</summary>
public sealed class GlobalMouseHook : IGlobalMouseHook
{
    private readonly LowLevelMouseHook _hook = new() { AddKeyboardKeys = true };

    public GlobalMouseHook()
    {
        _hook.DoubleClick += (_, e) =>
            DoubleClick?.Invoke(new MouseDoubleClick(e.Keys.ToString() == "MouseLeft", e.Position.X, e.Position.Y));
    }

    public event Action<MouseDoubleClick>? DoubleClick;

    public void Start() => _hook.Start();
    public void Stop() => _hook.Stop();
    public void Dispose() => _hook.Dispose();
}

/// <summary>The real desktop hit test: <c>WindowFromPoint</c>, the shell gateway's icon list and UI Automation.</summary>
public sealed class DesktopHitTest : IDesktopHitTest
{
    public IntPtr WindowAt(int x, int y) => WindowFromPoint(new POINT { X = x, Y = y });

    public IntPtr DesktopIconList() => ShellOperationGateway.Default.FindDesktopIconList();

    public bool IsIconAt(int x, int y)
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        return element.Current.ControlType == ControlType.ListItem;
    }
}
