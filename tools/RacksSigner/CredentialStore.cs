using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RacksSigner;

// The private signing key lives ONLY in Windows Credential Manager (generic credential), as a
// PKCS#8 blob. Windows protects it with DPAPI under the owner's logon; it is never written to a
// file by this tool except as a passphrase-encrypted backup the owner asks for.
//
// Persistence: CRED_PERSIST_LOCAL_MACHINE. The key survives logoff/reboot but stays on THIS
// machine: it is not roamed to other machines with a roaming profile, which ENTERPRISE would do.
// Moving the key to another PC is an explicit export-backup / import-backup.
internal static class CredentialStore
{
    public const string Target = "Racks/update-signing";
    private const string UserName = "RacksSigner";
    private const string Comment = "Racks update signing key (ECDSA P-256, PKCS#8). See docs/RELEASING.md.";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;
    private const int CRED_MAX_CREDENTIAL_BLOB_SIZE = 5 * 512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);

    public static bool Exists()
    {
        if (CredRead(Target, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            CredFree(ptr);
            return true;
        }
        int err = Marshal.GetLastWin32Error();
        if (err == ERROR_NOT_FOUND) return false;
        throw new Win32Exception(err, "CredRead failed");
    }

    // Writes the blob; the caller zeroes its own copy afterwards.
    public static void Write(byte[] secret)
    {
        if (secret.Length == 0 || secret.Length > CRED_MAX_CREDENTIAL_BLOB_SIZE)
            throw new ArgumentException("Credential blob size is out of range.");
        IntPtr blob = Marshal.AllocHGlobal(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Target,
                Comment = Comment,
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = UserName,
            };
            if (!CredWrite(ref cred, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CredWrite failed");
        }
        finally
        {
            Marshal.Copy(new byte[secret.Length], 0, blob, secret.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    // Returns a copy of the blob; the caller must zero it (CryptographicOperations.ZeroMemory).
    public static byte[] Read()
    {
        if (!CredRead(Target, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND)
                throw new InvalidOperationException($"No signing key in Windows Credential Manager under '{Target}'. Run 'RacksSigner keygen' or 'RacksSigner import-backup'.");
            throw new Win32Exception(err, "CredRead failed");
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            int size = checked((int)cred.CredentialBlobSize);
            var result = new byte[size];
            if (size > 0)
            {
                Marshal.Copy(cred.CredentialBlob, result, 0, size);
                Marshal.Copy(new byte[size], 0, cred.CredentialBlob, size);
            }
            return result;
        }
        finally
        {
            CredFree(ptr);
        }
    }
}
