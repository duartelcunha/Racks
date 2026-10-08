namespace Racks.Core.Abstractions;

/// <summary>Runs an action once after a delay. Disposing the result cancels it. Exists so timing logic can be tested without sleeping.</summary>
public interface IDelayScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

/// <summary>Production scheduler: a one-shot <see cref="System.Threading.Timer"/> per call; callbacks run on the thread pool.</summary>
public sealed class TimerDelayScheduler : IDelayScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
        => new System.Threading.Timer(_ => action(), null, delay, Timeout.InfiniteTimeSpan);
}

/// <summary>Scheduler whose callbacks run on the WPF UI thread (a one-shot <see cref="System.Windows.Threading.DispatcherTimer"/> per call).</summary>
public sealed class DispatcherDelayScheduler : IDelayScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(System.Windows.Threading.DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
