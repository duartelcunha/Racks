using Microsoft.Win32;
using Racks.Core.Abstractions;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Fakes;

/// <summary>Deterministic <see cref="IRepeatingScheduler"/>: ticks only when <see cref="Advance"/> moves the clock.</summary>
public sealed class ManualRepeatingScheduler : IRepeatingScheduler
{
    private sealed class Entry : IDisposable
    {
        public TimeSpan Interval;
        public TimeSpan NextDue;
        public Action Tick = null!;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private readonly List<Entry> _entries = new();
    private TimeSpan _now;

    /// <summary>Timers that were started and not disposed.</summary>
    public int ActiveCount => _entries.Count(e => !e.Disposed);

    /// <summary>Every timer ever started (a restart makes a second one).</summary>
    public int StartedCount { get; private set; }

    public IDisposable Every(TimeSpan interval, Action tick)
    {
        var e = new Entry { Interval = interval, NextDue = _now + interval, Tick = tick };
        _entries.Add(e);
        StartedCount++;
        return e;
    }

    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        while (true)
        {
            var next = _entries.Where(e => !e.Disposed && e.NextDue <= end).OrderBy(e => e.NextDue).FirstOrDefault();
            if (next == null) break;
            _now = next.NextDue;
            next.NextDue += next.Interval;
            next.Tick();
        }
        _now = end;
    }
}

/// <summary>An in-memory <see cref="IRegistryRoot"/> that remembers every write, so a test can count them.</summary>
public sealed class InMemoryRegistryRoot : IRegistryRoot
{
    private readonly Dictionary<string, RegistryValue> _values = new(StringComparer.OrdinalIgnoreCase);

    public string Path => @"HKCU\SOFTWARE\RacksTests-memory";

    /// <summary>name = data, in the order written.</summary>
    public List<(string Name, string Data)> Writes { get; } = new();

    public List<string> Deletes { get; } = new();

    public RegistryValue? Read(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public void Write(string name, object data, RegistryValueKind kind)
    {
        _values[name] = new RegistryValue(data, kind);
        Writes.Add((name, Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
    }

    public bool Delete(string name)
    {
        Deletes.Add(name);
        return _values.Remove(name);
    }

    public string? Text(string name) => _values.TryGetValue(name, out var v) ? Convert.ToString(v.Data) : null;
}
