using System;
using System.Collections.Generic;

namespace Racks.Util
{
    // Release speed for flick-to-throw. Averages the drag over the last WindowSeconds of samples
    // instead of smoothing per mouse event, so one tiny, fast step can't spike the throw, and a
    // rack the user held still before letting go is not thrown at all.
    public sealed class FlickTracker
    {
        public const double WindowSeconds = 0.08;
        public const double RestSeconds = 0.07; // no movement for this long before release = parked

        private readonly Queue<(double t, double x, double y)> _samples = new();
        private readonly Func<double> _clock;

        public FlickTracker() : this(() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency) { }

        internal FlickTracker(Func<double> clock) => _clock = clock;

        public void Reset(double x, double y)
        {
            _samples.Clear();
            _samples.Enqueue((_clock(), x, y));
        }

        public void Add(double x, double y)
        {
            double now = _clock();
            _samples.Enqueue((now, x, y));
            while (_samples.Count > 2 && now - _samples.Peek().t > WindowSeconds) _samples.Dequeue();
        }

        // Velocity in DIP/s at the moment of release, clamped to PhysicsStep.MaxSpeed.
        public (double vx, double vy) Release()
        {
            if (_samples.Count < 2 || _clock() - _samples.ToArray()[^1].t > RestSeconds) return (0, 0);
            return Current();
        }

        // Current drag velocity (DIP/s): what a rack pushed right now should at least match.
        public (double vx, double vy) Current()
        {
            if (_samples.Count < 2) return (0, 0);
            var arr = _samples.ToArray();
            var last = arr[^1];
            var first = arr[0];
            double span = last.t - first.t;
            if (span <= 0) return (0, 0);
            return (PhysicsStep.Clamp((last.x - first.x) / span), PhysicsStep.Clamp((last.y - first.y) / span));
        }
    }
}
