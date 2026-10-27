using Racks.UseCases.Updates;
using System.Text.Json;
using Racks.Util;

namespace Racks.Tests.Util;

// The updater has one version comparison (UpdateVerifier.IsNewer) and one strict tag reader.
public class UpdaterVersionTests
{
    [Theory]
    [InlineData("1.1.10", "1.1.4.0", true)]
    [InlineData("1.2.0", "1.1.4.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.1.4", "1.1.4.0", false)]
    [InlineData("1.1.4", "1.1.4", false)]
    [InlineData("1.1.3", "1.1.4.0", false)]
    [InlineData("1.1.5", "v1.1.4", true)]
    [InlineData("garbage", "1.1.4.0", false)]
    [InlineData("1.1.5", "garbage", false)]
    public void IsNewer_compares_numerically(string version, string current, bool expected)
        => Assert.Equal(expected, UpdateVerifier.IsNewer(version, current));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v10.0.0", "10.0.0")]
    public void A_plain_version_tag_is_read(string tag, string expected)
    {
        Assert.True(ReleaseManifestValidator.TryGetTagVersion(JsonSerializer.SerializeToElement(new { tag_name = tag }), out var t, out var v));
        Assert.Equal(tag, t);
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("V1.2.3")]
    [InlineData("v1.2.3.4")]
    [InlineData("1.2")]
    [InlineData("1.2.3-beta")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3 <b>pwned</b>")]
    [InlineData("vv1.2.3")]
    [InlineData("")]
    public void Any_other_tag_is_ignored(string tag)
        => Assert.False(ReleaseManifestValidator.TryGetTagVersion(JsonSerializer.SerializeToElement(new { tag_name = tag }), out _, out _));

    [Fact]
    public void A_release_without_a_string_tag_is_ignored()
    {
        Assert.False(ReleaseManifestValidator.TryGetTagVersion(JsonSerializer.SerializeToElement(new { name = "v1.2.3" }), out _, out _));
        Assert.False(ReleaseManifestValidator.TryGetTagVersion(JsonSerializer.SerializeToElement(new { tag_name = 5 }), out _, out _));
    }
}
