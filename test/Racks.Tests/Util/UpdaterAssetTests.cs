using Racks.UseCases.Updates;
using System.Security.Cryptography;
using System.Text.Json;
using Racks.Util;

namespace Racks.Tests.Util;

public class UpdaterAssetTests
{
    private const string Base = "https://github.com/duartelcunha/Racks/releases/download/v9.9.9/";
    private const string Good = Base + "Racks-Setup-9.9.9.exe";

    private static JsonElement Release(params (string name, string url, long size)[] assets)
    {
        var items = assets.Select(a => new { name = a.name, browser_download_url = a.url, size = a.size });
        return JsonSerializer.SerializeToElement(new { assets = items });
    }

    private static (string, string, long)[] SignedSet(string version = "9.9.9")
    {
        string b = $"https://github.com/duartelcunha/Racks/releases/download/v{version}/";
        return new[]
        {
            ("SHA256SUMS.txt", b + "SHA256SUMS.txt", 5L),
            ($"Racks-Setup-{version}.exe", b + $"Racks-Setup-{version}.exe", 1234L),
            ($"Racks-Setup-{version}.manifest.json", b + $"Racks-Setup-{version}.manifest.json", 150L),
            ($"Racks-Setup-{version}.manifest.json.sig", b + $"Racks-Setup-{version}.manifest.json.sig", 88L),
        };
    }

    [Fact]
    public void Picks_the_installer_manifest_and_signature_by_exact_name()
    {
        Assert.True(ReleaseManifestValidator.TrySelectSignedRelease(Release(SignedSet()), "v9.9.9", out var a));
        Assert.Equal("9.9.9", a.Version);
        Assert.Equal(Good, a.InstallerUrl);
        Assert.Equal(Base + "Racks-Setup-9.9.9.manifest.json", a.ManifestUrl);
        Assert.Equal(Base + "Racks-Setup-9.9.9.manifest.json.sig", a.SignatureUrl);
    }

    [Theory]
    [InlineData("Racks-Setup-9.9.9.exe")]
    [InlineData("Racks-Setup-9.9.9.manifest.json")]
    [InlineData("Racks-Setup-9.9.9.manifest.json.sig")]
    public void A_release_missing_any_signed_asset_is_not_offered(string missing)
    {
        var set = SignedSet().Where(a => a.Item1 != missing).ToArray();
        Assert.False(ReleaseManifestValidator.TrySelectSignedRelease(Release(set), "v9.9.9", out _));
    }

    [Fact]
    public void Assets_of_another_version_do_not_count()
    {
        // The 1.0.0 installer and its manifest sit in the v9.9.9 release: not this release's assets.
        Assert.False(ReleaseManifestValidator.TrySelectSignedRelease(Release(SignedSet("1.0.0")), "v9.9.9", out _));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v9.9")]
    [InlineData("v9.9.9-rc1")]
    [InlineData("v9.9.9/../x")]
    [InlineData("")]
    public void A_tag_that_is_not_a_plain_version_is_not_offered(string tag)
        => Assert.False(ReleaseManifestValidator.TrySelectSignedRelease(Release(SignedSet()), tag, out _));

    [Theory]
    [InlineData("http://github.com/duartelcunha/Racks/releases/download/v1/Racks-Setup-1.exe")]       // not HTTPS
    [InlineData("https://evil.example/duartelcunha/Racks/releases/download/v1/Racks-Setup-1.exe")]    // other host
    [InlineData("https://github.com/someone-else/Racks/releases/download/v1/Racks-Setup-1.exe")]      // other repo
    [InlineData("https://github.com/duartelcunha/Racks/archive/refs/heads/main.zip")]                 // not a release asset
    [InlineData("not a url")]
    public void Rejects_untrusted_download_urls(string url)
    {
        Assert.False(ReleaseManifestValidator.IsTrustedDownloadUrl(url));
        Assert.False(ReleaseManifestValidator.TryFindAsset(Release(("Racks-Setup-1.exe", url, 10)), "Racks-Setup-1.exe", out _));
    }

    [Fact]
    public void A_trusted_url_must_end_in_the_asset_name()
    {
        // Name says installer, URL points at another file of the release.
        var root = Release(("Racks-Setup-9.9.9.exe", Base + "Racks-portable-9.9.9.zip", 10));
        Assert.False(ReleaseManifestValidator.TryFindAsset(root, "Racks-Setup-9.9.9.exe", out _));
    }

    [Fact]
    public void Names_must_match_exactly()
    {
        var root = Release(("racks-setup-9.9.9.exe", Base + "racks-setup-9.9.9.exe", 10),
                           ("setup.exe", Good, 10));
        Assert.False(ReleaseManifestValidator.TryFindAsset(root, "Racks-Setup-9.9.9.exe", out _));
    }

    [Fact]
    public void A_bad_first_asset_does_not_hide_a_good_later_one()
    {
        var root = Release(("Racks-Setup-9.9.9.exe", "https://evil.example/x.exe", 1), ("Racks-Setup-9.9.9.exe", Good, 2));
        Assert.True(ReleaseManifestValidator.TryFindAsset(root, "Racks-Setup-9.9.9.exe", out var url));
        Assert.Equal(Good, url);
    }

    [Fact]
    public void No_assets_property_means_no_update()
        => Assert.False(ReleaseManifestValidator.TrySelectSignedRelease(JsonSerializer.SerializeToElement(new { tag_name = "v1" }), "v9.9.9", out _));

    [Fact]
    public void With_the_placeholder_key_no_update_is_ever_applied()
    {
        // The updater's own decision (embedded key). Signed with a throwaway in-memory key, so it is
        // refused by the placeholder (NoTrustedKey) and by any real embedded key (BadSignature).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var installer = new byte[] { 1, 2, 3, 4 };
        var manifest = UpdateManifest.ForInstaller("9.9.9", SHA256.HashData(installer), installer.Length).ToCanonicalBytes();
        string sig = UpdateSignature.Sign(manifest, key);

        var result = ReleaseManifestValidator.VerifyReleaseManifest(manifest, sig, "v9.9.9", "1.0.0.0", out var verified);

        Assert.Null(verified);
        if (!UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64))
            Assert.Equal(UpdateCheck.NoTrustedKey, result);
        else
            Assert.Equal(UpdateCheck.BadSignature, result);
    }
}
