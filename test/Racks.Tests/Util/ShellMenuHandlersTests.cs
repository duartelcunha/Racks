using System.Reflection;
using Racks.Util;

namespace Racks.Tests.Util;

public class ShellMenuHandlersTests
{
    // The events are field-like, so the backing field holds the delegate; count its subscribers.
    private static int Subscribers(ShellContextMenu menu, string eventName)
    {
        var field = typeof(ShellContextMenu).GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (field.GetValue(menu) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    [Fact]
    public void Each_menu_replaces_the_previous_menus_handlers()
    {
        var menu = new ShellContextMenu();
        var handlers = new ShellMenuHandlers(menu);

        for (int i = 0; i < 200; i++)                       // 200 right clicks
            handlers.Set(() => { }, () => { });

        Assert.Equal(1, Subscribers(menu, "ContextMenuClosed"));
        Assert.Equal(1, Subscribers(menu, "ContextMenuRenameSelected"));
    }

    [Fact]
    public void A_menu_without_rename_removes_the_old_rename_handler()
    {
        var menu = new ShellContextMenu();
        var handlers = new ShellMenuHandlers(menu);

        handlers.Set(() => { }, () => { });                 // a menu where Rename exists but is never chosen
        handlers.Set(() => { }, null);                      // the next menu has no rename handler at all

        Assert.Equal(1, Subscribers(menu, "ContextMenuClosed"));
        Assert.Equal(0, Subscribers(menu, "ContextMenuRenameSelected"));
    }

    [Fact]
    public void Clear_detaches_everything_and_is_repeatable()
    {
        var menu = new ShellContextMenu();
        var handlers = new ShellMenuHandlers(menu);
        handlers.Set(() => { }, () => { });

        handlers.Clear();
        handlers.Clear();

        Assert.Equal(0, Subscribers(menu, "ContextMenuClosed"));
        Assert.Equal(0, Subscribers(menu, "ContextMenuRenameSelected"));
    }
}
