using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Racks.Util
{
    public static class DesktopIconPositioner
    {
        [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
        private class ShellWindows { }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellWindows
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(
                [In] ref object pvarLoc,
                [In] ref object pvarLocRoot,
                [In] int swClass,
                [Out] out int phwnd,
                [In] int swfwOptions);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            // Return the raw interface pointer and let us marshal it ourselves. Letting the
            // CLR auto-marshal the out object was throwing "Specified cast is not valid" on
            // this shell object; a manual GetObjectForIUnknown + QI is more forgiving.
            [PreserveSig]
            int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        }

        // Desktop doesn't hand out IFolderView directly (QueryService returns E_NOINTERFACE),
        // so we go through the browser: QueryService -> IShellBrowser, ask it for the active
        // IShellView, then QI that to IFolderView. Only QueryActiveShellView is needed, but the
        // preceding vtable slots (IOleWindow + earlier IShellBrowser methods) must be declared
        // in order so the slot lands correctly - they're placeholders, never called.
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow_placeholder();
            void ContextSensitiveHelp_placeholder();
            void InsertMenusSB_placeholder();
            void SetMenuSB_placeholder();
            void RemoveMenusSB_placeholder();
            void SetStatusTextSB_placeholder();
            void EnableModelessSB_placeholder();
            void TranslateAcceleratorSB_placeholder();
            void BrowseObject_placeholder();
            void GetViewStateStream_placeholder();
            void GetControlWindow_placeholder();
            void SendControlMsg_placeholder();
            [PreserveSig]
            int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
        }

        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            void GetCurrentViewMode(out uint pViewMode);
            void SetCurrentViewMode(uint ViewMode);
            void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void Item(int iItemIndex, out IntPtr ppidl);
            void ItemCount(uint uFlags, out int pcItems);
            void Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void GetSelectionMarkedItem(out int piItem);
            void GetFocusedItem(out int piItem);
            void GetItemPosition(IntPtr pidl, out Interop.POINT pt);
            void GetSpacing(ref Interop.POINT pt);
            void GetDefaultSpacing(ref Interop.POINT pt);
            void GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            void SelectAndPositionItems(uint cidl, IntPtr[] apidl, ref Interop.POINT apt, uint dwFlags);
        }
        
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        // Lay a set of desktop items out in a clean grid starting from the top-left corner,
        // skipping the very first column rows that Windows' own icons usually occupy. Used
        // when a rack is removed so its files come back to the desktop tidy instead of
        // scattered wherever Explorer drops them. Best-effort: any failure is swallowed so
        // removal never breaks because layout misbehaved.
        public static async void ArrangeInGrid(System.Collections.Generic.IEnumerable<string> fullPaths)
        {
            var paths = new System.Collections.Generic.List<string>(fullPaths);
            if (paths.Count == 0) return;

            await Task.Delay(400); // let Explorer materialize the returned icons first

            try
            {
                var view = GetDesktopFolderView();
                if (view == null) return;

                // Grid metrics: read the desktop's own icon spacing so we match its layout.
                Interop.POINT spacing = new Interop.POINT();
                try { view.GetSpacing(ref spacing); } catch { }
                int stepX = spacing.X > 0 ? spacing.X : 100;
                int stepY = spacing.Y > 0 ? spacing.Y : 110;

                var wa = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                int marginX = 16, marginY = 16;
                int cols = Math.Max(1, (wa.Width - marginX) / stepX);
                // Start a couple of rows down so we don't stack on Recycle Bin / This PC.
                int startRow = 2;

                for (int i = 0; i < paths.Count; i++)
                {
                    int slot = i;
                    int col = slot % cols;
                    int row = startRow + slot / cols;
                    int x = marginX + col * stepX;
                    int y = marginY + row * stepY;

                    IntPtr pidl = ILCreateFromPath(paths[i]);
                    if (pidl != IntPtr.Zero)
                    {
                        Interop.POINT pt = new Interop.POINT { X = x, Y = y };
                        IntPtr[] apidl = new IntPtr[] { pidl };
                        try { view.SelectAndPositionItems(1, apidl, ref pt, 0x10 /* SVSI_POSITIONITEM */); }
                        catch { }
                        ILFree(pidl);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArrangeInGrid failed: {ex.Message}");
            }
        }

        // Resolve the live desktop IFolderView. The desktop doesn't expose IFolderView directly
        // (QueryService for it returns E_NOINTERFACE), so we go through the browser:
        // QueryService -> IShellBrowser -> QueryActiveShellView -> IShellView, then QI to
        // IFolderView. NOTE: calling methods on this view across the process boundary is
        // unreliable (the interface isn't marshaled), so ArrangeInGrid is strictly best-effort
        // and its failures are swallowed. Exact icon placement from an outside process isn't
        // achievable this way - that's a Windows limitation, not a bug here.
        private static IFolderView? GetDesktopFolderView()
        {
            var shellWindows = (IShellWindows)new ShellWindows();
            object loc = 0; // CSIDL_DESKTOP
            object empty = Type.Missing;
            int hwnd;
            object window = shellWindows.FindWindowSW(ref loc, ref empty, 8 /* SWC_DESKTOP */, out hwnd, 1 /* SWFO_NEEDDISPATCH */);
            if (window == null) return null;
            var sp = (IServiceProvider)window;
            Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
            int hr = sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out IntPtr pBrowser);
            if (hr != 0 || pBrowser == IntPtr.Zero) return null;

            IShellBrowser sb;
            try { sb = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser); }
            finally { Marshal.Release(pBrowser); }

            int hr2 = sb.QueryActiveShellView(out object shellView);
            if (hr2 != 0 || shellView == null) return null;

            return shellView as IFolderView;
        }
    }
}
