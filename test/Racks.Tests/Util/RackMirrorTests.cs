using Racks.Util;

namespace Racks.Tests.Util;

public class RackMirrorTests
{
    [Theory]
    [InlineData(null, "Rack")]
    [InlineData("", "Rack")]
    [InlineData("   ", "Rack")]
    [InlineData("...", "Rack")]
    [InlineData("Work", "Work")]
    [InlineData("  Work  ", "Work")]
    [InlineData("Work.", "Work")]
    public void Sanitize_falls_back_and_trims(string? title, string expected)
        => Assert.Equal(expected, RackMirror.Sanitize(title!));

    [Fact]
    public void Sanitize_strips_invalid_filename_characters()
        => Assert.Equal("abc", RackMirror.Sanitize("a/b*c"));

    [Fact]
    public void Sanitize_caps_length_at_80()
        => Assert.Equal(80, RackMirror.Sanitize(new string('x', 200)).Length);
}
