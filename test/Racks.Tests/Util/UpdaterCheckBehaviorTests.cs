using Racks.UseCases.Updates;
using Racks.Infrastructure.Update;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-UPDATE-1, check side: what the updater fetches, what it offers and what it says, driven through
// the real code with a fake server and fake toasts (no network, no source scraping).
public class UpdaterCheckBehaviorTests
{
    private const string Hostile = "EVIL 6.6.6 http://evil.example/x https://github.com/duartelcunha/Racks/releases <script>";

    private static void AssertNotOffered(UpdateRig rig)
    {
        Assert.Empty(rig.Notifier.Offers);
        Assert.Empty(rig.Launcher.Started);
        Assert.Equal(0, rig.Shutdown.Count);
        Assert.DoesNotContain(rig.Http.Requests, r => r.EndsWith(".exe", StringComparison.Ordinal));
        Assert.Empty(rig.Downloads());
    }

    private static void AssertNotVerifiedMessage(UpdateRig rig)
    {
        var plain = Assert.Single(rig.Notifier.Plain);
        Assert.Equal((UpdateMessages.NotVerifiedHeader, UpdateMessages.NotVerifiedBody), plain);
        Assert.DoesNotMatch(@"\d", rig.Notifier.AllText);
        foreach (var banned in new[] { "http", "github", "releases page", "download", "www." })
            Assert.DoesNotContain(banned, rig.Notifier.AllText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertInstallRefusedAfterAsync(UpdateRig rig)
    {
        await rig.Service.InstallAsync();
        Assert.Contains(("Update blocked", "This update can't be verified, so Racks won't install it. Check for updates again."), rig.Notifier.Plain);
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Downloads());
    }

    // ---- no key: contacts nobody ----------------------------------------------------------

    [Theory]
    [InlineData(UpdateTrust.Placeholder)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public async Task A_build_without_a_real_update_key_contacts_nobody_and_says_so_when_asked(string key)
    {
        using var rig = new UpdateRig();
        rig.Build(publicKey: key);
        rig.Serve();

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: true);

        Assert.Empty(rig.Http.Requests);
        Assert.Equal((UpdateMessages.NoKeyHeader, UpdateMessages.NoKeyBody), Assert.Single(rig.Notifier.Plain));
        Assert.Empty(rig.Notifier.Offers);
    }

    [Fact]
    public async Task A_build_without_a_key_stays_silent_on_the_automatic_check()
    {
        using var rig = new UpdateRig();
        rig.Build(publicKey: UpdateTrust.Placeholder);

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: false);

        Assert.Empty(rig.Http.Requests);
        Assert.Empty(rig.Notifier.Messages);
    }

    // ---- what is requested ----------------------------------------------------------------

    [Fact]
    public async Task A_check_reads_the_release_feed_then_only_the_manifest_and_signature()
    {
        using var rig = new UpdateRig();
        rig.Build();

        await rig.CheckAsync();

        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi, rig.ManifestAsset, rig.SignatureAsset }, rig.Http.Requests);
        Assert.All(rig.Http.UserAgents, ua => Assert.Equal("Racks/" + UpdateRig.Running, ua));
        Assert.Empty(rig.Downloads());
    }

    // ---- the offer ------------------------------------------------------------------------

    [Fact]
    public async Task A_verified_release_is_offered_with_only_the_signed_version()
    {
        using var rig = new UpdateRig { ReleaseName = Hostile, ReleaseBody = Hostile };
        rig.Build();

        await rig.CheckAsync();

        var (header, body) = Assert.Single(rig.Notifier.Offers);
        Assert.Equal("🚀 Racks 1.5.0 is available", header);
        Assert.Equal("A signed update is ready to install. See what's new on the releases page.", body);
        foreach (var leak in new[] { "EVIL", "6.6.6", "evil.example", "<script>" })
            Assert.DoesNotContain(leak, rig.Notifier.AllText);
        Assert.Empty(rig.Notifier.Plain);
    }

    [Fact]
    public async Task A_newer_unverified_release_is_never_named_when_an_older_signed_one_is_offered()
    {
        using var rig = new UpdateRig();
        string b = UpdateRig.Repo + "v1.9.0/";
        rig.ExtraReleaseJson = JsonSerializer.Serialize(new
        {
            tag_name = "v1.9.0",
            name = Hostile,
            body = Hostile,
            draft = false,
            prerelease = false,
            assets = new object[]
            {
                new { name = "Racks-Setup-1.9.0.exe", browser_download_url = b + "Racks-Setup-1.9.0.exe" },
                new { name = "Racks-Setup-1.9.0.manifest.json", browser_download_url = b + "Racks-Setup-1.9.0.manifest.json" },
                new { name = "Racks-Setup-1.9.0.manifest.json.sig", browser_download_url = b + "Racks-Setup-1.9.0.manifest.json.sig" },
            },
        });
        rig.Build();

        await rig.CheckAsync();

        Assert.Equal("🚀 Racks 1.5.0 is available", Assert.Single(rig.Notifier.Offers).Header);
        foreach (var leak in new[] { "1.9.0", "EVIL", "6.6.6", "evil.example" })
            Assert.DoesNotContain(leak, rig.Notifier.AllText);
        Assert.DoesNotContain(rig.Http.Requests, r => r.EndsWith(".exe", StringComparison.Ordinal));
    }

    // ---- nothing to offer -----------------------------------------------------------------

    [Theory]
    [InlineData("v1.4.0")]   // the running version
    [InlineData("v1.3.9")]   // a downgrade
    public async Task The_same_or_an_older_release_is_not_offered(string tag)
    {
        using var rig = new UpdateRig { Tag = tag, ManifestVersion = tag[1..] };
        rig.Build();

        await rig.CheckAsync();

        Assert.Equal(("You are up to date!", "There is no available update."), Assert.Single(rig.Notifier.Plain));
        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi }, rig.Http.Requests);
        AssertNotOffered(rig);
    }

    [Fact]
    public async Task Up_to_date_is_silent_on_the_automatic_check()
    {
        using var rig = new UpdateRig { Tag = "v1.4.0", ManifestVersion = "1.4.0" };
        rig.Build();
        await rig.CheckAsync(showToastIfNoUpdate: false);
        Assert.Empty(rig.Notifier.Messages);
    }

    // ---- refusals: nothing verified, nothing named, nothing downloaded ---------------------

    private static readonly byte[] Padding = Encoding.ASCII.GetBytes(new string(' ', 1025));

    public static TheoryData<string> BadSets() => new()
    {
        "signature by another key", "signature is garbage text", "signature is DER not P1363", "signature wrong length",
        "signature empty", "signature over cap", "signature 404", "manifest 404", "manifest garbled", "manifest not canonical",
        "manifest over cap", "manifest for another version", "genuine old manifest replayed under a newer tag",
        "manifest signed but sha256 field not hex",
    };

    private static void Break(UpdateRig rig, string what)
    {
        switch (what)
        {
            case "signature by another key": rig.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256); break;
            case "signature is garbage text": rig.SignatureBytesOverride = Encoding.ASCII.GetBytes("!!!not base64!!!"); break;
            case "signature is DER not P1363":
                rig.SignatureBytesOverride = Encoding.ASCII.GetBytes(Convert.ToBase64String(
                    rig.Key.SignData(rig.Manifest.ToCanonicalBytes(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));
                break;
            case "signature wrong length": rig.SignatureBytesOverride = Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[63])); break;
            case "signature empty": rig.SignatureBytesOverride = Array.Empty<byte>(); break;
            case "signature over cap": rig.SignatureBytesOverride = Padding; break;
            case "signature 404": rig.RouteOverrides[rig.SignatureAsset] = _ => new HttpResponseMessage(HttpStatusCode.NotFound); break;
            case "manifest 404": rig.RouteOverrides[rig.ManifestAsset] = _ => new HttpResponseMessage(HttpStatusCode.NotFound); break;
            case "manifest garbled": rig.ManifestBytesOverride = Encoding.UTF8.GetBytes("{\"product\":"); break;
            case "manifest not canonical":
                // Valid JSON with the right values, validly signed, but not the canonical encoding.
                rig.ManifestBytesOverride = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rig.Manifest.ToCanonicalBytes()).Replace(",", ", "));
                break;
            case "manifest over cap": rig.ManifestBytesOverride = Padding; break;
            case "manifest for another version":
                rig.ManifestOverride = UpdateManifest.ForInstaller("1.7.0", SHA256.HashData(rig.SignedInstaller), rig.SignedInstaller.Length); break;
            case "genuine old manifest replayed under a newer tag":
                // A really signed 1.5.0 manifest (same key) served as the manifest of the v1.6.0 release.
                rig.Tag = "v1.6.0"; rig.ManifestVersion = "1.6.0";
                rig.ManifestOverride = UpdateManifest.ForInstaller("1.5.0", SHA256.HashData(rig.SignedInstaller), rig.SignedInstaller.Length); break;
            case "manifest signed but sha256 field not hex":
                rig.ManifestBytesOverride = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rig.Manifest.ToCanonicalBytes()).Replace(rig.Manifest.Sha256, new string('z', 64))); break;
            default: throw new ArgumentException(what);
        }
    }

    [Theory]
    [MemberData(nameof(BadSets))]
    public async Task A_release_whose_manifest_or_signature_does_not_verify_is_not_offered_or_named(string what)
    {
        using var rig = new UpdateRig();
        Break(rig, what);
        rig.Build();

        await rig.CheckAsync();

        AssertNotVerifiedMessage(rig);
        AssertNotOffered(rig);
        await AssertInstallRefusedAfterAsync(rig);
    }

    [Fact]
    public async Task An_unverified_release_is_silent_on_the_automatic_check_and_still_blocks_install()
    {
        using var rig = new UpdateRig { SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256) };
        rig.Build();

        await rig.CheckAsync(showToastIfNoUpdate: false);

        Assert.Empty(rig.Notifier.Messages);
        await AssertInstallRefusedAfterAsync(rig);
    }

    [Fact]
    public async Task A_later_failed_check_withdraws_an_earlier_offer()
    {
        using var rig = new UpdateRig();
        rig.Build();
        await rig.CheckAsync();
        Assert.Single(rig.Notifier.Offers);

        rig.SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);   // the same release now carries a bad signature
        await rig.CheckAsync();
        await rig.Service.InstallAsync();

        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Downloads());
        Assert.Equal("Update blocked", rig.Notifier.Plain.Last().Header);
    }

    [Fact]
    public async Task A_release_without_the_manifest_or_signature_asset_is_not_offered()
    {
        foreach (bool noManifest in new[] { true, false })
        {
            using var rig = new UpdateRig { OmitManifest = noManifest, OmitSignature = !noManifest };
            rig.Build();
            await rig.CheckAsync();
            AssertNotVerifiedMessage(rig);
            AssertNotOffered(rig);
        }
    }

    public static TheoryData<string, string> BadUrls()
    {
        var data = new TheoryData<string, string>();
        foreach (var asset in new[] { "installer", "manifest", "signature" })
        {
            data.Add(asset, "http://github.com/duartelcunha/Racks/releases/download/v1.5.0/{n}");
            data.Add(asset, "https://evil.example/duartelcunha/Racks/releases/download/v1.5.0/{n}");
            data.Add(asset, "https://github.com.evil.example/duartelcunha/Racks/releases/download/v1.5.0/{n}");
            data.Add(asset, "https://github.com/someone-else/Racks/releases/download/v1.5.0/{n}");
            data.Add(asset, "https://github.com/duartelcunha/Racks/archive/refs/heads/{n}");
            data.Add(asset, "https://github.com/duartelcunha/Racks/releases/download/v1.5.0/other-file.bin");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(BadUrls))]
    public async Task An_asset_outside_the_https_github_release_path_is_refused_before_any_download(string asset, string urlTemplate)
    {
        using var rig = new UpdateRig();
        string name = asset switch { "installer" => "Racks-Setup-1.5.0.exe", "manifest" => "Racks-Setup-1.5.0.manifest.json", _ => "Racks-Setup-1.5.0.manifest.json.sig" };
        string url = urlTemplate.Replace("{n}", name);
        if (asset == "installer") rig.InstallerUrl = url;
        else if (asset == "manifest") rig.ManifestUrl = url;
        else rig.SignatureUrl = url;
        rig.Build();

        await rig.CheckAsync();

        // Only the release feed was read: no URL taken from the JSON was fetched.
        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi }, rig.Http.Requests);
        AssertNotVerifiedMessage(rig);
        AssertNotOffered(rig);
        await AssertInstallRefusedAfterAsync(rig);
    }

    // ---- bounded reads of the small assets ------------------------------------------------

    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    public async Task A_small_asset_that_never_ends_is_cut_off_at_the_cap(string which)
    {
        using var rig = new UpdateRig();
        var endless = new EndlessContent();
        rig.Build();
        rig.Serve();
        string url = which == "manifest" ? rig.ManifestAsset : rig.SignatureAsset;
        rig.RouteOverrides[url] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless };

        await rig.CheckAsync();

        Assert.True(endless.Produced <= UpdateManifest.MaxManifestBytes + 4096, $"read {endless.Produced} bytes");
        AssertNotVerifiedMessage(rig);
        AssertNotOffered(rig);
    }

    [Fact]
    public async Task A_small_asset_that_announces_too_much_is_refused_without_reading_it()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.RouteOverrides[rig.ManifestAsset] = _ =>
        {
            var content = new StringContent(new string('x', 10));
            content.Headers.ContentLength = 5000;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };

        await rig.CheckAsync();

        AssertNotVerifiedMessage(rig);
        AssertNotOffered(rig);
    }

    // ---- failures of the check itself -----------------------------------------------------

    [Fact]
    public async Task A_failed_check_the_user_asked_for_says_so_and_the_automatic_one_stays_quiet()
    {
        foreach (bool asked in new[] { true, false })
        {
            using var rig = new UpdateRig();
            rig.Build();
            rig.Http.Route(ReleaseCatalogClient.ReleasesApi, _ => throw new HttpRequestException("offline"));

            await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, asked);

            if (asked) Assert.Equal(("Failed to check for updates.", "offline"), Assert.Single(rig.Notifier.Plain));
            else Assert.Empty(rig.Notifier.Messages);
            AssertNotOffered(rig);
        }
    }

    [Fact]
    public async Task A_release_feed_that_is_not_json_is_a_failed_check()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Http.Route(ReleaseCatalogClient.ReleasesApi, Encoding.UTF8.GetBytes("<html>rate limited</html>"));

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, true);

        Assert.Equal("Failed to check for updates.", Assert.Single(rig.Notifier.Plain).Header);
        AssertNotOffered(rig);
    }

    [Fact]
    public async Task A_release_feed_that_is_not_a_list_offers_nothing()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Http.Route(ReleaseCatalogClient.ReleasesApi, Encoding.UTF8.GetBytes("{\"message\":\"API rate limit exceeded\"}"));

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, true);

        Assert.Equal(("You are up to date!", "There is no available update."), Assert.Single(rig.Notifier.Plain));
        AssertNotOffered(rig);
    }
}
