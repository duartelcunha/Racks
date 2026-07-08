using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Racks.Util
{
    // Ice-rink physics for racks. Pushing a rack into another gives the other rack VELOCITY;
    // it then glides on its own, slowing by friction, and bounces off screen edges - like
    // sliding pucks on ice. One shared per-frame loop drives every moving rack; it starts
    // when something gains velocity and stops itself the instant everything is at rest, so an
    // idle desktop costs 0 CPU (the lesson from earlier runaway-loop bugs).
    //
    // The RackWindow the physics move must expose: Left/Top/Width/Height (WPF Window), a bool
    // for "locked/anchored", and a way to persist its final position. We keep those as small
    // delegates so this stays decoupled from RackWindow's internals.
    public sealed class PhysicsBody
    {
        public Window Window = null!;
        public double Vx, Vy;                 // velocity in DIP/second
        public Func<bool> IsAnchored = () => false; // locked or topmost: never moved by physics
        public Action OnSettled = () => { };  // called once when this body comes to rest (persist pos)
        public bool Moving => Math.Abs(Vx) > StopSpeed || Math.Abs(Vy) > StopSpeed;
        internal const double StopSpeed = 6.0; // DIP/s below which we consider it stopped
    }

    public static class RackPhysics
    {
        // Tuning for a "medium" ice feel.
        private const double Friction = 6.5;      // higher = stops sooner
        private const double Restitution = 0.55;  // energy kept after an edge bounce
        private const double PushSpeedPerPx = 26; // overlap px -> velocity handed to a pushed rack
        private const double MaxSpeed = 2600;     // clamp so a fast shove can't teleport

        private static readonly List<PhysicsBody> _bodies = new();
        private static bool _running;
        private static long _lastTicks;

        // Register a rack's body once (on creation). Safe to call again; ignores duplicates.
        public static void Register(PhysicsBody body)
        {
            if (body?.Window == null || _bodies.Contains(body)) return;
            _bodies.Add(body);
        }

        public static void Unregister(PhysicsBody body)
        {
            _bodies.Remove(body);
        }

        // Give `target` velocity away from `fromCenter`, proportional to how deep the overlap
        // is, along the shallower overlap axis. Called by the dragged rack each frame it
        // overlaps a neighbour. Kicks the shared loop into life.
        public static void Impart(PhysicsBody target, Rect targetRect, Rect pusherRect)
        {
            if (target.IsAnchored()) return;
            var intersect = Rect.Intersect(targetRect, pusherRect);
            if (intersect.IsEmpty || intersect.Width <= 0 || intersect.Height <= 0) return;

            double dx = (targetRect.Left + targetRect.Width / 2) - (pusherRect.Left + pusherRect.Width / 2);
            double dy = (targetRect.Top + targetRect.Height / 2) - (pusherRect.Top + pusherRect.Height / 2);

            if (intersect.Width < intersect.Height)
            {
                double dir = dx != 0 ? Math.Sign(dx) : 1;
                double add = dir * intersect.Width * PushSpeedPerPx;
                // Push apart immediately by the overlap so they never visually intersect,
                // then hand over speed so it keeps gliding.
                target.Window.Left += dir * intersect.Width;
                target.Vx = Clamp(target.Vx + add, -MaxSpeed, MaxSpeed);
            }
            else
            {
                double dir = dy != 0 ? Math.Sign(dy) : 1;
                double add = dir * intersect.Height * PushSpeedPerPx;
                target.Window.Top += dir * intersect.Height;
                target.Vy = Clamp(target.Vy + add, -MaxSpeed, MaxSpeed);
            }
            EnsureRunning();
        }

        // Start the loop after velocity was set directly on a body (flick-to-throw).
        public static void Kick() => EnsureRunning();

        private static void EnsureRunning()
        {
            if (_running) return;
            _running = true;
            _lastTicks = 0;
            CompositionTarget.Rendering += Tick;
        }

        private static void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= Tick;
        }

        private static void Tick(object? sender, EventArgs e)
        {
            // Real elapsed time so motion is frame-rate independent.
            long now = DateTime.UtcNow.Ticks;
            if (_lastTicks == 0) { _lastTicks = now; return; }
            double dt = (now - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            _lastTicks = now;
            if (dt <= 0) return;
            if (dt > 0.05) dt = 0.05; // clamp a stall so nothing leaps across the screen

            bool anyMoving = false;

            foreach (var b in _bodies)
            {
                if (b.Window == null || b.IsAnchored()) { b.Vx = b.Vy = 0; continue; }
                if (!b.Moving) { if (b.Vx != 0 || b.Vy != 0) { b.Vx = b.Vy = 0; b.OnSettled(); } continue; }

                // Integrate position.
                double nx = b.Window.Left + b.Vx * dt;
                double ny = b.Window.Top + b.Vy * dt;

                // Exponential friction: v *= e^(-friction*dt). Smooth, frame-independent.
                double decay = Math.Exp(-Friction * dt);
                b.Vx *= decay;
                b.Vy *= decay;

                // Bounce off the working area of the monitor the rack is on.
                var wa = ScreenBounds(b.Window);
                double w = b.Window.Width, h = b.Window.Height;
                if (nx < wa.Left) { nx = wa.Left; b.Vx = -b.Vx * Restitution; }
                else if (nx + w > wa.Right) { nx = wa.Right - w; b.Vx = -b.Vx * Restitution; }
                if (ny < wa.Top) { ny = wa.Top; b.Vy = -b.Vy * Restitution; }
                else if (ny + h > wa.Bottom) { ny = wa.Bottom - h; b.Vy = -b.Vy * Restitution; }

                b.Window.Left = nx;
                b.Window.Top = ny;

                // Rack-vs-rack collision. A gliding rack hitting another rack (locked or not)
                // is stopped/bounced at its edge - so a LOCKED rack is a solid hitbox nothing
                // passes through, and a moving rack transfers a shove to a free one.
                ResolveRackCollisions(b);

                if (b.Moving) anyMoving = true;
                else { b.Vx = b.Vy = 0; b.OnSettled(); }
            }

            if (!anyMoving) Stop();
        }

        // Separate a moving body from any rack it overlaps: eject it along the shallower
        // overlap axis and bounce its velocity on that axis. If the other rack is free (not
        // anchored), hand it some of the incoming speed so the collision passes energy on.
        private static void ResolveRackCollisions(PhysicsBody b)
        {
            var rect = new Rect(b.Window.Left, b.Window.Top, b.Window.Width, b.Window.Height);
            foreach (var o in _bodies)
            {
                if (o == b || o.Window == null) continue;
                var orect = new Rect(o.Window.Left, o.Window.Top, o.Window.Width, o.Window.Height);
                if (!rect.IntersectsWith(orect)) continue;
                var isect = Rect.Intersect(rect, orect);
                if (isect.IsEmpty || isect.Width <= 0 || isect.Height <= 0) continue;

                double cdx = (rect.Left + rect.Width / 2) - (orect.Left + orect.Width / 2);
                double cdy = (rect.Top + rect.Height / 2) - (orect.Top + orect.Height / 2);

                if (isect.Width < isect.Height)
                {
                    double dir = cdx != 0 ? Math.Sign(cdx) : 1; // push b away from o horizontally
                    b.Window.Left += dir * isect.Width;
                    if (!o.IsAnchored()) { o.Vx = Clamp(o.Vx - b.Vx * (1 - Restitution), -MaxSpeed, MaxSpeed); }
                    b.Vx = -b.Vx * Restitution;
                }
                else
                {
                    double dir = cdy != 0 ? Math.Sign(cdy) : 1;
                    b.Window.Top += dir * isect.Height;
                    if (!o.IsAnchored()) { o.Vy = Clamp(o.Vy - b.Vy * (1 - Restitution), -MaxSpeed, MaxSpeed); }
                    b.Vy = -b.Vy * Restitution;
                }
                rect = new Rect(b.Window.Left, b.Window.Top, b.Window.Width, b.Window.Height);
            }
        }

        private static Rect ScreenBounds(Window w)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                var wa = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                // WorkingArea is device px; convert to DIP so it matches Window.Left/Top.
                double scale = 1.0;
                var src = System.Windows.PresentationSource.FromVisual(w);
                if (src?.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
                return new Rect(wa.Left / scale, wa.Top / scale, wa.Width / scale, wa.Height / scale);
            }
            catch
            {
                return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            }
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
