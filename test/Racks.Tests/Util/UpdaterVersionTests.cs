namespace Racks.Tests.Util;

public class UpdaterVersionTests
{
    [Theory]
    [InlineData("v1.1.10", "1.1.4.0", true)]
    [InlineData("v1.2.0", "1.1.4.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("v1.1.4", "1.1.4.0", false)]
    [InlineData("1.1.4", "1.1.4", false)]
    [InlineData("v1.1.3", "1.1.4.0", false)]
    [InlineData("garbage", "1.1.4.0", false)]
    public void IsNewer_compares_numerically(string latestTag, string current, bool expected)
        => Assert.Equal(expected, Updater.IsNewer(latestTag, current));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3.4", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3-beta", "1.2.3")]
    [InlineData("", "0.0.0")]
    public void Norm3_normalizes_to_three_parts(string input, string expected)
        => Assert.Equal(expected, Updater.Norm3(input));
}
