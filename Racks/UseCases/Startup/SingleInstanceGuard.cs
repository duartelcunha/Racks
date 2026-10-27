namespace Racks.UseCases.Startup;

/// <summary>A held (or refused) single-instance name. The real one wraps a named <see cref="System.Threading.Mutex"/>.</summary>
public interface ISingleInstanceHandle : IDisposable
{
    /// <summary>True when this process created the name and so owns it; false when another Racks already holds it.</summary>
    bool CreatedNew { get; }
    /// <summary>Gives the name back. Only called on a handle that owns it.</summary>
    void Release();
}

/// <summary>
/// One Racks per Windows session. The second launch finds the name taken in under a millisecond and exits silently (the
/// first one's tray icon is already there), which replaced a process-list check that raced at startup and popped a dialog.
/// The name is <c>Local\</c> (session-scoped), not <c>Global\</c>: a global name let any process in any session squat it
/// and block every launch, and stopped two users on one machine from each running their own Racks.
/// Disabled (always acquires, holds nothing) in debug builds, as before. The handle factory is injected so tests never
/// create a mutex that could collide with the user's running Racks.
/// </summary>
public sealed class SingleInstanceGuard
{
    public const string MutexName = @"Local\Racks-SingleInstance-2C9D";

    private readonly bool _enabled;
    private readonly Func<string, ISingleInstanceHandle> _factory;
    private ISingleInstanceHandle? _handle;

    public SingleInstanceGuard(bool enabled, Func<string, ISingleInstanceHandle> factory)
    {
        _enabled = enabled;
        _factory = factory;
    }

    /// <summary>True when this process may go on (it is the first one, or the guard is disabled).</summary>
    public bool TryAcquire()
    {
        if (!_enabled) return true;
        _handle = _factory(MutexName);
        return _handle.CreatedNew;
    }

    /// <summary>
    /// Releases the name on exit. A refused second instance never owned it, so it is only disposed: releasing a mutex
    /// the thread does not own throws, which used to log a bogus crash entry on every second launch.
    /// </summary>
    public void Release()
    {
        var handle = _handle;
        _handle = null;
        if (handle == null) return;
        if (handle.CreatedNew) handle.Release();
        handle.Dispose();
    }
}
