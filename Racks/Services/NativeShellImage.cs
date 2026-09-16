using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Racks.Services;

/// <summary>Windows shell thumbnails with the shell's real file icon fallback.</summary>
internal static class NativeShellImage
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageSize { public int Width; public int Height; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory
    {
        [PreserveSig] int GetImage(ImageSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IImageFactory factory);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    public static BitmapSource? Load(string path, int size)
    {
        // This method is called from the existing thumbnail background task.
        int initialized = CoInitializeEx(IntPtr.Zero, 0);
        IImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var id = typeof(IImageFactory).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out factory));
            int pixels = Math.Clamp(size, 16, 512);
            Marshal.ThrowExceptionForHR(factory.GetImage(new ImageSize { Width = pixels, Height = pixels }, 0, out bitmap));
            if (bitmap == IntPtr.Zero) return null;
            var result = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            result.Freeze();
            return result;
        }
        catch (Exception error) { Debug.WriteLine($"Shell image unavailable for {path}: {error.Message}"); return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) Util.Interop.DeleteObject(bitmap);
            if (factory != null) Marshal.ReleaseComObject(factory);
            if (initialized >= 0) CoUninitialize();
        }
    }
}
