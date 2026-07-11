using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Racks.Util
{
    // Creates an NTFS directory junction (mount-point reparse point). Junctions
    // are the "hardlink equivalent" for folders that don't need admin / Developer
    // Mode:
    //   - same-volume only,
    //   - traversable by every Win32 / .NET file API, including file pickers,
    //   - no .lnk file on disk; the entry looks like a real folder,
    //   - removing the junction does NOT touch the target IF the caller deletes
    //     it as a reparse point (Directory.Delete(path, recursive: false)).
    //     NOTE: on modern .NET, Directory.Delete(path, recursive: true) SKIPS
    //     name-surrogate reparse points (junctions), so it does not follow them —
    //     but do not rely on that per call site: all recursive deletes of rack
    //     trees go through SafeDelete, the single audited no-follow walker.
    //
    // Strategy: build the REPARSE_DATA_BUFFER ourselves and call
    // DeviceIoControl(FSCTL_SET_REPARSE_POINT). If that fails for any reason
    // (AV interference, ACL quirks, OneDrive placeholder weirdness, etc.) we
    // fall back to spawning `cmd /c mklink /J` which is the system-blessed way
    // to create a junction and works under the same conditions.
    public static class JunctionHelper
    {
        private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_ALL = 0x00000007; // READ | WRITE | DELETE
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr InBuffer, int nInBufferSize,
            IntPtr OutBuffer, int nOutBufferSize,
            out int pBytesReturned,
            IntPtr lpOverlapped);

        public static bool TryCreate(string targetDir, string junctionPath)
        {
            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Log($"target missing: {targetDir}");
                    return false;
                }
                if (Directory.Exists(junctionPath) || File.Exists(junctionPath))
                {
                    Log($"dest exists: {junctionPath}");
                    return false;
                }

                if (TryCreateViaApi(targetDir, junctionPath, out int err))
                    return true;

                Log($"API junction failed (err={err}) — falling back to mklink. target={targetDir} junction={junctionPath}");

                if (TryCreateViaMklink(targetDir, junctionPath))
                    return true;

                Log("mklink fallback also failed");
                return false;
            }
            catch (Exception ex)
            {
                Log($"TryCreate exception: {ex}");
                return false;
            }
        }

        private static bool TryCreateViaApi(string targetDir, string junctionPath, out int win32Err)
        {
            win32Err = 0;
            Directory.CreateDirectory(junctionPath);

            string targetFull = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar);
            string substituteName = @"\??\" + targetFull;
            string printName = targetFull;

            byte[] subBytes = Encoding.Unicode.GetBytes(substituteName);
            byte[] printBytes = Encoding.Unicode.GetBytes(printName);

            int pathBytesLen = subBytes.Length + 2 + printBytes.Length + 2;
            int reparseDataLen = 8 + pathBytesLen;
            int totalLen = 8 + reparseDataLen;

            byte[] buffer = new byte[totalLen];
            int p = 0;
            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buffer, p); p += 4;
            BitConverter.GetBytes((ushort)reparseDataLen).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, p); p += 2;                       // Reserved
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, p); p += 2;                       // SubstituteNameOffset
            BitConverter.GetBytes((ushort)subBytes.Length).CopyTo(buffer, p); p += 2;         // SubstituteNameLength
            BitConverter.GetBytes((ushort)(subBytes.Length + 2)).CopyTo(buffer, p); p += 2;   // PrintNameOffset
            BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, p); p += 2;       // PrintNameLength
            subBytes.CopyTo(buffer, p); p += subBytes.Length;
            buffer[p++] = 0; buffer[p++] = 0;
            printBytes.CopyTo(buffer, p); p += printBytes.Length;
            buffer[p++] = 0; buffer[p++] = 0;

            IntPtr unmanaged = Marshal.AllocHGlobal(totalLen);
            try
            {
                Marshal.Copy(buffer, 0, unmanaged, totalLen);
                using var handle = CreateFileW(
                    junctionPath,
                    GENERIC_WRITE,
                    FILE_SHARE_ALL,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    win32Err = Marshal.GetLastWin32Error();
                    try { Directory.Delete(junctionPath); } catch { }
                    return false;
                }

                bool ok = DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT,
                    unmanaged, totalLen, IntPtr.Zero, 0, out _, IntPtr.Zero);
                if (!ok)
                {
                    win32Err = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    try { Directory.Delete(junctionPath); } catch { }
                    return false;
                }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(unmanaged);
            }
        }

        private static bool TryCreateViaMklink(string targetDir, string junctionPath)
        {
            try
            {
                if (Directory.Exists(junctionPath))
                {
                    // The API path left an empty dir behind; mklink wants an
                    // absent destination.
                    try { Directory.Delete(junctionPath); } catch { }
                }
                // Pass each token via ArgumentList (not a concatenated command line) so the
                // framework quotes the paths per-argument. Filesystem paths can't currently
                // inject (NTFS forbids quotes, Directory.Exists gates the target), but this keeps
                // the pattern injection-proof if a caller ever passes a non-filesystem string.
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add("mklink");
                psi.ArgumentList.Add("/J");
                psi.ArgumentList.Add(junctionPath);
                psi.ArgumentList.Add(targetDir);
                using var proc = Process.Start(psi)!;
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0 && IsReparsePoint(junctionPath))
                    return true;
                string stderr = proc.StandardError.ReadToEnd();
                Log($"mklink exit={proc.ExitCode} stderr={stderr}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"mklink exception: {ex.Message}");
                return false;
            }
        }

        public static bool IsReparsePoint(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        // Lightweight debug log so the user can grab the file and report what
        // went wrong with a junction creation. Best-effort, ignores failures.
        private static readonly object _logLock = new object();
        private static string LogPath => Path.Combine(Path.GetTempPath(), "Racks-junction.log");
        private static void Log(string msg)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
