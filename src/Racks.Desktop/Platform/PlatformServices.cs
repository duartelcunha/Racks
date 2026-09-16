using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Racks.Core;

namespace Racks.Desktop.Platform;

public sealed class PlatformServices : IFileActions
{
    public string FileManager => OperatingSystem.IsMacOS() ? "Finder" : "Explorer";
    public string ShortcutExtension => OperatingSystem.IsWindows() ? ".lnk" : ".webloc";
    public void Move(string source, string destination) => SafeFiles.MoveNoReplace(source, destination);

    public void CreateShortcut(string source, string destination)
    {
        if (SafeFiles.Exists(destination)) throw new IOException("The destination already exists.");
        if (OperatingSystem.IsWindows())
        {
            // Shell COM needs an STA. The operation coordinator itself runs off the UI thread.
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".racks-" + Guid.NewGuid().ToString("N") + ".lnk");
                    try { Racks.Util.ShellLinkHelper.Create(temporary, source, Path.GetDirectoryName(source) ?? ""); SafeFiles.MoveNoReplace(temporary, destination); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(); thread.Join();
            if (failure != null) throw failure;
        }
        else
        {
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true });
            writer.WriteStartDocument(); writer.WriteStartElement("plist"); writer.WriteAttributeString("version", "1.0");
            writer.WriteStartElement("dict"); writer.WriteElementString("key", "URL"); writer.WriteElementString("string", new Uri(source).AbsoluteUri);
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
        }
    }

    public void Open(string path)
    {
        if (!SafeFiles.Exists(path)) throw new IOException("This item is unavailable. Reconnect its folder and try again.");
        if (OperatingSystem.IsMacOS()) Start("/usr/bin/open", path);
        else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Reveal(string path)
    {
        if (OperatingSystem.IsMacOS()) Start("/usr/bin/open", "-R", path);
        else
        {
            var target = SafeFiles.Exists(path) ? path : Path.GetDirectoryName(path)!;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true });
        }
    }

    public void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void Start(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Process.Start(info);
    }

    public bool AttachToDesktop(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null) return false;
        if (OperatingSystem.IsWindows())
        {
            IntPtr desktop = IntPtr.Zero;
            EnumWindows((parent, _) =>
            {
                var candidate = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (candidate == IntPtr.Zero) return true;
                desktop = candidate; return false;
            }, IntPtr.Zero);
            if (desktop == IntPtr.Zero) return false;
            var position = ScreenPosition(window);
            SetParent(handle.Handle, desktop);
            if (GetParent(handle.Handle) != desktop) return false;
            var style = GetWindowLong(handle.Handle, -16);
            SetWindowLong(handle.Handle, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
            var point = new NativePoint { X = position.X, Y = position.Y };
            ScreenToClient(desktop, ref point);
            SetWindowPos(handle.Handle, IntPtr.Zero, point.X, point.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            return true;
        }
        if (OperatingSystem.IsMacOS() && handle.HandleDescriptor == "NSWindow")
        {
            // Desktop-level window, on all Spaces; native controls stay in the shared UI.
            ObjcSendLong(handle.Handle, Selector("setLevel:"), -2147483622);
            ObjcSendLong(handle.Handle, Selector("setCollectionBehavior:"), 1 | 256);
            return true;
        }
        return false;
    }

    public void Detach(Window window)
    {
        if (window.TryGetPlatformHandle() is not { } handle) return;
        if (OperatingSystem.IsWindows())
        {
            var position = ScreenPosition(window);
            SetParent(handle.Handle, IntPtr.Zero);
            var style = GetWindowLong(handle.Handle, -16);
            SetWindowLong(handle.Handle, -16, (style & ~0x40000000) | unchecked((int)0x80000000));
            SetScreenPosition(window, position);
        }
        else if (OperatingSystem.IsMacOS()) ObjcSendLong(handle.Handle, Selector("setLevel:"), 0);
    }

    public PixelPoint ScreenPosition(Window window) => OperatingSystem.IsWindows() &&
        window.TryGetPlatformHandle() is { } handle && GetWindowRect(handle.Handle, out var rect)
            ? new PixelPoint(rect.Left, rect.Top) : window.Position;

    public void SetScreenPosition(Window window, PixelPoint position)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle)
        {
            var point = new NativePoint { X = position.X, Y = position.Y };
            if ((GetWindowLong(handle.Handle, -16) & 0x40000000) != 0)
            {
                var parent = GetParent(handle.Handle);
                if (parent != IntPtr.Zero) ScreenToClient(parent, ref point);
            }
            SetWindowPos(handle.Handle, IntPtr.Zero, point.X, point.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }
        else window.Position = position;
    }

    public bool DesktopConnectionAlive(Window window) => !OperatingSystem.IsWindows() ||
        (window.TryGetPlatformHandle() is { } h && GetParent(h.Handle) != IntPtr.Zero && IsWindow(GetParent(h.Handle)));

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void ObjcSendLong(IntPtr receiver, IntPtr selector, long value);
}
