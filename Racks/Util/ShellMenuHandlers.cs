namespace Racks.Util;

/// <summary>
/// Holds the event handlers of the one <see cref="ShellContextMenu"/> a rack reuses for every right
/// click. Adding handlers with += on each click made them pile up: a "rename" handler from a menu
/// where Rename was never chosen fired on a later menu, and every "closed" handler ran each time.
/// Each <see cref="Set"/> replaces the previous menu's handlers, so at most one of each is attached.
/// </summary>
public sealed class ShellMenuHandlers
{
    private readonly ShellContextMenu _menu;
    private Action? _closed;
    private Action? _rename;

    public ShellMenuHandlers(ShellContextMenu menu) => _menu = menu;

    public void Set(Action? closed, Action? rename)
    {
        Clear();
        _closed = closed;
        _rename = rename;
        if (closed != null) _menu.ContextMenuClosed += closed;
        if (rename != null) _menu.ContextMenuRenameSelected += rename;
    }

    public void Clear()
    {
        if (_closed != null) _menu.ContextMenuClosed -= _closed;
        if (_rename != null) _menu.ContextMenuRenameSelected -= _rename;
        _closed = null;
        _rename = null;
    }
}
