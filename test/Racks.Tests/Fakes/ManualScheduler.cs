using Racks.Core.Abstractions;

namespace Racks.Tests.Fakes;

/// <summary>Deterministic <see cref="IDelayScheduler"/>: nothing runs until <see cref="Advance"/> moves the clock.</summary>
public sealed class ManualScheduler : IDelayScheduler
{
    private sealed class Entry : IDisposable
    {
        public TimeSpan Due;
        public Action Action = null!;
        public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }

    private readonly List<Entry> _entries = new();
    private TimeSpan _now;

    public int PendingCount => _entries.Count(e => !e.Cancelled);

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var e = new Entry { Due = _now + delay, Action = action };
        _entries.Add(e);
        return e;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        while (true)
        {
            var next = _entries.Where(e => !e.Cancelled && e.Due <= _now).OrderBy(e => e.Due).FirstOrDefault();
            if (next == null) break;
            _entries.Remove(next);
            next.Action();
        }
        _entries.RemoveAll(e => e.Cancelled);
    }
}
