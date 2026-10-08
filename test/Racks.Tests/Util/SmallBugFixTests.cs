using Racks.Core;
using Racks.Util;

namespace Racks.Tests.Util;

public class SortToggleTests
{
    // 1/2 name, 3/4 date modified, 5/6 date created, 7/8 type, 9/10 size.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    public void Clicking_the_active_ascending_key_flips_it_to_descending(int ascending)
        => Assert.Equal(ascending + 1, SortToggle.Next(ascending, ascending));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    public void Clicking_the_active_descending_key_flips_it_back_to_ascending(int ascending)
        => Assert.Equal(ascending, SortToggle.Next(ascending + 1, ascending));

    [Theory]
    [InlineData(2, 9, 9)]     // was name descending, pick size: size ascending (the old code gave 10)
    [InlineData(10, 1, 1)]
    [InlineData(4, 7, 7)]
    [InlineData(1, 3, 3)]
    [InlineData(8, 5, 5)]
    public void Picking_another_key_sorts_by_it_ascending(int current, int ascending, int expected)
        => Assert.Equal(expected, SortToggle.Next(current, ascending));
}

public class MagicOrganizeLayoutTests
{
    [Fact]
    public void Positions_are_in_wpf_units_not_pixels()
    {
        // 150% display: 2880x1620 px working area is 1920x1080 WPF units.
        var one = MagicOrganizeLayout.Grid(1, 0, 0, 2880, 1620, 1.5);
        double expectedX = (1920 - MagicOrganizeLayout.RackWidth) / 2;
        double expectedY = (1080 - MagicOrganizeLayout.RackHeight) / 2;
        Assert.Equal(expectedX, one[0].X, 3);
        Assert.Equal(expectedY, one[0].Y, 3);
    }

    [Fact]
    public void A_single_rack_is_centred_at_100_percent()
    {
        var one = MagicOrganizeLayout.Grid(1, 0, 0, 1920, 1040, 1.0);
        Assert.Equal((1920 - 300) / 2.0, one[0].X, 3);
        Assert.Equal((1040 - 380) / 2.0, one[0].Y, 3);
    }

    [Fact]
    public void Racks_form_a_grid_with_the_gap_between_them_and_never_overlap()
    {
        var slots = MagicOrganizeLayout.Grid(5, 0, 0, 3840, 2160, 2.0);   // 3 columns, 2 rows
        Assert.Equal(5, slots.Count);
        Assert.Equal(slots[0].X + MagicOrganizeLayout.RackWidth + MagicOrganizeLayout.Gap, slots[1].X, 3);
        Assert.Equal(slots[0].Y + MagicOrganizeLayout.RackHeight + MagicOrganizeLayout.Gap, slots[3].Y, 3);
        Assert.Equal(slots[0].X, slots[3].X, 3);
    }

    [Fact]
    public void The_working_area_offset_is_respected_and_scaled()
    {
        // Taskbar on the left: working area starts 96 px (= 64 units at 150%) in.
        var withOffset = MagicOrganizeLayout.Grid(1, 96, 0, 2784, 1620, 1.5);
        var without = MagicOrganizeLayout.Grid(1, 0, 0, 2784, 1620, 1.5);
        Assert.Equal(64, withOffset[0].X - without[0].X, 3);
    }

    [Fact]
    public void Too_many_racks_for_the_screen_start_near_the_corner_instead_of_off_screen()
    {
        var slots = MagicOrganizeLayout.Grid(30, 0, 0, 1280, 720, 1.0);
        Assert.Equal(50, slots[0].X, 3);
        Assert.Equal(50, slots[0].Y, 3);
    }

    [Fact]
    public void Empty_and_invalid_input_is_safe()
    {
        Assert.Empty(MagicOrganizeLayout.Grid(0, 0, 0, 1920, 1080, 1));
        Assert.Single(MagicOrganizeLayout.Grid(1, 0, 0, 1920, 1080, 0));   // zero scale treated as 1
    }
}

public class AutoOrganizerCategoryTests
{
    [Theory]
    [InlineData("png", "Images")]
    [InlineData("mp4", "Videos")]
    [InlineData("pdf", "Documents")]
    [InlineData("xlsx", "Spreadsheets")]
    [InlineData("zip", "Archives")]
    [InlineData("exe", "Programs")]
    [InlineData("cs", "Development")]
    [InlineData("lnk", "Apps")]
    [InlineData("folder", "Folders")]
    [InlineData("zzz", "Misc")]
    [InlineData("", "Misc")]
    public void Extensions_map_to_categories(string ext, string expected)
        => Assert.Equal(expected, AutoOrganizer.GetCategoryNameForExtension(ext));
}

[Collection("Registry")]
public class DropDefaultTests
{
    [Fact]
    public void A_new_rack_moves_dropped_files_by_default()
    {
        string previous = InstanceController.appName;
        InstanceController.appName = "RacksTests-" + Guid.NewGuid().ToString("N");
        Racks.MainWindow._controller = new InstanceController();
        try { Assert.False(new Instance("x", false).LinkOnDrop); }
        finally
        {
            Racks.MainWindow._controller = null!;
            InstanceController.appName = previous;
        }
    }
}
