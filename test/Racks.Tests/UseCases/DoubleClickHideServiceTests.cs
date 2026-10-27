using Racks.UseCases.DoubleClickHide;

namespace Racks.Tests.UseCases;

// Double-click on the empty desktop hides or shows the racks. The hook's life and the per-click decision, with a fake hook,
// a fake hit test and a fake clock: nothing here installs a real mouse hook or touches the desktop.
public class DoubleClickHideServiceTests
{
    private sealed class FakeHook : IGlobalMouseHook
    {
        public int Started, Stopped, Disposed;
        public event Action<MouseDoubleClick>? DoubleClick;
        public void Start() => Started++;
        public void Stop() => Stopped++;
        public void Dispose() => Disposed++;
        public void Raise(MouseDoubleClick c) => DoubleClick?.Invoke(c);
        public bool HasSubscribers => DoubleClick != null;
    }

    private sealed class FakeHitTest : IDesktopHitTest
    {
        public IntPtr Window = new(100), List = new(100);
        public bool Icon;
        public List<string> Calls { get; } = new();
        public IntPtr WindowAt(int x, int y) { Calls.Add("window"); return Window; }
        public IntPtr DesktopIconList() { Calls.Add("list"); return List; }
        public bool IsIconAt(int x, int y) { Calls.Add("icon"); return Icon; }
    }

    private sealed class Rig
    {
        public List<FakeHook> Hooks { get; } = new();
        public FakeHitTest Hit { get; } = new();
        public DateTime Now = new(2026, 1, 1, 12, 0, 0);
        public int Toggles;
        public DoubleClickHideService Service { get; }

        public Rig() => Service = new DoubleClickHideService(
            () => { var h = new FakeHook(); Hooks.Add(h); return h; }, Hit, () => Toggles++, () => Now);

        public void Click(bool left = true) => Hooks[^1].Raise(new MouseDoubleClick(left, 10, 20));
    }

    // ---- the hook's life -------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_created_while_the_switch_is_off()
    {
        var rig = new Rig();
        rig.Service.Enabled = false;
        Assert.Empty(rig.Hooks);
        rig.Service.Dispose();   // exit with no hook is fine
    }

    [Fact]
    public void Turning_it_on_creates_and_starts_one_hook_and_setting_it_again_does_nothing()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Service.Enabled = true;
        var hook = Assert.Single(rig.Hooks);
        Assert.Equal(1, hook.Started);
        Assert.True(hook.HasSubscribers);
    }

    [Fact]
    public void Turning_it_off_only_stops_the_hook_and_turning_it_on_again_replaces_it()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        var first = rig.Hooks[0];

        rig.Service.Enabled = false;
        Assert.Equal(1, first.Stopped);
        Assert.Equal(0, first.Disposed);

        rig.Service.Enabled = true;
        Assert.Equal(1, first.Disposed);
        Assert.Equal(2, rig.Hooks.Count);
        Assert.Equal(1, rig.Hooks[1].Started);
    }

    [Fact]
    public void Exit_stops_then_disposes_the_current_hook()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Service.Dispose();
        Assert.Equal(1, rig.Hooks[0].Stopped);
        Assert.Equal(1, rig.Hooks[0].Disposed);
    }

    // ---- the decision ----------------------------------------------------------------------------

    [Fact]
    public void A_left_double_click_on_the_empty_desktop_toggles_the_racks()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Click();
        Assert.Equal(1, rig.Toggles);
    }

    [Fact]
    public void Other_buttons_are_ignored_and_do_not_start_the_debounce()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Click(left: false);
        Assert.Equal(0, rig.Toggles);
        Assert.Empty(rig.Hit.Calls);
        rig.Click();                       // same instant: the right click did not use up the 0.3 s window
        Assert.Equal(1, rig.Toggles);
    }

    [Fact]
    public void A_second_double_click_within_300_ms_is_ignored_and_one_after_it_counts()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Click();
        rig.Now += TimeSpan.FromMilliseconds(299);
        rig.Click();
        Assert.Equal(1, rig.Toggles);
        rig.Now += TimeSpan.FromMilliseconds(1);   // 300 ms after the first
        rig.Click();
        Assert.Equal(2, rig.Toggles);
    }

    [Fact]
    public void The_debounce_clock_starts_even_when_the_click_did_not_toggle()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Hit.Window = new IntPtr(7);    // some other window
        rig.Click();
        rig.Hit.Window = new IntPtr(100);  // now the desktop, 100 ms later
        rig.Now += TimeSpan.FromMilliseconds(100);
        rig.Click();
        Assert.Equal(0, rig.Toggles);
    }

    [Fact]
    public void A_click_on_another_window_does_not_toggle_and_never_asks_about_icons()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Hit.Window = new IntPtr(7);
        rig.Click();
        Assert.Equal(0, rig.Toggles);
        Assert.Equal(new[] { "window", "list" }, rig.Hit.Calls);
    }

    [Fact]
    public void A_click_on_a_desktop_icon_does_not_toggle()
    {
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Hit.Icon = true;
        rig.Click();
        Assert.Equal(0, rig.Toggles);
        Assert.Equal(new[] { "window", "list", "icon" }, rig.Hit.Calls);
    }

    [Fact]
    public void Without_explorer_both_handles_are_zero_and_that_still_counts_as_the_desktop()
    {
        // Characterizes the original: zero == zero passes the window test, only an icon at the point stops it.
        var rig = new Rig();
        rig.Service.Enabled = true;
        rig.Hit.Window = IntPtr.Zero;
        rig.Hit.List = IntPtr.Zero;
        rig.Click();
        Assert.Equal(1, rig.Toggles);
    }
}
