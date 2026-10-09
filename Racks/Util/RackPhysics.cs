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
        public Func<bool> IsAnchored = () => false; // locked, topmost or being dragged: never moved by physics
        public Action OnSettled = () => { };  // called once when this body comes to rest (persist pos)
        public bool Moving => Math.Abs(Vx) > StopSpeed || Math.Abs(Vy) > StopSpeed;
        internal const double StopSpeed = PhysicsStep.StopSpeed;

        // True while the physics loop owns this window's position; RackWindow skips its own
        // per-move work (edge snapping, corner radii) until the glide ends.
        public bool Gliding { get; internal set; }

        internal readonly SimBody Sim = new();
        internal double WrittenX = double.NaN, WrittenY = double.NaN; // last position the loop put the window at
    }

    public static class RackPhysics
    {
        public const double MaxSpeed = PhysicsStep.MaxSpeed;
        private const double PushSpeedPerPx = 26; // overlap px -> velocity handed to a pushed rack
        private const double PushLead = 1.15;     // a pushed rack moves a bit faster than the rack pushing it
        // Rendering can fire far above the display rate while windows move; stepping and moving racks on
        // every one of those starved the drag of the rack doing the pushing (it froze mid-push).
        private static readonly TimeSpan MinFrame = TimeSpan.FromMilliseconds(12);

        private static readonly List<PhysicsBody> _bodies = new();
        private static bool _running;
        private static TimeSpan _lastFrame = TimeSpan.MinValue;

        // Global on/off for the ice-rink feel. When false, dragging a rack into another does
        // nothing (no push, no glide, no flick-to-throw): racks just overlap freely. Set from
        // the saved "IcePhysics" setting at startup and toggled live from Settings. Default on.
        public static bool Enabled = true;

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

        // Give `target` velocity away from the pusher, proportional to how deep the overlap is,
        // along the shallower overlap axis. Called by the dragged rack while it overlaps a
        // neighbour. Only velocity is handed over: the loop separates the two, so the pushed
        // window is moved by one owner (the loop), not also by the drag events.
        public static void Impart(PhysicsBody target, Rect targetRect, Rect pusherRect, (double vx, double vy) pusherVelocity = default)
        {
            if (!Enabled) return;
            if (target.IsAnchored()) return;
            var intersect = Rect.Intersect(targetRect, pusherRect);
            if (intersect.IsEmpty || intersect.Width <= 0 || intersect.Height <= 0) return;

            double dx = (targetRect.Left + targetRect.Width / 2) - (pusherRect.Left + pusherRect.Width / 2);
            double dy = (targetRect.Top + targetRect.Height / 2) - (pusherRect.Top + pusherRect.Height / 2);

            if (intersect.Width < intersect.Height)
            {
                double dir = dx != 0 ? Math.Sign(dx) : 1;
                // At least as fast as the pusher is moving (plus a little), so the dragged rack doesn't keep catching up.
                double v = dir * Math.Max(intersect.Width * PushSpeedPerPx, Math.Abs(pusherVelocity.vx) * PushLead);
                if (Math.Sign(v) != Math.Sign(target.Vx) || Math.Abs(v) > Math.Abs(target.Vx)) target.Vx = PhysicsStep.Clamp(v);
            }
            else
            {
                double dir = dy != 0 ? Math.Sign(dy) : 1;
                double v = dir * Math.Max(intersect.Height * PushSpeedPerPx, Math.Abs(pusherVelocity.vy) * PushLead);
                if (Math.Sign(v) != Math.Sign(target.Vy) || Math.Abs(v) > Math.Abs(target.Vy)) target.Vy = PhysicsStep.Clamp(v);
            }
            EnsureRunning();
        }

        // Start the loop after velocity was set directly on a body (flick-to-throw).
        public static void Kick() { if (Enabled) EnsureRunning(); }

        private static void EnsureRunning()
        {
            if (_running) return;
            _running = true;
            _lastFrame = TimeSpan.MinValue;
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
            // The frame's own timestamp: steps match what is shown, and a second callback for
            // the same frame (WPF can raise Rendering more than once) is skipped.
            TimeSpan frame = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
            if (_lastFrame == TimeSpan.MinValue) { _lastFrame = frame; return; }
            if (frame - _lastFrame < MinFrame) return;
            double dt = (frame - _lastFrame).TotalSeconds;
            _lastFrame = frame;
            if (dt <= 0) return;

            var live = new List<PhysicsBody>(_bodies.Count);
            foreach (var b in _bodies)
            {
                if (b.Window == null) continue;
                Sync(b);
                live.Add(b);
            }
            var areas = new Dictionary<SimBody, Rect>();
            foreach (var b in live) areas[b.Sim] = ScreenBounds(b.Window);
            PhysicsStep.Advance(live.ConvertAll(b => b.Sim), dt, s => areas[s]);

            bool anyMoving = false;
            foreach (var b in live)
            {
                b.Vx = b.Sim.Vx;
                b.Vy = b.Sim.Vy;
                bool shifted = Math.Abs(b.Sim.X - b.WrittenX) > 0.01 || Math.Abs(b.Sim.Y - b.WrittenY) > 0.01;
                if (!b.Sim.Anchored && shifted)
                {
                    b.Gliding = true;
                    MoveWindow(b, b.Sim.X, b.Sim.Y);
                }
                if (b.Moving) { anyMoving = true; continue; }
                b.Vx = b.Vy = 0;
                if (b.Gliding) { b.Gliding = false; b.OnSettled(); }
            }

            if (!anyMoving) Stop();
        }

        // Copy the window's state into its simulation body. A body the loop is not moving (or
        // that something else moved, e.g. a drag) starts from where its window really is;
        // a gliding body keeps its own sub-pixel position.
        private static void Sync(PhysicsBody b)
        {
            var s = b.Sim;
            s.Anchored = b.IsAnchored();
            s.W = b.Window.Width;
            s.H = b.Window.Height;
            s.Vx = b.Vx;
            s.Vy = b.Vy;
            bool movedElsewhere = double.IsNaN(b.WrittenX)
                || Math.Abs(b.Window.Left - b.WrittenX) > 1.5 || Math.Abs(b.Window.Top - b.WrittenY) > 1.5;
            if (!b.Gliding || movedElsewhere)
            {
                s.X = b.Window.Left;
                s.Y = b.Window.Top;
                b.WrittenX = s.X;
                b.WrittenY = s.Y;
            }
        }

        // One SetWindowPos per frame (WPF's Left and Top setters move the window twice, which
        // shows as a staircase on diagonal glides). Racks are children of the desktop view, so
        // the screen position is converted to the parent's client coordinates.
        private static void MoveWindow(PhysicsBody b, double x, double y)
        {
            b.WrittenX = x;
            b.WrittenY = y;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(b.Window).Handle;
                if (hwnd == IntPtr.Zero) { b.Window.Left = x; b.Window.Top = y; return; }
                double scale = 1.0;
                var src = PresentationSource.FromVisual(b.Window);
                if (src?.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
                var pt = new Interop.POINT { X = (int)Math.Round(x * scale), Y = (int)Math.Round(y * scale) };
                var parent = Interop.GetParent(hwnd);
                if (parent != IntPtr.Zero) Interop.ScreenToClient(parent, ref pt);
                Interop.SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, 0, 0,
                    Interop.SWP_NOZORDER | Interop.SWP_NOSIZE | Interop.SWP_NOACTIVATE | Interop.SWP_NOOWNERZORDER);
            }
            catch
            {
                b.Window.Left = x;
                b.Window.Top = y;
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

    }
}
