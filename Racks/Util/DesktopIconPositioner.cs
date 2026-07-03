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
            void QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
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

        public static async void SetDesktopIconPosition(string filename, int x, int y)
        {
            await Task.Delay(300); // Give Explorer time to create the icon

            try
            {
                var shellWindows = (IShellWindows)new ShellWindows();
                object loc = 0; // CSIDL_DESKTOP
                object empty = Type.Missing;
                int hwnd;
                object window = shellWindows.FindWindowSW(ref loc, ref empty, 8 /* SWC_DESKTOP */, out hwnd, 1 /* SWFO_NEEDDISPATCH */);
                
                if (window != null)
                {
                    var sp = (IServiceProvider)window;
                    Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                    Guid IID_IFolderView = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
                    object folderViewObj;
                    sp.QueryService(ref SID_STopLevelBrowser, ref IID_IFolderView, out folderViewObj); 
                    
                    if (folderViewObj != null)
                    {
                        var view = (IFolderView)folderViewObj;
                        
                        IntPtr pidl = ILCreateFromPath(filename);
                        if (pidl != IntPtr.Zero)
                        {
                            Interop.POINT pt = new Interop.POINT { X = x, Y = y };
                            IntPtr[] apidl = new IntPtr[] { pidl };
                            // SVSI_SELECT = 1, SVSI_POSITIONITEM = 0x10
                            view.SelectAndPositionItems(1, apidl, ref pt, 0x11);
                            ILFree(pidl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to position icon: {ex.Message}");
            }
        }
    }
}
