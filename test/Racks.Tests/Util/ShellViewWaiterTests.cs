using Racks.Tests.Fakes;
using Racks.Util;

namespace Racks.Tests.Util;

public class ShellViewWaiterTests
{
    private sealed class Harness
    {
        public readonly ManualScheduler Clock = new();
        public int FindCalls;
        public IntPtr Handle = IntPtr.Zero;
        public readonly List<IntPtr> Found = new();
        public int GaveUp;
        public readonly ShellViewWaiter Waiter;

        public Harness()
        {
            Waiter = new ShellViewWaiter(() => { FindCalls++; return Handle; }, Clock, h => Found.Add(h), () => GaveUp++);
        }
    }

    [Fact]
    public void Attaches_as_soon_as_the_desktop_window_appears_without_blocking()
    {
        var h = new Harness();
        h.Waiter.Start();
        Assert.Equal(1, h.FindCalls);                         // one immediate attempt, then control returns
        Assert.Empty(h.Found);

        h.Clock.Advance(TimeSpan.FromSeconds(3));            // still missing for three polls
        h.Handle = new IntPtr(1234);
        h.Clock.Advance(ShellViewWaiter.Interval);

        Assert.Equal(new[] { new IntPtr(1234) }, h.Found);
        Assert.Equal(0, h.GaveUp);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(h.Found);                               // polling stopped once found
    }

    [Fact]
    public void Gives_up_after_the_attempt_limit()
    {
        var h = new Harness();
        h.Waiter.Start();
        for (int i = 0; i < ShellViewWaiter.MaxAttempts * 2; i++)   // one real second at a time
            h.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ShellViewWaiter.MaxAttempts, h.FindCalls);
        Assert.Equal(1, h.GaveUp);
        Assert.Empty(h.Found);
    }

    [Fact]
    public void Starting_again_while_waiting_does_not_start_a_second_poll()
    {
        var h = new Harness();
        h.Waiter.Start();
        h.Waiter.Start();
        h.Waiter.Start();
        Assert.Equal(1, h.FindCalls);

        h.Clock.Advance(ShellViewWaiter.Interval);
        Assert.Equal(2, h.FindCalls);                         // one poll per interval, not three
    }

    [Fact]
    public void Dispose_stops_polling()
    {
        var h = new Harness();
        h.Waiter.Start();
        h.Waiter.Dispose();
        h.Handle = new IntPtr(1);
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, h.FindCalls);
        Assert.Empty(h.Found);
    }

    [Fact]
    public void It_can_wait_again_after_it_has_found_the_window()
    {
        var h = new Harness { Handle = new IntPtr(7) };
        h.Waiter.Start();
        Assert.Single(h.Found);

        h.Handle = IntPtr.Zero;                               // Explorer restarted
        h.Waiter.Start();
        h.Handle = new IntPtr(8);
        h.Clock.Advance(ShellViewWaiter.Interval);

        Assert.Equal(new[] { new IntPtr(7), new IntPtr(8) }, h.Found);
    }
}
