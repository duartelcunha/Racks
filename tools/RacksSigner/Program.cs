using System.Security.Cryptography;
using System.Text;
using Racks.Util;
using RacksSigner;

// RacksSigner: owner-only tool that signs Racks release installers (docs/RELEASING.md).
//
//   keygen                                   create the key pair; private key -> Credential Manager,
//                                            prints ONLY the public key for UpdateTrust.cs
//   pubkey                                   print the stored key's public key
//   sign --installer <path> --version <v>    write <installer dir>/Racks-Setup-<v>.manifest.json + .sig
//        [--force]                           (overwrite an existing manifest/sig)
//   verify --installer <path> [--version <v>] [--public-key <base64 SPKI>]
//                                            run the app's own checks on installer + manifest + sig
//   export-backup --to <path>                passphrase-encrypted PKCS#8 (PBES2, AES-256-CBC,
//                                            SHA-256, 600k iterations) PEM; the passphrase is typed
//   import-backup --from <path>              restore such a backup into Credential Manager
//
// Secrets never go on the command line: passphrases are typed interactively.

return Cli.Run(args);

internal static class Cli
{
    private const int MinPassphraseLength = 12;
    private const int PbeIterations = 600_000;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var opts = ParseOptions(args.Skip(1).ToArray());
            return args[0] switch
            {
                "keygen" => KeyGen(),
                "pubkey" => PubKey(),
                "sign" => Sign(Required(opts, "installer"), Required(opts, "version"), opts.ContainsKey("force")),
                "verify" => Verify(Required(opts, "installer"), opts.GetValueOrDefault("version"), opts.GetValueOrDefault("public-key")),
                "export-backup" => ExportBackup(Required(opts, "to")),
                "import-backup" => ImportBackup(Required(opts, "from")),
                _ => Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException
                                      or CryptographicException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static int KeyGen()
    {
        if (CredentialStore.Exists())
            throw new InvalidOperationException(
                $"A signing key already exists under '{CredentialStore.Target}'. Refusing to replace it. " +
                $"Back it up (export-backup) before deleting it with: cmdkey /delete:{CredentialStore.Target}");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] pkcs8 = key.ExportPkcs8PrivateKey();
        try { CredentialStore.Write(pkcs8); }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }

        // Read it back to prove the stored key is the one whose public half we print.
        string pub = UpdateSignature.ExportPublicKey(key);
        using (var stored = LoadKey())
            if (UpdateSignature.ExportPublicKey(stored) != pub)
                throw new InvalidOperationException("The key read back from Credential Manager does not match. Nothing to paste.");

        Console.WriteLine($"Private key stored in Windows Credential Manager as '{CredentialStore.Target}' (persist: local machine).");
        Console.WriteLine("Paste this public key into Racks/Util/UpdateTrust.cs (PublicKeySpkiBase64):");
        Console.WriteLine();
        Console.WriteLine(pub);
        Console.WriteLine();
        Console.WriteLine("Next: make an encrypted offline backup with 'RacksSigner export-backup --to <removable drive>\\racks-signing-key.pem'.");
        return 0;
    }

    private static int PubKey()
    {
        using var key = LoadKey();
        string pub = UpdateSignature.ExportPublicKey(key);
        Console.WriteLine(pub);
        ReportEmbeddedKey(pub);
        return 0;
    }

    private static int Sign(string installerPath, string version, bool force)
    {
        if (!UpdateManifest.IsValidVersion(version))
            throw new ArgumentException($"'{version}' is not a plain version like 1.4.1.");
        var installer = new FileInfo(installerPath);
        if (!installer.Exists) throw new IOException($"Installer not found: {installer.FullName}");
        if (installer.Name != UpdateManifest.InstallerName(version))
            throw new ArgumentException($"The installer must be named {UpdateManifest.InstallerName(version)} (got {installer.Name}).");
        if (installer.Length <= 0 || installer.Length > UpdateManifest.MaxInstallerSize)
            throw new ArgumentException("The installer size is out of range.");

        using var key = LoadKey();
        string pub = UpdateSignature.ExportPublicKey(key);
        if (!UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64))
            throw new InvalidOperationException(
                "UpdateTrust.PublicKeySpkiBase64 is still the placeholder. Paste the public key (RacksSigner pubkey) into " +
                "Racks/Util/UpdateTrust.cs and release a build that contains it before signing.");
        if (pub != UpdateTrust.PublicKeySpkiBase64.Trim())
            throw new InvalidOperationException(
                "The stored signing key does not match the public key compiled into Racks (UpdateTrust.cs). Refusing to sign.");

        // Hash through one read-only handle that denies writers, then build the manifest.
        UpdateManifest manifest;
        using (var fs = new FileStream(installer.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            manifest = UpdateManifest.ForInstaller(version, SHA256.HashData(fs), fs.Length);

        byte[] manifestBytes = manifest.ToCanonicalBytes();
        string signature = UpdateSignature.Sign(manifestBytes, key);

        string dir = installer.DirectoryName!;
        string manifestPath = Path.Combine(dir, UpdateManifest.ManifestName(version));
        string sigPath = Path.Combine(dir, UpdateManifest.SignatureName(version));
        var mode = force ? FileMode.Create : FileMode.CreateNew;
        WriteFile(manifestPath, manifestBytes, mode);
        WriteFile(sigPath, Encoding.ASCII.GetBytes(signature), mode);

        Console.WriteLine($"Signed {installer.Name}");
        Console.WriteLine($"  sha256   {manifest.Sha256}");
        Console.WriteLine($"  size     {manifest.Size}");
        Console.WriteLine($"  manifest {manifestPath}");
        Console.WriteLine($"  sig      {sigPath}");

        // Prove the files on disk pass the app's own checks with the embedded key.
        return Verify(installer.FullName, version, null);
    }

    private static int Verify(string installerPath, string? version, string? publicKey)
    {
        var installer = new FileInfo(installerPath);
        if (!installer.Exists) throw new IOException($"Installer not found: {installer.FullName}");
        version ??= VersionFromName(installer.Name)
            ?? throw new ArgumentException("Pass --version; it cannot be read from the installer name.");
        string key = publicKey ?? UpdateTrust.PublicKeySpkiBase64;

        string dir = installer.DirectoryName!;
        string manifestPath = Path.Combine(dir, UpdateManifest.ManifestName(version));
        string sigPath = Path.Combine(dir, UpdateManifest.SignatureName(version));
        byte[]? manifestBytes = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        string? sig = File.Exists(sigPath) ? File.ReadAllText(sigPath, Encoding.ASCII) : null;

        // "0.0.0": the newer-than-current rule is the app's to apply; here only release binding.
        var result = UpdateVerifier.VerifyManifest(manifestBytes, sig, key, "v" + version, "0.0.0", out var manifest);
        if (result == UpdateCheck.Ok && manifest != null)
        {
            if (manifest.FileName != installer.Name) result = UpdateCheck.WrongRelease;
            else
            {
                using var fs = new FileStream(installer.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                result = UpdateVerifier.VerifyInstaller(manifest, fs);
            }
        }

        if (result == UpdateCheck.Ok)
        {
            Console.WriteLine($"OK: {installer.Name} matches its signed manifest for v{version}.");
            return 0;
        }
        Console.Error.WriteLine($"FAILED ({result}): {UpdateVerifier.Describe(result)}");
        return 1;
    }

    private static int ExportBackup(string to)
    {
        var target = new FileInfo(to);
        if (target.Exists) throw new IOException($"{target.FullName} already exists. Refusing to overwrite.");
        using var key = LoadKey();
        char[] pass = ReadNewPassphrase();
        try
        {
            var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, PbeIterations);
            char[] pem = new char[4096];
            if (!key.TryExportEncryptedPkcs8PrivateKeyPem(pass, pbe, pem, out int written))
                throw new CryptographicException("Could not export the encrypted key.");
            byte[] bytes = Encoding.ASCII.GetBytes(pem, 0, written);
            try { WriteFile(target.FullName, bytes, FileMode.CreateNew); }
            finally
            {
                Array.Clear(pem);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally { Array.Clear(pass); }

        Console.WriteLine($"Encrypted backup written to {target.FullName}");
        Console.WriteLine($"Public key: {UpdateSignature.ExportPublicKey(key)}");
        Console.WriteLine("Keep it offline (removable drive) and the passphrase somewhere separate. Without both, a lost key means a new key and a manual update for every user.");
        return 0;
    }

    private static int ImportBackup(string from)
    {
        if (CredentialStore.Exists())
            throw new InvalidOperationException(
                $"A signing key already exists under '{CredentialStore.Target}'. Delete it first (cmdkey /delete:{CredentialStore.Target}) if you really mean to replace it.");
        string pem = File.ReadAllText(from, Encoding.ASCII);
        char[] pass = ReadPassphrase("Backup passphrase: ");
        using var key = ECDsa.Create();
        try { key.ImportFromEncryptedPem(pem, pass); }
        finally { Array.Clear(pass); }

        byte[] pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            // Must be a P-256 key: signing a probe proves it.
            UpdateSignature.Sign(new byte[] { 0 }, key);
            CredentialStore.Write(pkcs8);
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }

        string pub = UpdateSignature.ExportPublicKey(key);
        Console.WriteLine($"Key restored to Windows Credential Manager as '{CredentialStore.Target}'.");
        Console.WriteLine($"Public key: {pub}");
        ReportEmbeddedKey(pub);
        return 0;
    }

    // ---------------------------------------------------------------------------------------------

    private static ECDsa LoadKey()
    {
        byte[] pkcs8 = CredentialStore.Read();
        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out int read);
            if (read != pkcs8.Length) throw new CryptographicException("Trailing data after the stored key.");
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    private static void ReportEmbeddedKey(string pub)
    {
        if (!UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64))
            Console.WriteLine("Note: Racks/Util/UpdateTrust.cs still has the placeholder key.");
        else if (pub != UpdateTrust.PublicKeySpkiBase64.Trim())
            Console.WriteLine("WARNING: this key does NOT match the public key in Racks/Util/UpdateTrust.cs.");
        else
            Console.WriteLine("Matches the public key in Racks/Util/UpdateTrust.cs.");
    }

    private static string? VersionFromName(string name)
    {
        const string prefix = "Racks-Setup-", suffix = ".exe";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return null;
        string v = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return UpdateManifest.IsValidVersion(v) ? v : null;
    }

    private static void WriteFile(string path, byte[] bytes, FileMode mode)
    {
        using var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None);
        fs.Write(bytes);
    }

    private static char[] ReadNewPassphrase()
    {
        char[] a = ReadPassphrase($"New backup passphrase (min {MinPassphraseLength} chars): ");
        char[] b = ReadPassphrase("Repeat passphrase: ");
        try
        {
            if (a.Length < MinPassphraseLength) throw new ArgumentException($"The passphrase must have at least {MinPassphraseLength} characters.");
            if (!a.AsSpan().SequenceEqual(b)) throw new ArgumentException("The passphrases do not match.");
            return a;
        }
        catch
        {
            Array.Clear(a);
            throw;
        }
        finally { Array.Clear(b); }
    }

    private static char[] ReadPassphrase(string prompt)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Passphrases must be typed interactively (stdin is redirected).");
        Console.Write(prompt);
        var buffer = new List<char>();
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (buffer.Count > 0) buffer.RemoveAt(buffer.Count - 1); continue; }
                if (!char.IsControl(k.KeyChar)) buffer.Add(k.KeyChar);
            }
            Console.WriteLine();
            return buffer.ToArray();
        }
        finally
        {
            for (int i = 0; i < buffer.Count; i++) buffer[i] = '\0';
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] rest)
    {
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < rest.Length; i++)
        {
            if (!rest[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{rest[i]}'.");
            string name = rest[i].Substring(2);
            if (name == "force") { opts[name] = "true"; continue; }
            if (i + 1 >= rest.Length) throw new ArgumentException($"--{name} needs a value.");
            opts[name] = rest[++i];
        }
        return opts;
    }

    private static string Required(Dictionary<string, string> opts, string name)
        => opts.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"--{name} is required.");

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() => Console.WriteLine("""
        RacksSigner: signs Racks release installers. See docs/RELEASING.md.

          keygen                                   create the signing key (stored in Windows Credential Manager)
          pubkey                                   print the stored key's public key
          sign --installer <path> --version <v> [--force]
          verify --installer <path> [--version <v>] [--public-key <base64 SPKI>]
          export-backup --to <path>                passphrase-encrypted backup of the private key
          import-backup --from <path>              restore a backup into Credential Manager
        """);
}
