using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Racks.Util;

// Signed update manifests (INV-UPDATE-1).
//
// This file is BCL-only and is compiled into BOTH Racks and tools/RacksSigner (linked source), so
// the bytes the signer signs and the bytes the updater verifies come from one implementation.
//
// A release carries three assets:
//   Racks-Setup-<v>.exe                 the installer
//   Racks-Setup-<v>.manifest.json       canonical manifest (below), exact bytes are signed
//   Racks-Setup-<v>.manifest.json.sig   base64 ECDSA P-256 / SHA-256 signature (IEEE P1363, r||s)
//
// Canonical manifest: UTF-8 JSON, no whitespace, fields in this fixed order:
//   {"product":"Racks","version":"1.4.1","fileName":"Racks-Setup-1.4.1.exe","sha256":"<64 lowercase hex>","size":123}
// Every field is restricted to [A-Za-z0-9.-] so there is exactly one valid encoding. Binding the
// version and file name into the signed bytes means an old, genuinely signed installer cannot be
// replayed under a newer release tag.
public sealed record UpdateManifest(string Product, string Version, string FileName, string Sha256, long Size)
{
    public const string ProductName = "Racks";
    public const long MaxInstallerSize = 512L * 1024 * 1024;
    public const int MaxManifestBytes = 1024;
    public const int MaxSignatureBytes = 1024;

    public static string InstallerName(string version) => $"Racks-Setup-{version}.exe";
    public static string ManifestName(string version) => $"Racks-Setup-{version}.manifest.json";
    public static string SignatureName(string version) => ManifestName(version) + ".sig";

    private static readonly Regex VersionPattern =
        new(@"\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Sha256Pattern =
        new(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // "1.4.1": three numeric parts, no leading zeros, no "v", no suffix.
    public static bool IsValidVersion(string? version) => version != null && VersionPattern.IsMatch(version);

    public static UpdateManifest ForInstaller(string version, ReadOnlySpan<byte> sha256, long size)
        => new(ProductName, version, InstallerName(version), Convert.ToHexStringLower(sha256), size);

    public bool IsWellFormed
        => Product == ProductName
           && IsValidVersion(Version)
           && FileName == InstallerName(Version)
           && Sha256 != null && Sha256Pattern.IsMatch(Sha256)
           && Size > 0 && Size <= MaxInstallerSize;

    public byte[] ToCanonicalBytes()
    {
        if (!IsWellFormed) throw new InvalidOperationException("Manifest fields are not well-formed.");
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("product", Product);
            w.WriteString("version", Version);
            w.WriteString("fileName", FileName);
            w.WriteString("sha256", Sha256);
            w.WriteNumber("size", Size);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    // Accepts ONLY the canonical encoding: the bytes must parse to a well-formed manifest AND
    // re-encode to exactly the same bytes (no extra fields, reordering, whitespace or escapes).
    public static bool TryParseCanonical(ReadOnlySpan<byte> bytes, out UpdateManifest? manifest)
    {
        manifest = null;
        if (bytes.Length == 0 || bytes.Length > MaxManifestBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("product", out var p) || p.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("fileName", out var f) || f.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("sha256", out var h) || h.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("size", out var s) || s.ValueKind != JsonValueKind.Number
                || !s.TryGetInt64(out long size)) return false;
            var m = new UpdateManifest(p.GetString()!, v.GetString()!, f.GetString()!, h.GetString()!, size);
            if (!m.IsWellFormed) return false;
            if (!m.ToCanonicalBytes().AsSpan().SequenceEqual(bytes)) return false;
            manifest = m;
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}

// ECDSA P-256 with SHA-256 over the canonical manifest bytes. Signatures are the fixed 64-byte
// IEEE P1363 form (r||s), stored base64 in the .sig file. Public keys are SubjectPublicKeyInfo
// (SPKI) DER, base64.
public static class UpdateSignature
{
    public const int SignatureLength = 64;
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static string Sign(ReadOnlySpan<byte> manifestBytes, ECDsa privateKey)
    {
        if (!IsP256(privateKey)) throw new ArgumentException("The signing key must be ECDSA P-256.", nameof(privateKey));
        byte[] sig = privateKey.SignData(manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(sig);
    }

    public static bool Verify(ReadOnlySpan<byte> manifestBytes, string? signatureText, string? publicKeySpkiBase64)
    {
        if (!TryDecodeSignature(signatureText, out var sig)) return false;
        using var key = TryImportPublicKey(publicKeySpkiBase64);
        if (key == null) return false;
        return key.VerifyData(manifestBytes, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    // Exactly one P-256 SPKI, nothing trailing. Returns null for anything else (including the
    // placeholder in UpdateTrust).
    public static ECDsa? TryImportPublicKey(string? spkiBase64)
    {
        if (string.IsNullOrWhiteSpace(spkiBase64)) return null;
        byte[] der;
        try { der = Convert.FromBase64String(spkiBase64.Trim()); }
        catch (FormatException) { return null; }
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(der, out int read);
            if (read != der.Length || !IsP256(key)) { key.Dispose(); return null; }
            return key;
        }
        catch (CryptographicException) { key.Dispose(); return null; }
    }

    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static bool IsP256(ECDsa key)
    {
        if (key.KeySize != 256) return false;
        var curve = key.ExportParameters(false).Curve;
        if (!curve.IsNamed) return false;
        return curve.Oid.Value == P256Oid
               || string.Equals(curve.Oid.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase)
               || string.Equals(curve.Oid.FriendlyName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodeSignature(string? text, out byte[] sig)
    {
        sig = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text) || text.Length > UpdateManifest.MaxSignatureBytes) return false;
        try { sig = Convert.FromBase64String(text.Trim()); }
        catch (FormatException) { return false; }
        return sig.Length == SignatureLength;
    }
}

public enum UpdateCheck
{
    Ok,
    NoTrustedKey,
    MissingManifest,
    MissingSignature,
    BadSignature,
    MalformedManifest,
    WrongRelease,
    NotNewer,
    SizeMismatch,
    HashMismatch,
}

// The two checks the updater runs. Both fail closed: anything other than UpdateCheck.Ok means the
// installer is not launched.
public static class UpdateVerifier
{
    // Step 1, before the installer is downloaded: the manifest is signed by the embedded key,
    // belongs to THIS release tag, and is newer than what is running.
    public static UpdateCheck VerifyManifest(byte[]? manifestBytes, string? signatureText, string? publicKeySpkiBase64,
                                             string releaseTag, string currentVersion, out UpdateManifest? manifest)
    {
        manifest = null;
        using (var key = UpdateSignature.TryImportPublicKey(publicKeySpkiBase64))
            if (key == null) return UpdateCheck.NoTrustedKey;
        if (manifestBytes == null || manifestBytes.Length == 0 || manifestBytes.Length > UpdateManifest.MaxManifestBytes)
            return UpdateCheck.MissingManifest;
        if (string.IsNullOrWhiteSpace(signatureText)) return UpdateCheck.MissingSignature;
        if (!UpdateSignature.Verify(manifestBytes, signatureText, publicKeySpkiBase64)) return UpdateCheck.BadSignature;
        if (!UpdateManifest.TryParseCanonical(manifestBytes, out var m) || m == null) return UpdateCheck.MalformedManifest;
        if (!TagMatches(releaseTag, m.Version)) return UpdateCheck.WrongRelease;
        if (!IsNewer(m.Version, currentVersion)) return UpdateCheck.NotNewer;
        manifest = m;
        return UpdateCheck.Ok;
    }

    // Step 2, on the downloaded bytes (read through the handle that is kept open until launch):
    // exact size and SHA-256 from the signed manifest.
    public static UpdateCheck VerifyInstaller(UpdateManifest manifest, Stream installer)
    {
        if (installer.Length != manifest.Size) return UpdateCheck.SizeMismatch;
        installer.Position = 0;
        byte[] actual = SHA256.HashData(installer);
        byte[] expected = Convert.FromHexString(manifest.Sha256);
        if (installer.Position != manifest.Size) return UpdateCheck.SizeMismatch;
        return CryptographicOperations.FixedTimeEquals(actual, expected) ? UpdateCheck.Ok : UpdateCheck.HashMismatch;
    }

    // The release tag must name exactly the signed version ("v1.4.1" or "1.4.1").
    public static bool TagMatches(string? releaseTag, string version)
        => releaseTag == "v" + version || releaseTag == version;

    // Signed version strictly greater than the running one (FileVersion "1.4.0.0" or "1.4.0").
    // An unparsable current version counts as "not newer" (fail closed).
    public static bool IsNewer(string signedVersion, string? currentVersion)
    {
        if (!Version.TryParse(signedVersion, out var signed)) return false;
        string cur = (currentVersion ?? "").Trim();
        if (cur.StartsWith("v", StringComparison.OrdinalIgnoreCase)) cur = cur.Substring(1);
        if (!Version.TryParse(cur, out var current)) return false;
        var current3 = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return signed > current3;
    }

    public static string Describe(UpdateCheck result) => result switch
    {
        UpdateCheck.Ok => "The update was verified.",
        UpdateCheck.NoTrustedKey => "This build of Racks has no update signing key, so it cannot verify updates. Download new versions from the GitHub releases page.",
        UpdateCheck.MissingManifest => "The release has no signed update manifest. The update was not installed.",
        UpdateCheck.MissingSignature => "The release has no update signature. The update was not installed.",
        UpdateCheck.BadSignature => "The update signature is not valid. The update was not installed.",
        UpdateCheck.MalformedManifest => "The update manifest is malformed. The update was not installed.",
        UpdateCheck.WrongRelease => "The signed manifest does not match this release. The update was not installed.",
        UpdateCheck.NotNewer => "The signed update is not newer than this version. The update was not installed.",
        UpdateCheck.SizeMismatch => "The downloaded installer does not have the signed size. It was discarded.",
        UpdateCheck.HashMismatch => "The downloaded installer does not match its signed checksum. It was discarded.",
        _ => "The update could not be verified. It was not installed.",
    };
}
