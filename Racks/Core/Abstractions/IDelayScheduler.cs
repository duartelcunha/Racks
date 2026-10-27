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

/// <summary>Runs an action again and again at a fixed interval until the result is disposed. Exists so polling logic can be tested without waiting.</summary>
public interface IRepeatingScheduler
{
    IDisposable Every(TimeSpan interval, Action tick);
}

/// <summary>Production repeating scheduler: one <see cref="System.Windows.Threading.DispatcherTimer"/>, so ticks run on the UI thread (create it on the UI thread).</summary>
public sealed class DispatcherRepeatingScheduler : IRepeatingScheduler
{
    public IDisposable Every(TimeSpan interval, Action tick)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(System.Windows.Threading.DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
