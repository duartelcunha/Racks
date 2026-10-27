using System.Text.RegularExpressions;
using Racks.Util;
using Racks.Views;

namespace Racks.Tests.AutoRoute;

// Instance.AutoRouteRegex stores exactly what it is given (INV-ROUTE-1): a saved rule is never lost because a
// check failed while loading (the slow-pattern probe is a wall-clock test, and a busy machine can fail it for a
// fine pattern). The registry load and the layout import assign through this setter. Whether a rule can run is
// decided by AutoRouteService (see AutoRouteServiceTests); whether a NEW rule may be saved, by the rule dialog.
public class AutoRouteRegexSetterTests
{
    private static Instance NewRack() => new("R", false);

    [Theory]
    [InlineData(@"\.pdf$")]
    [InlineData("([unclosed")]
    [InlineData("(a+)+$")]
    [InlineData("  spaced  ")]
    [InlineData(@"^(\w+\s?)*$")]
    public void The_text_is_kept_as_given_even_when_it_cannot_run(string pattern)
    {
        var rack = NewRack();
        using (rack.SuspendPersistence())
        {
            rack.AutoRouteRegex = @"\.old$";
            rack.AutoRouteRegex = pattern;
        }
        Assert.Equal(pattern, rack.AutoRouteRegex);
    }

    [Fact]
    public void Empty_and_null_clear_the_rule_as_before()
    {
        var rack = NewRack();
        using (rack.SuspendPersistence())
        {
            rack.AutoRouteRegex = @"\.pdf$";
            rack.AutoRouteRegex = "";
            Assert.Equal("", rack.AutoRouteRegex);
            rack.AutoRouteRegex = @"\.pdf$";
            rack.AutoRouteRegex = null!;
            Assert.Equal("", rack.AutoRouteRegex);
        }
    }

    [Fact]
    public void A_change_raises_one_notification_and_the_same_text_none()
    {
        var rack = NewRack();
        int changes = 0;
        rack.PropertyChanged += (_, _) => changes++;
        using (rack.SuspendPersistence())
        {
            rack.AutoRouteRegex = "([unclosed";
            rack.AutoRouteRegex = "([unclosed";
        }
        Assert.Equal(1, changes);
    }

    // ---- the edit-time check (rule dialog) --------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(@"\.pdf$")]
    [InlineData(@"^report-.*\.pdf$")]
    [InlineData("(jpg|png|gif)$")]
    public void The_editor_accepts_empty_and_usable_patterns(string text) => Assert.True(AutoRouteRuleDialog.IsSavable(text));

    [Theory]
    [InlineData("([unclosed")]
    [InlineData("*leading-star")]
    [InlineData("(a+)+$")]
    [InlineData("(a|aa)+$")]
    [InlineData(@"^(\w+\s?)*$")]
    [InlineData("(.*a){30}")]
    public void The_editor_refuses_invalid_and_too_slow_patterns_within_a_bounded_time(string text)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(AutoRouteRuleDialog.IsSavable(text));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "the probe is bounded by the match timeout");
    }

    [Fact]
    public void IsAcceptable_agrees_with_TryCompile_for_ordinary_patterns()
    {
        Assert.True(SafeRegex.IsAcceptable(@"\.pdf$", RegexOptions.IgnoreCase));
        Assert.False(SafeRegex.IsAcceptable(null));
        Assert.False(SafeRegex.IsAcceptable(""));
        Assert.False(SafeRegex.IsAcceptable("(unclosed"));
        Assert.False(SafeRegex.IsAcceptable("(a+)+$"));
    }
}
