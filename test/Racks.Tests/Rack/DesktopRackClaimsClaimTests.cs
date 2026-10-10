using Racks.Rack;

namespace Racks.Tests.Rack;

// A claim is just a file name. Two racks claiming one name both show any file that has it.
public class DesktopRackClaimsClaimTests
{
    private static Instance Rack(string name, bool desktop = true, params string[] files)
        => new(name, false) { IsDesktopFilterRack = desktop, AssignedFiles = files.ToList() };

    [Fact]
    public void The_target_gets_the_name_and_other_racks_lose_it()
    {
        var a = Rack("A", true, "x.txt", "y.txt");
        var b = Rack("B");
        var c = Rack("C", true, "x.txt");

        var changed = DesktopRackClaims.Claim(new[] { a, b, c }, b, "x.txt");

        Assert.Equal(new[] { "x.txt" }, b.AssignedFiles);
        Assert.Equal(new[] { "y.txt" }, a.AssignedFiles);
        Assert.Empty(c.AssignedFiles);
        Assert.Equal(new[] { a, c }, changed);
    }

    [Fact]
    public void Claiming_twice_does_not_duplicate_and_reports_nothing_changed()
    {
        var a = Rack("A", true, "x.txt");
        var b = Rack("B");

        DesktopRackClaims.Claim(new[] { a, b }, a, "x.txt");
        var changed = DesktopRackClaims.Claim(new[] { a, b }, a, "x.txt");

        Assert.Equal(new[] { "x.txt" }, a.AssignedFiles);
        Assert.Empty(changed);
    }

    [Fact]
    public void Folder_racks_are_left_alone()
    {
        var folder = Rack("F", desktop: false, "x.txt");
        var target = Rack("T");

        var changed = DesktopRackClaims.Claim(new[] { folder, target }, target, "x.txt");

        Assert.Equal(new[] { "x.txt" }, folder.AssignedFiles);
        Assert.Empty(changed);
    }

    [Fact]
    public void A_target_without_a_list_gets_one()
    {
        var t = new Instance("T", false) { IsDesktopFilterRack = true, AssignedFiles = null! };
        DesktopRackClaims.Claim(new[] { t }, t, "x.txt");
        Assert.Equal(new[] { "x.txt" }, t.AssignedFiles);
    }
}
