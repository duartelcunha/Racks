using Racks.Rack;

namespace Racks.Tests.Rack;

public class RackNamesTests
{
    [Fact]
    public void A_free_name_is_kept()
        => Assert.Equal("Images", RackNames.Unique("Images", new[] { "Docs", "Videos" }));

    [Fact]
    public void A_taken_name_gets_the_next_free_number()
    {
        Assert.Equal("Images 2", RackNames.Unique("Images", new[] { "Images" }));
        Assert.Equal("Images 3", RackNames.Unique("Images", new[] { "Images", "Images 2" }));
    }

    [Fact]
    public void Names_are_compared_without_regard_to_case_like_the_registry()
        => Assert.Equal("images 2", RackNames.Unique("images", new[] { "Images" }));

    [Fact]
    public void A_gap_is_filled_and_nulls_are_ignored()
        => Assert.Equal("Images 2", RackNames.Unique("Images", new string?[] { "Images", null, "Images 3" }));
}
