using Microsoft.Win32.SafeHandles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Racks.Util
{
    public static class Interop
    {
        public static void SetDesktopIconsVisibility(bool visible)
        {
            IntPtr hwnd = FindWindow("Progman", "Program Manager");
            IntPtr defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null!);

            if (defView == IntPtr.Zero)
            {
                IntPtr workerW = IntPtr.Zero;
                do
                {
                    workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null!);
                    if (workerW != IntPtr.Zero)
                    {
                        defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null!);
                        if (defView != IntPtr.Zero)
                            break;
                    }
                } while (workerW != IntPtr.Zero);
            }

            if (defView != IntPtr.Zero)
            {
                ShowWindow(defView, visible ? 5 : 0); // SW_SHOW = 5, SW_HIDE = 0
            }
        }

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public uint x;
            public uint y;
            public uint cx;
            public uint cy;
            public uint flags;
        }

        public const uint GW_HWNDPREV = 3;
        public const uint GW_HWNDNEXT = 2;
        public const int GWL_EXSTYLE = -20;
        public const int GWL_STYLE = -16;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SW_SHOWNA = 8;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        // SHChangeNotify — tells the Windows shell that a file-system change happened
        // so any open Explorer view (including the Desktop, which is just a folder
        // view of %UserProfile%\Desktop + AllUsers\Desktop) repaints immediately.
        // Without this, after we move a file off the Desktop, Explorer's view caches
        // the icon for several seconds and the user sees a "ghost" of the old file.
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void RefreshDesktop()
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ALLEVENTS
        }

        public const int  SHCNE_RENAMEITEM    = 0x00000001;
        public const int  SHCNE_CREATE        = 0x00000002;
        public const int  SHCNE_DELETE        = 0x00000004;
        public const int  SHCNE_MKDIR         = 0x00000008;
        public const int  SHCNE_RMDIR         = 0x00000010;
        public const int  SHCNE_RENAMEFOLDER  = 0x00020000;
        public const int  SHCNE_UPDATEDIR     = 0x00001000;
        public const int  SHCNE_UPDATEITEM    = 0x00002000;
        public const int  SHCNE_ASSOCCHANGED  = 0x08000000;
        public const uint SHCNF_PATHW         = 0x0005;
        public const uint SHCNF_FLUSH         = 0x1000;
        public const uint SHCNF_FLUSHNOWAIT   = 0x3000;

        // High-level helper. Call ONCE per move and ONCE per source-parent at end of a
        // batch. NotifyShellMove fires the "this got renamed/moved" event for a single
        // path. NotifyShellUpdateDir refreshes a folder view (use on the source parent
        // after a batch so the Desktop redraws without F5).
        public static void NotifyShellMove(string oldPath, string newPath, bool isDirectory)
        {
            try
            {
                IntPtr p1 = Marshal.StringToHGlobalUni(oldPath);
                IntPtr p2 = Marshal.StringToHGlobalUni(newPath);
                try
                {
                    uint evt = (uint)(isDirectory ? SHCNE_RENAMEFOLDER : SHCNE_RENAMEITEM);
                    SHChangeNotify(evt, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, p1, p2);
                }
                finally { Marshal.FreeHGlobal(p1); Marshal.FreeHGlobal(p2); }
            }
            catch { /* notification is best-effort; never fail the move because of it */ }
        }

        public static void NotifyShellUpdateDir(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            try
            {
                IntPtr p = Marshal.StringToHGlobalUni(folderPath);
                try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, p, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        // Global hotkey (for cross-rack quick finder).
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_WIN = 0x8;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }


        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }


        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumedWindow lpEnumFunc, ArrayList lParam);

        public delegate bool EnumedWindow(IntPtr hwnd, ArrayList lParam);

        public static bool EnumWindowCallback(IntPtr hwnd, ArrayList lParam)
        {
            lParam.Add(hwnd);
            return true;
        }
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);
        public const int WM_NCACTIVATE = 0x0086;
        public const uint WM_SETREDRAW = 0x000B;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_MOVING = 0x0216;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_SETFOCUS = 0x0007;
        public const int WM_KILLFOCUS = 0x0008;
        public const int WM_SIZE = 0x0005;
        public const int SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOZORDER = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public const uint SHGFI_ICON = 0x000000100;      // Get icon
        public const uint SHGFI_LARGEICON = 0x000000000; // Large icon (default)
        public const uint SHGFI_SMALLICON = 0x000000001; // Small icon


        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int width, int height, uint uFlags);
        public const uint SWP_NOACTIVATE = 0x0010;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;

            public MARGINS(int left, int right, int top, int bottom)
            {
                Left = left;
                Right = right;
                Top = top;
                Bottom = bottom;
            }
        }
        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        public enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_INVALID_STATE = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        public enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19

        }
        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [ComImport, Guid("b3d0d38f-bc8d-420e-b3b8-4f3f1c2fbd88"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IContextMenu
        {
            void QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            void InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            void GetCommandString(uint idCmd, uint uFlags, uint reserved, StringBuilder commandString, uint cch);
        }

        [ComImport, Guid("0c5f5b9a-6990-11d2-b8c8-006097a5f6d0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem
        {
            void BindToHandler();
            void GetDisplayName();
            void GetAttributes();
        }



        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct CMINVOKECOMMANDINFO
        {
            public uint cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpDirectory;
            public uint nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
        }

        [Flags]
        public enum SHGFI : uint
        {
            Icon = 0x100,
            LargeIcon = 0x0,
            UseFileAttributes = 0x10,
            DisplayName = 0x200,
            TypeName = 0x400
        }

 
        [StructLayout(LayoutKind.Sequential)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public IntPtr lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }


        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);


        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);


        public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern int SHCreateItemFromParsingName(
           [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
           IntPtr pbc,
           ref Guid riid,
           [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);


        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        internal static int SHGetFileInfo(string path, int v1, ref SHFILEINFO shinfo, uint v2, int flags)
        {
            throw new NotImplementedException();
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]


        public interface IShellItemImageFactory
        {
            int GetImage(System.Drawing.Size size, int flags, out IntPtr phbm);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);
        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_READ_ATTRIBUTES = 0x80;
        private const uint FILE_SHARE_READWRITE = 0x7;
        private const uint OPEN_EXISTING = 3;
        public static ulong GetFileId(string path)
        {
            try
            {
                using var handle = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_READWRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                if (handle.IsInvalid)
                    return 0;
                return GetFileInformationByHandle(handle, out var info) ?
                    ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow : 0;
            }
            catch
            {
                return 0;
            }
        }

    }

}
