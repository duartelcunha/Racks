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
