using System.Threading;
using System.Threading.Tasks;
using Racks.Core.Abstractions;
using Racks.UseCases.Startup;
using Application = System.Windows.Application;

namespace Racks.Infrastructure.Startup;

/// <summary>The uninstall watchdog: a background thread that sleeps for the timeout, then runs the action (the process exit).</summary>
internal sealed class ThreadWatchdog : IWatchdog
{
    public void Arm(TimeSpan timeout, Action onTimeout) =>
        new Thread(() => { Thread.Sleep(timeout); onTimeout(); }) { IsBackground = true }.Start();
}

/// <summary>A named <see cref="Mutex"/> held for the lifetime of the process.</summary>
internal sealed class MutexHandle : ISingleInstanceHandle
{
    private readonly Mutex _mutex;

    public MutexHandle(string name)
    {
        _mutex = new Mutex(true, name, out bool createdNew);
        CreatedNew = createdNew;
    }

    public bool CreatedNew { get; }
    public void Release() => _mutex.ReleaseMutex();
    public void Dispose() => _mutex.Dispose();
}

/// <summary>
/// A real wait on a thread-pool timer, then the action on the UI thread. Not a DispatcherTimer: the app's always-busy timers
/// and hooks can starve one, and the startup animation must run when the UI thread is free to render it.
/// </summary>
internal sealed class TaskDispatcherDelayScheduler : IDelayScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var cts = new CancellationTokenSource();
        Task.Delay(delay, cts.Token).ContinueWith(
            _ => Application.Current.Dispatcher.Invoke(action),
            cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        return cts;
    }
}
