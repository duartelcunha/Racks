using System.Windows;
using Racks.Util;

namespace Racks.Tests.Util;

// The ice-rink maths. Before this, a rack gliding into another threw the other one back
// TOWARD it, the pair re-overlapped and jittered, and slow glides stalled then jumped.
public class PhysicsStepTests
{
    private static readonly Rect Screen = new(0, 0, 4000, 2000);

    private static SimBody Rack(double x, double y = 500, double vx = 0, bool anchored = false) =>
        new() { X = x, Y = y, W = 300, H = 200, Vx = vx, Anchored = anchored };

    private static void Run(IReadOnlyList<SimBody> bodies, double seconds, double fps = 60)
    {
        int frames = (int)Math.Round(seconds * fps);
        for (int i = 0; i < frames; i++) PhysicsStep.Advance(bodies, 1.0 / fps, _ => Screen);
    }

    [Fact]
    public void A_rack_hit_from_the_left_slides_right_away_from_the_hitter()
    {
        var hitter = Rack(300, vx: 1500); // 100 DIP gap; a 1500 DIP/s glide travels ~230
        var target = Rack(700);
        var bodies = new[] { hitter, target };

        double targetStart = target.X;
        Run(bodies, 0.6);

        Assert.True(target.X > targetStart + 50, $"target should have been pushed right, X={target.X}");
        Assert.True(target.Vx >= 0, "the hit rack must never move back toward the hitter");
    }

    [Fact]
    public void After_a_collision_the_two_racks_do_not_overlap()
    {
        var bodies = new[] { Rack(100, vx: 2000), Rack(700), Rack(1300) };
        for (int i = 0; i < 120; i++)
        {
            PhysicsStep.Advance(bodies, 1.0 / 60, _ => Screen);
            for (int a = 0; a < bodies.Length; a++)
                for (int b = a + 1; b < bodies.Length; b++)
                {
                    var overlap = Rect.Intersect(bodies[a].Rect, bodies[b].Rect);
                    Assert.True(overlap.IsEmpty || overlap.Width < 1 || overlap.Height < 1,
                        $"frame {i}: racks {a} and {b} overlap by {overlap.Width:0.0}");
                }
        }
    }

    [Fact]
    public void Pushes_chain_from_rack_to_rack()
    {
        var a = Rack(100, vx: 2400);
        var b = Rack(450);
        var c = Rack(800);
        Run(new[] { a, b, c }, 1.0);
        Assert.True(c.X > 800, $"the third rack should have moved, X={c.X}");
    }

    [Fact]
    public void A_locked_rack_never_moves_and_the_hitter_bounces_back()
    {
        var hitter = Rack(300, vx: 1500); // 100 DIP gap; a 1500 DIP/s glide travels ~230
        var locked = Rack(700, anchored: true);
        Run(new[] { hitter, locked }, 0.8);

        Assert.Equal(700, locked.X);
        Assert.True(hitter.X + hitter.W <= locked.X + 0.5, "the hitter must stop at the locked rack's edge");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void A_glide_travels_the_same_distance_at_any_frame_rate(double fps)
    {
        var r = Rack(100, vx: 1200);
        Run(new[] { r }, 2.0, fps);
        // v0 / friction = 1200 / 6.5 = ~184.6 DIP for an exponential decay to rest.
        Assert.InRange(r.X - 100, 175, 190);
    }

    [Fact]
    public void A_slow_glide_keeps_its_sub_pixel_motion()
    {
        var r = Rack(100, vx: 20); // well under 1 px per frame at 60 Hz
        double last = r.X;
        for (int i = 0; i < 10; i++)
        {
            PhysicsStep.Advance(new[] { r }, 1.0 / 60, _ => Screen);
            Assert.True(r.X > last, $"frame {i}: the rack stalled at {r.X}");
            last = r.X;
        }
    }

    [Fact]
    public void A_rack_bounces_off_the_screen_edge()
    {
        var r = Rack(3600, vx: 2000);
        Run(new[] { r }, 1.0);
        Assert.True(r.X + r.W <= Screen.Right + 0.001);
        Assert.True(r.Vx <= 0, "after hitting the right edge it must move left or rest");
    }

    [Fact]
    public void A_long_stall_is_not_replayed_as_a_leap()
    {
        var r = Rack(100, vx: 2000);
        PhysicsStep.Advance(new[] { r }, 2.0, _ => Screen); // a 2 s hitch
        Assert.True(r.X - 100 <= 2000 * PhysicsStep.MaxFrame + 0.001);
    }
}
