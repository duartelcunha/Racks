using Racks.UseCases.Hotkeys;

namespace Racks.Infrastructure.Shell;

/// <summary>The real <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> behind <see cref="HotkeyController"/>.</summary>
public sealed class Win32HotkeyApi : IHotkeyApi
{
    public bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey) => Racks.Util.Interop.RegisterHotKey(hwnd, id, modifiers, virtualKey);
    public bool Unregister(IntPtr hwnd, int id) => Racks.Util.Interop.UnregisterHotKey(hwnd, id);
}
