using Racks.Tests.Fakes;
using Racks.UseCases.HotCorner;

namespace Racks.Tests.UseCases;

// The hot corner polls the pointer every 100 ms: entering a 16 px square at the top-right of the PRIMARY monitor hides the
// racks, leaving it (and a 64 px margin) shows them. These pin the numbers and the state machine moved out of MainWindow.
public class HotCornerServiceTests
{
    private sealed class Probe : IHotCornerProbe
    {
        public (int X, int Y) Cursor { get; set; } = (500, 500);
        public (int Right, int Top) Primary { get; set; } = (1920, 0);
        public Exception? Throw { get; set; }
        public (int X, int Y) CursorPosition => Throw is { } t ? throw t : Cursor;
        public (int Right, int Top) PrimaryTopRight => Primary;
    }

    private sealed class Rig
    {
        public ManualRepeatingScheduler Scheduler { get; } = new();
        public Probe Probe { get; } = new();
        public int Hides, Shows;
        public Exception? HideThrows;
        public HotCornerService Service { get; }

        public Rig() => Service = new HotCornerService(Scheduler, Probe, () => { Hides++; if (HideThrows != null) throw HideThrows; }, () => Shows++);

        public void MoveTo(int x, int y)
        {
            Probe.Cursor = (x, y);
            Scheduler.Advance(TimeSpan.FromMilliseconds(100));
        }
    }

    [Fact]
    public void Nothing_runs_until_started_and_the_poll_interval_is_100_ms()
    {
        var rig = new Rig();
        rig.Probe.Cursor = (1920, 0);
        rig.Scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, rig.Hides);
        Assert.False(rig.Service.IsWatching);

        rig.Service.Start();
        rig.Scheduler.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(0, rig.Hides);
        rig.Scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Hides);
    }

    [Fact]
    public void Starting_twice_keeps_one_timer_as_the_startup_path_does()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.Service.Start();
        Assert.Equal(1, rig.Scheduler.ActiveCount);
        Assert.Equal(1, rig.Scheduler.StartedCount);
    }

    [Theory]
    [InlineData(1920, 0, true)]      // the corner pixel itself
    [InlineData(1904, 16, true)]     // far edge of the 16 px square (inclusive)
    [InlineData(1903, 0, false)]     // one pixel too far left
    [InlineData(1920, 17, false)]    // one pixel too low
    [InlineData(1921, 0, false)]     // past the right edge (a monitor to the right)
    [InlineData(1920, -1, false)]    // above the top edge
    public void The_hot_zone_is_a_16_pixel_square_including_its_edges(int x, int y, bool inside)
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(x, y);
        Assert.Equal(inside ? 1 : 0, rig.Hides);
    }

    [Fact]
    public void Staying_in_the_corner_hides_once_not_on_every_tick()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(1915, 3);
        rig.MoveTo(1916, 4);
        rig.MoveTo(1915, 3);
        Assert.Equal(1, rig.Hides);
        Assert.True(rig.Service.RacksHidden);
        Assert.Equal(0, rig.Shows);
    }

    [Theory]
    [InlineData(1900, 40, true)]    // outside the 16 px zone, inside the 64 px margin
    [InlineData(1856, 64, true)]    // the margin's exact edges (x >= Right-64 and y <= Top+64)
    [InlineData(1855, 10, false)]   // one pixel too far left
    [InlineData(1900, 65, false)]   // one pixel too low
    [InlineData(1700, 10, false)]   // at the top edge but far to the left
    public void The_leave_margin_keeps_the_racks_hidden_until_both_x_and_y_are_outside_it(int x, int y, bool stillNear)
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(1920, 0);
        rig.MoveTo(x, y);
        Assert.Equal(stillNear ? 0 : 1, rig.Shows);
        Assert.Equal(stillNear, rig.Service.RacksHidden);
    }

    [Fact]
    public void Leaving_shows_the_racks_once_and_a_second_visit_hides_again()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(1920, 0);
        rig.MoveTo(1855, 10);
        Assert.Equal(1, rig.Shows);
        Assert.False(rig.Service.RacksHidden);
        rig.MoveTo(1000, 500);
        Assert.Equal(1, rig.Shows);      // no repeated Show
        rig.MoveTo(1918, 2);
        Assert.Equal(2, rig.Hides);
    }

    [Fact]
    public void The_corner_is_the_primary_monitors_even_when_it_sits_right_of_another_one()
    {
        // Primary monitor to the right of a second one: its right edge is 3840 in virtual-screen pixels.
        var rig = new Rig();
        rig.Probe.Primary = (3840, 0);
        rig.Service.Start();

        rig.MoveTo(1920, 0);             // the top-right of the OTHER monitor is not the hot corner
        Assert.Equal(0, rig.Hides);
        rig.MoveTo(3838, 5);
        Assert.Equal(1, rig.Hides);
        rig.MoveTo(3790, 30);            // inside the margin of the primary
        Assert.Equal(0, rig.Shows);
        rig.MoveTo(3700, 30);
        Assert.Equal(1, rig.Shows);
    }

    [Fact]
    public void A_secondary_monitor_to_the_right_never_triggers_it()
    {
        var rig = new Rig();                // primary 0..1920
        rig.Service.Start();
        rig.MoveTo(2500, 5);
        rig.MoveTo(3839, 0);
        Assert.Equal(0, rig.Hides);
    }

    [Fact]
    public void A_primary_monitor_whose_top_is_not_zero_uses_its_own_top()
    {
        var rig = new Rig();
        rig.Probe.Primary = (1920, 200);
        rig.Service.Start();
        rig.MoveTo(1920, 0);
        Assert.Equal(0, rig.Hides);
        rig.MoveTo(1920, 210);
        Assert.Equal(1, rig.Hides);
    }

    [Fact]
    public void Stopping_ends_the_timer_and_shows_racks_that_the_corner_had_hidden()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(1920, 0);

        rig.Service.Stop();

        Assert.Equal(0, rig.Scheduler.ActiveCount);
        Assert.False(rig.Service.IsWatching);
        Assert.False(rig.Service.RacksHidden);
        Assert.Equal(1, rig.Shows);

        rig.Probe.Cursor = (1920, 0);
        rig.Scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Hides);       // no more ticks
    }

    [Fact]
    public void Stopping_when_nothing_is_hidden_shows_nothing_and_is_safe_to_repeat()
    {
        var rig = new Rig();
        rig.Service.Stop();
        rig.Service.Start();
        rig.Service.Stop();
        rig.Service.Stop();
        Assert.Equal(0, rig.Shows);
    }

    [Fact]
    public void It_can_be_started_again_after_a_stop_with_a_new_timer()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.Service.Stop();
        rig.Service.Start();
        Assert.Equal(2, rig.Scheduler.StartedCount);
        Assert.Equal(1, rig.Scheduler.ActiveCount);
    }

    [Fact]
    public void Dispose_is_a_stop()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.MoveTo(1920, 0);
        rig.Service.Dispose();
        Assert.Equal(0, rig.Scheduler.ActiveCount);
        Assert.Equal(1, rig.Shows);
    }

    [Fact]
    public void A_tick_that_throws_is_swallowed_and_the_watch_carries_on()
    {
        var rig = new Rig();
        rig.Service.Start();
        rig.Probe.Throw = new InvalidOperationException("no primary screen");
        rig.Scheduler.Advance(TimeSpan.FromMilliseconds(100));
        rig.Probe.Throw = null;

        rig.MoveTo(1920, 0);

        Assert.Equal(1, rig.Hides);
        Assert.Equal(1, rig.Scheduler.ActiveCount);
    }

    [Fact]
    public void The_hidden_flag_is_set_before_hiding_so_a_failing_hide_is_not_retried_on_every_tick()
    {
        var rig = new Rig { HideThrows = new InvalidOperationException("window closed") };
        rig.Service.Start();
        rig.MoveTo(1920, 0);
        rig.MoveTo(1920, 0);
        Assert.Equal(1, rig.Hides);
        Assert.True(rig.Service.RacksHidden);
    }
}
