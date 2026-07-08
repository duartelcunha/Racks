using System.Windows;

namespace Racks.Util
{
    // Multi-monitor helpers. WPF's WindowStartupLocation="CenterScreen" always centers on
    // the PRIMARY monitor, which is wrong when the user is working on a second screen. These
    // center a window on whichever monitor the mouse cursor is currently on.
    public static class WindowPlacement
    {
        // Center the window in the working area of the monitor under the cursor. Call from
        // Loaded so ActualWidth/Height are known; falls back to Width/Height otherwise.
        public static void CenterOnCursorScreen(Window window)
        {
            try
            {
                var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                var wa = screen.WorkingArea; // device pixels

                // Convert the window's size to device pixels so centering is correct under DPI.
                double scale = 1.0;
                var src = System.Windows.PresentationSource.FromVisual(window);
                if (src?.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;

                double w = (window.ActualWidth > 0 ? window.ActualWidth : window.Width) * scale;
                double h = (window.ActualHeight > 0 ? window.ActualHeight : window.Height) * scale;

                double leftDevice = wa.Left + (wa.Width - w) / 2;
                double topDevice = wa.Top + (wa.Height - h) / 2;

                // Window.Left/Top are in DIPs; convert device -> DIP.
                window.Left = leftDevice / scale;
                window.Top = topDevice / scale;
            }
            catch { /* best-effort: leave the default position on any failure */ }
        }
    }
}
