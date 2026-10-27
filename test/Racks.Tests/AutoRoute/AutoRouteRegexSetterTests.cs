using System.Diagnostics;
using System.Text.RegularExpressions;
using Racks.Util;

namespace Racks.Tests.AutoRoute;

// Instance.AutoRouteRegex keeps only a pattern that can run (INV-ROUTE-1, INV-REGEX-1). Like the font and color
// setters, a value that is not usable is ignored and the previous one stays. The registry load and the layout
// import both assign through this setter, so they behave the same way.
public class AutoRouteRegexSetterTests
{
    private static Instance NewRack()
    {
        var rack = new Instance("R", false);
        return rack;
    }

    [Theory]
    [InlineData(@"\.pdf$")]
    [InlineData(@"^report-.*\.pdf$")]
    [InlineData(@".*screenshot.*")]
    [InlineData(@"(jpg|png|gif)$")]
    public void A_valid_pattern_is_kept(string pattern)
    {
        var rack = NewRack();
        using (rack.SuspendPersistence()) rack.AutoRouteRegex = pattern;
        Assert.Equal(pattern, rack.AutoRouteRegex);
    }

    [Theory]
    [InlineData("([unclosed")]
    [InlineData("*leading-star")]
    [InlineData("(?<n>a)(?<n")]
    public void An_invalid_pattern_is_rejected_and_the_previous_one_stays(string pattern)
    {
        var rack = NewRack();
        using (rack.SuspendPersistence())
        {
            rack.AutoRouteRegex = @"\.pdf$";
            rack.AutoRouteRegex = pattern;
        }
        Assert.Equal(@"\.pdf$", rack.AutoRouteRegex);
    }

    [Theory]
    [InlineData("(a+)+$")]
    [InlineData("(a|aa)+$")]
    [InlineData(@"^(\w+\s?)*$")]
    [InlineData("(.*a){30}")]
    public void A_pattern_that_is_too_slow_is_rejected_and_the_check_is_bounded(string pattern)
    {
        var rack = NewRack();
        var sw = Stopwatch.StartNew();
        using (rack.SuspendPersistence())
        {
            rack.AutoRouteRegex = "keep";
            rack.AutoRouteRegex = pattern;
        }
        sw.Stop();
        Assert.Equal("keep", rack.AutoRouteRegex);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "the probe is bounded by the match timeout");
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
    public void A_rejected_value_raises_no_change_notification()
    {
        var rack = NewRack();
        int changes = 0;
        rack.PropertyChanged += (_, _) => changes++;
        using (rack.SuspendPersistence()) rack.AutoRouteRegex = "([unclosed";
        Assert.Equal(0, changes);
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
