using System;
using System.Collections.Generic;
using System.Windows;

namespace Racks.Util
{
    // The pure maths of the ice-rink physics, with no windows involved so it can be tested.
    // Positions are doubles in DIP and are never read back from a window while a body glides,
    // so slow glides keep their sub-pixel motion instead of stalling and jumping.
    public sealed class SimBody
    {
        public double X, Y, W, H;   // top-left and size, DIP
        public double Vx, Vy;       // DIP/second
        public bool Anchored;       // locked, pinned or being dragged: a solid wall that never moves
        public bool Moving => !Anchored && (Math.Abs(Vx) > PhysicsStep.StopSpeed || Math.Abs(Vy) > PhysicsStep.StopSpeed);
        public Rect Rect => new(X, Y, W, H);
    }

    public static class PhysicsStep
    {
        public const double Friction = 6.5;      // higher = stops sooner
        public const double Restitution = 0.55;  // energy kept after a bounce or a collision
        public const double MaxSpeed = 2600;     // clamp so a fast shove can't teleport
        public const double StopSpeed = 6.0;     // DIP/s below which a body is at rest
        public const double SubStep = 1.0 / 240; // fixed step: collisions behave the same at any frame rate
        public const double MaxFrame = 0.05;     // a stall longer than this is not replayed (no leap across the screen)

        // Advance every body by `dt` seconds. `bounds` gives the working area (DIP) a body bounces in.
        public static void Advance(IReadOnlyList<SimBody> bodies, double dt, Func<SimBody, Rect> bounds)
        {
            if (dt <= 0) return;
            dt = Math.Min(dt, MaxFrame);
            int steps = Math.Max(1, (int)Math.Ceiling(dt / SubStep));
            double h = dt / steps;
            var area = new Rect[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) area[i] = bounds(bodies[i]);

            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    var b = bodies[i];
                    if (b.Anchored) { b.Vx = b.Vy = 0; continue; }
                    if (!b.Moving) continue;
                    Integrate(b, h, area[i]);
                }
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (bodies[i].Moving) Collide(bodies[i], bodies);
                }
            }
        }

        private static void Integrate(SimBody b, double h, Rect a)
        {
            b.X += b.Vx * h;
            b.Y += b.Vy * h;
            double decay = Math.Exp(-Friction * h); // exponential friction, frame-rate independent
            b.Vx *= decay;
            b.Vy *= decay;

            if (b.X < a.Left) { b.X = a.Left; b.Vx = Math.Abs(b.Vx) * Restitution; }
            else if (b.X + b.W > a.Right) { b.X = a.Right - b.W; b.Vx = -Math.Abs(b.Vx) * Restitution; }
            if (b.Y < a.Top) { b.Y = a.Top; b.Vy = Math.Abs(b.Vy) * Restitution; }
            else if (b.Y + b.H > a.Bottom) { b.Y = a.Bottom - b.H; b.Vy = -Math.Abs(b.Vy) * Restitution; }
        }

        // Separate `b` from every body it overlaps along the shallower axis. A free body takes half
        // the overlap each and, if the two are closing, they exchange momentum like equal pucks: the
        // hit body moves AWAY from the hitter. An anchored body is a wall: `b` takes the whole overlap
        // and bounces.
        internal static void Collide(SimBody b, IReadOnlyList<SimBody> bodies)
        {
            foreach (var o in bodies)
            {
                if (ReferenceEquals(o, b)) continue;
                var isect = Rect.Intersect(b.Rect, o.Rect);
                if (isect.IsEmpty || isect.Width <= 0 || isect.Height <= 0) continue;

                bool horizontal = isect.Width < isect.Height;
                double overlap = horizontal ? isect.Width : isect.Height;
                double centreB = horizontal ? b.X + b.W / 2 : b.Y + b.H / 2;
                double centreO = horizontal ? o.X + o.W / 2 : o.Y + o.H / 2;
                double n = centreO >= centreB ? 1 : -1; // direction from b towards o on this axis

                double vb = horizontal ? b.Vx : b.Vy;
                double vo = horizontal ? o.Vx : o.Vy;
                double closing = (vb - vo) * n; // > 0: they are moving into each other

                if (o.Anchored)
                {
                    Shift(b, horizontal, -n * overlap);
                    if (closing > 0) vb = -vb * Restitution;
                }
                else
                {
                    Shift(b, horizontal, -n * overlap / 2);
                    Shift(o, horizontal, n * overlap / 2);
                    if (closing > 0)
                    {
                        double impulse = (1 + Restitution) / 2 * closing;
                        vb -= n * impulse;
                        vo += n * impulse;
                    }
                }

                if (horizontal) { b.Vx = Clamp(vb); o.Vx = o.Anchored ? 0 : Clamp(vo); }
                else { b.Vy = Clamp(vb); o.Vy = o.Anchored ? 0 : Clamp(vo); }
            }
        }

        private static void Shift(SimBody body, bool horizontal, double d)
        {
            if (horizontal) body.X += d; else body.Y += d;
        }

        public static double Clamp(double v) => v < -MaxSpeed ? -MaxSpeed : (v > MaxSpeed ? MaxSpeed : v);
    }
}
