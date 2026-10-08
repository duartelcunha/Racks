using System.Diagnostics;
using Racks.Util;

namespace Racks.Tests.Util;

public class SafeRegexTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("([unclosed")]
    public void TryCompile_returns_null_for_empty_or_invalid_pattern(string? pattern)
        => Assert.Null(SafeRegex.TryCompile(pattern));

    [Fact]
    public void TryCompile_sets_the_match_timeout()
    {
        var re = SafeRegex.TryCompile("abc");
        Assert.NotNull(re);
        Assert.Equal(SafeRegex.MatchTimeout, re!.MatchTimeout);
    }

    [Fact]
    public void IsMatch_null_regex_is_false()
        => Assert.False(SafeRegex.IsMatch(null, "anything", onTimeout: true));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsMatch_returns_the_fallback_when_the_pattern_times_out(bool onTimeout)
    {
        var re = SafeRegex.TryCompile("(a+)+$");
        var input = new string('a', 40) + "!";
        var sw = Stopwatch.StartNew();
        var result = SafeRegex.IsMatch(re, input, onTimeout);
        sw.Stop();
        Assert.Equal(onTimeout, result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "match must be bounded by the timeout");
    }

    [Fact]
    public void IsMatch_matches_normally()
        => Assert.True(SafeRegex.IsMatch(SafeRegex.TryCompile(@"^\d+\.pdf$"), "123.pdf", onTimeout: false));
}
