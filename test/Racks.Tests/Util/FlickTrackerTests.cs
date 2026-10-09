using Racks.Util;

namespace Racks.Tests.Util;

public class FlickTrackerTests
{
    private sealed class Clock
    {
        public double Now;
        public double Read() => Now;
    }

    [Fact]
    public void Release_speed_is_the_average_over_the_last_samples()
    {
        var clock = new Clock();
        var t = new FlickTracker(clock.Read);
        t.Reset(0, 0);
        for (int i = 1; i <= 10; i++) { clock.Now = i * 0.016; t.Add(i * 16, 0); } // 1000 DIP/s

        var (vx, vy) = t.Release();
        Assert.InRange(vx, 990, 1010);
        Assert.Equal(0, vy);
    }

    [Fact]
    public void One_tiny_fast_step_does_not_spike_the_throw()
    {
        var clock = new Clock();
        var t = new FlickTracker(clock.Read);
        t.Reset(0, 0);
        for (int i = 1; i <= 5; i++) { clock.Now = i * 0.016; t.Add(i * 8, 0); } // 500 DIP/s
        clock.Now += 0.0005; t.Add(41, 0); // 1 px in half a millisecond: 2000 DIP/s on its own

        var (vx, _) = t.Release();
        Assert.InRange(vx, 400, 620);
    }

    [Fact]
    public void A_rack_held_still_before_release_is_not_thrown()
    {
        var clock = new Clock();
        var t = new FlickTracker(clock.Read);
        t.Reset(0, 0);
        clock.Now = 0.016; t.Add(30, 0);
        clock.Now = 0.5; // the mouse rested before the button came up

        Assert.Equal((0.0, 0.0), t.Release());
    }

    [Fact]
    public void Current_speed_is_available_mid_drag_for_pushing_other_racks()
    {
        var clock = new Clock();
        var t = new FlickTracker(clock.Read);
        t.Reset(0, 0);
        for (int i = 1; i <= 6; i++) { clock.Now = i * 0.016; t.Add(i * 10, 0); } // 625 DIP/s
        clock.Now += 0.2; // still dragging, just not sampled for a moment

        Assert.InRange(t.Current().vx, 600, 650);
        Assert.Equal((0.0, 0.0), t.Release()); // but a release now would count as parked
    }

    [Fact]
    public void The_throw_is_capped()
    {
        var clock = new Clock();
        var t = new FlickTracker(clock.Read);
        t.Reset(0, 0);
        clock.Now = 0.01; t.Add(500, 0); // 50 000 DIP/s

        Assert.Equal(PhysicsStep.MaxSpeed, t.Release().vx);
    }
}
