using Racks.Util;

namespace Racks.Tests.Util;

// Auto-routing did nothing on plain (desktop) racks: their folder is the Desktop, and moving a file
// from the Desktop into the Desktop is refused. They must route into the workspace instead.
public class DesktopRouterDestinationTests
{
    // Setting Folder normally saves to the registry through the main window; tests only need the values.
    private static Instance Rack(string name, bool desktop, string folder)
    {
        var rack = new Instance(name, false);
        using (rack.SuspendPersistence())
        {
            rack.IsDesktopFilterRack = desktop;
            rack.Folder = folder;
        }
        return rack;
    }

    [Fact]
    public void A_desktop_rack_routes_into_the_workspace()
    {
        var rack = Rack("Plain", true, @"C:\Users\x\Desktop");
        Assert.Equal(@"C:\ws", DesktopRouter.DestinationFor(rack, @"C:\ws"));
    }

    [Fact]
    public void A_folder_rack_routes_into_its_folder()
    {
        var rack = Rack("Folder", false, @"D:\Photos");
        Assert.Equal(@"D:\Photos", DesktopRouter.DestinationFor(rack, @"C:\ws"));
    }

    [Fact]
    public void The_destination_for_a_desktop_rack_is_never_the_desktop_it_would_be_moved_from()
    {
        string desktop = @"C:\Users\x\Desktop";
        var rack = Rack("Plain", true, desktop);
        Assert.NotEqual(desktop, DesktopRouter.DestinationFor(rack, @"C:\Users\x\RacksWorkspace"));
    }
}
