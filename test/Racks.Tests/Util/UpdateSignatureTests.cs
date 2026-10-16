using System.IO;
using System.Security.Cryptography;
using System.Text;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-UPDATE-1: an installer is only launched when its manifest is signed by the embedded key,
// names this release, is newer than what runs, and matches the downloaded bytes. Every key here is
// ephemeral and in memory; no private key is ever written to disk.
public class UpdateSignatureTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string PublicKey => UpdateSignature.ExportPublicKey(_key);

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
    }

    private static byte[] Installer(int size = 4096, byte seed = 7)
    {
        var bytes = new byte[size];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 + seed);
        return bytes;
    }

    private static UpdateManifest ManifestFor(string version, byte[] installer)
        => UpdateManifest.ForInstaller(version, SHA256.HashData(installer), installer.Length);

    private (byte[] manifest, string sig) Signed(string version, byte[] installer, ECDsa? key = null)
    {
        byte[] m = ManifestFor(version, installer).ToCanonicalBytes();
        return (m, UpdateSignature.Sign(m, key ?? _key));
    }

    // --- canonical manifest -------------------------------------------------------------------

    [Fact]
    public void Canonical_manifest_has_fixed_field_order_and_no_whitespace()
    {
        var m = new UpdateManifest("Racks", "1.4.1", "Racks-Setup-1.4.1.exe", new string('a', 64), 123);
        Assert.Equal(
            "{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"" + new string('a', 64) + "\",\"size\":123}",
            Encoding.UTF8.GetString(m.ToCanonicalBytes()));
    }

    [Fact]
    public void Canonical_manifest_round_trips()
    {
        var m = ManifestFor("2.0.0", Installer());
        Assert.True(UpdateManifest.TryParseCanonical(m.ToCanonicalBytes(), out var parsed));
        Assert.Equal(m, parsed);
    }

    [Theory]
    // whitespace, reordering, extra field, escapes: same meaning, not the canonical bytes
    [InlineData("{ \"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"version\":\"1.4.1\",\"product\":\"Racks\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5,\"x\":1}")]
    [InlineData("{\"product\":\"R\\u0061cks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}\n")]
    // wrong values
    [InlineData("{\"product\":\"Other\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"v1.4.1\",\"fileName\":\"Racks-Setup-v1.4.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.04.1\",\"fileName\":\"Racks-Setup-1.04.1.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.2.exe\",\"sha256\":\"HASH\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"UPPER\",\"size\":5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":0}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":-5}")]
    [InlineData("{\"product\":\"Racks\",\"version\":\"1.4.1\",\"fileName\":\"Racks-Setup-1.4.1.exe\",\"sha256\":\"HASH\",\"size\":\"5\"}")]
    [InlineData("[]")]
    [InlineData("")]
    public void Non_canonical_or_invalid_manifests_are_rejected(string json)
    {
        json = json.Replace("HASH", new string('a', 64)).Replace("UPPER", new string('A', 64));
        Assert.False(UpdateManifest.TryParseCanonical(Encoding.UTF8.GetBytes(json), out var m));
        Assert.Null(m);
    }

    [Theory]
    [InlineData("1.4.1", true)]
    [InlineData("0.0.1", true)]
    [InlineData("10.20.30", true)]
    [InlineData("v1.4.1", false)]
    [InlineData("1.4", false)]
    [InlineData("1.4.1.0", false)]
    [InlineData("1.4.1-beta", false)]
    [InlineData("1.4.1\n", false)]
    [InlineData("01.4.1", false)]
    public void Version_format_is_strict(string version, bool valid)
        => Assert.Equal(valid, UpdateManifest.IsValidVersion(version));

    // --- signature + release binding -----------------------------------------------------------

    [Fact]
    public void A_valid_signed_manifest_for_a_newer_release_is_accepted()
    {
        var installer = Installer();
        var (m, sig) = Signed("1.5.0", installer);
        Assert.Equal(UpdateCheck.Ok, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0.0", out var manifest));
        Assert.NotNull(manifest);
        using var stream = new MemoryStream(installer);
        Assert.Equal(UpdateCheck.Ok, UpdateVerifier.VerifyInstaller(manifest!, stream));
    }

    [Fact]
    public void A_tampered_installer_is_rejected()
    {
        var installer = Installer();
        var (m, sig) = Signed("1.5.0", installer);
        Assert.Equal(UpdateCheck.Ok, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0", out var manifest));
        var evil = (byte[])installer.Clone();
        evil[100] ^= 0xFF; // same size, different bytes
        using var stream = new MemoryStream(evil);
        Assert.Equal(UpdateCheck.HashMismatch, UpdateVerifier.VerifyInstaller(manifest!, stream));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void An_installer_of_the_wrong_size_is_rejected(int delta)
    {
        var installer = Installer();
        var manifest = ManifestFor("1.5.0", installer);
        using var stream = new MemoryStream(Installer(installer.Length + delta));
        Assert.Equal(UpdateCheck.SizeMismatch, UpdateVerifier.VerifyInstaller(manifest, stream));
    }

    [Fact]
    public void A_tampered_manifest_is_rejected()
    {
        var installer = Installer();
        var (m, sig) = Signed("1.5.0", installer);
        // Swap in the hash of another installer but keep the original signature.
        var evil = ManifestFor("1.5.0", Installer(seed: 99)).ToCanonicalBytes();
        Assert.Equal(UpdateCheck.BadSignature, UpdateVerifier.VerifyManifest(evil, sig, PublicKey, "v1.5.0", "1.4.0", out var manifest));
        Assert.Null(manifest);

        var flipped = (byte[])m.Clone();
        flipped[^3] ^= 0x01;
        Assert.Equal(UpdateCheck.BadSignature, UpdateVerifier.VerifyManifest(flipped, sig, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Fact]
    public void A_signature_from_another_key_is_rejected()
    {
        var (m, sig) = Signed("1.5.0", Installer(), _otherKey);
        Assert.Equal(UpdateCheck.BadSignature, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_signature_is_rejected(string? sig)
    {
        var (m, _) = Signed("1.5.0", Installer());
        Assert.Equal(UpdateCheck.MissingSignature, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Fact]
    public void A_missing_manifest_is_rejected()
    {
        var (_, sig) = Signed("1.5.0", Installer());
        Assert.Equal(UpdateCheck.MissingManifest, UpdateVerifier.VerifyManifest(null, sig, PublicKey, "v1.5.0", "1.4.0", out _));
        Assert.Equal(UpdateCheck.MissingManifest, UpdateVerifier.VerifyManifest(Array.Empty<byte>(), sig, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void A_garbled_signature_is_rejected(string sig)
    {
        var (m, _) = Signed("1.5.0", Installer());
        Assert.Equal(UpdateCheck.BadSignature, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Fact]
    public void A_der_encoded_signature_is_rejected()
    {
        // Only the fixed 64-byte r||s form is accepted.
        var (m, _) = Signed("1.5.0", Installer());
        string der = Convert.ToBase64String(_key.SignData(m, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        Assert.Equal(UpdateCheck.BadSignature, UpdateVerifier.VerifyManifest(m, der, PublicKey, "v1.5.0", "1.4.0", out _));
    }

    [Fact]
    public void An_old_signed_manifest_cannot_be_replayed_under_a_newer_tag()
    {
        // 1.3.0 was genuinely signed. An attacker with GitHub write access re-publishes it (and its
        // .sig) as the "v1.5.0" release: the signed version does not name this release.
        var (m, sig) = Signed("1.3.0", Installer());
        Assert.Equal(UpdateCheck.WrongRelease, UpdateVerifier.VerifyManifest(m, sig, PublicKey, "v1.5.0", "1.4.0", out var manifest));
        Assert.Null(manifest);
    }

    [Theory]
    [InlineData("1.3.0", "v1.3.0", "1.4.0.0")] // downgrade under its own tag
    [InlineData("1.4.0", "v1.4.0", "1.4.0.0")] // same version
    [InlineData("1.5.0", "v1.5.0", "garbage")] // unknown current version: fail closed
    public void A_signed_manifest_that_is_not_newer_is_rejected(string version, string tag, string current)
    {
        var (m, sig) = Signed(version, Installer());
        Assert.Equal(UpdateCheck.NotNewer, UpdateVerifier.VerifyManifest(m, sig, PublicKey, tag, current, out _));
    }

    [Theory]
    [InlineData("v1.5.0", true)]
    [InlineData("1.5.0", true)]
    [InlineData("v1.5.0-rc1", false)]
    [InlineData("V1.5.0", false)]
    [InlineData("v1.5", false)]
    [InlineData(null, false)]
    public void The_release_tag_must_name_the_signed_version(string? tag, bool ok)
        => Assert.Equal(ok, UpdateVerifier.TagMatches(tag, "1.5.0"));

    // --- trusted key ---------------------------------------------------------------------------

    [Fact]
    public void The_placeholder_key_refuses_every_update()
    {
        var (m, sig) = Signed("9.9.9", Installer());
        Assert.False(UpdateTrust.IsConfigured(UpdateTrust.Placeholder));
        Assert.Equal(UpdateCheck.NoTrustedKey, UpdateVerifier.VerifyManifest(m, sig, UpdateTrust.Placeholder, "v9.9.9", "1.0.0", out var manifest));
        Assert.Null(manifest);
    }

    [Fact]
    public void The_embedded_key_is_the_placeholder_or_a_valid_p256_key()
    {
        string key = UpdateTrust.PublicKeySpkiBase64;
        Assert.True(key == UpdateTrust.Placeholder || UpdateTrust.IsConfigured(key),
            "UpdateTrust.PublicKeySpkiBase64 must be the base64 SPKI printed by 'RacksSigner keygen'.");
    }

    [Fact]
    public void An_update_signed_by_any_key_but_the_embedded_one_is_refused()
    {
        // With the placeholder this refuses everything; with the real key it refuses this test key.
        var (m, sig) = Signed("9.9.9", Installer());
        var result = UpdateVerifier.VerifyManifest(m, sig, UpdateTrust.PublicKeySpkiBase64, "v9.9.9", "1.0.0", out var manifest);
        Assert.Contains(result, new[] { UpdateCheck.NoTrustedKey, UpdateCheck.BadSignature });
        Assert.Null(manifest);
    }

    [Fact]
    public void Keys_other_than_p256_are_not_accepted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(UpdateSignature.TryImportPublicKey(UpdateSignature.ExportPublicKey(p384)));
        Assert.Throws<ArgumentException>(() => UpdateSignature.Sign(new byte[] { 1 }, p384));
        using var rsa = RSA.Create(2048);
        Assert.Null(UpdateSignature.TryImportPublicKey(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())));
        Assert.Null(UpdateSignature.TryImportPublicKey("not base64"));
        Assert.False(UpdateTrust.IsConfigured(null));
    }

    [Fact]
    public void A_real_p256_key_is_accepted_as_configured()
        => Assert.True(UpdateTrust.IsConfigured(PublicKey));

    [Fact]
    public void Every_check_result_has_a_user_facing_message()
    {
        foreach (UpdateCheck r in Enum.GetValues<UpdateCheck>())
            Assert.False(string.IsNullOrWhiteSpace(UpdateVerifier.Describe(r)));
    }
}
