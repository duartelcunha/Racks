using Racks.UseCases.Updates;
using System.Text.Json;
using Racks.Util;

namespace Racks.Tests.Util;

// The updater reads /releases (first page) and offers the newest correctly signed release that is
// newer than what runs, so one broken or tampered release cannot hide an older good one.
public class UpdaterReleaseScanTests
{
    private static object Rel(string tag, bool signed = true, bool draft = false, bool prerelease = false)
    {
        string v = tag.StartsWith('v') ? tag.Substring(1) : tag;
        string b = $"https://github.com/duartelcunha/Racks/releases/download/{tag}/";
        var assets = new List<object>
        {
            new { name = $"Racks-Setup-{v}.exe", browser_download_url = b + $"Racks-Setup-{v}.exe" },
            new { name = "SHA256SUMS.txt", browser_download_url = b + "SHA256SUMS.txt" },
        };
        if (signed)
        {
            assets.Add(new { name = $"Racks-Setup-{v}.manifest.json", browser_download_url = b + $"Racks-Setup-{v}.manifest.json" });
            assets.Add(new { name = $"Racks-Setup-{v}.manifest.json.sig", browser_download_url = b + $"Racks-Setup-{v}.manifest.json.sig" });
        }
        return new { tag_name = tag, name = $"<script>{tag}</script>", body = "anything", draft, prerelease, assets };
    }

    private static JsonElement Releases(params object[] releases) => JsonSerializer.SerializeToElement(releases);

    [Fact]
    public void Signed_releases_newer_than_this_build_are_candidates_newest_first()
    {
        var list = ReleaseManifestValidator.SelectCandidates(
            Releases(Rel("v1.4.1"), Rel("v1.6.0"), Rel("v1.5.0"), Rel("v1.4.0"), Rel("v1.3.0")),
            "1.4.0.0", out var newest);

        Assert.Equal(new[] { "1.6.0", "1.5.0", "1.4.1" }, list.Select(a => a.Version));
        Assert.Equal("1.6.0", newest);
    }

    [Fact]
    public void An_unsigned_newer_release_is_reported_but_not_a_candidate()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(Rel("v2.0.0", signed: false), Rel("v1.5.0")), "1.4.0", out var newest);

        Assert.Equal(new[] { "1.5.0" }, list.Select(a => a.Version));
        Assert.Equal("2.0.0", newest);
    }

    [Fact]
    public void Drafts_prereleases_and_odd_tags_are_ignored()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(
            Rel("v3.0.0", draft: true),
            Rel("v2.5.0", prerelease: true),
            Rel("v2.4.0-rc1"),
            Rel("latest"),
            Rel("v2.0.0")), "1.4.0", out var newest);

        Assert.Equal(new[] { "2.0.0" }, list.Select(a => a.Version));
        Assert.Equal("2.0.0", newest);
    }

    [Fact]
    public void Nothing_newer_means_no_candidates_and_no_version()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(Rel("v1.4.0"), Rel("v1.3.9")), "1.4.0.0", out var newest);
        Assert.Empty(list);
        Assert.Null(newest);
    }

    [Fact]
    public void Only_the_first_page_is_scanned()
    {
        var many = Enumerable.Range(0, ReleaseManifestValidator.MaxReleasesScanned).Select(i => Rel($"v1.0.{i}")).Append(Rel("v9.0.0")).ToArray();
        var list = ReleaseManifestValidator.SelectCandidates(Releases(many), "0.9.0", out var newest);
        Assert.Equal(ReleaseManifestValidator.MaxReleasesScanned, list.Count);
        Assert.Equal($"1.0.{ReleaseManifestValidator.MaxReleasesScanned - 1}", newest);
    }

    [Fact]
    public void A_response_that_is_not_a_list_has_no_candidates()
    {
        var list = ReleaseManifestValidator.SelectCandidates(JsonSerializer.SerializeToElement(Rel("v9.0.0")), "1.0.0", out var newest);
        Assert.Empty(list);
        Assert.Null(newest);
    }

    private static UpdateManifest Manifest(string version) => UpdateManifest.ForInstaller(version, new byte[32], 10);

    [Fact]
    public async Task A_newer_release_that_fails_verification_does_not_hide_an_older_good_one()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(Rel("v1.6.0"), Rel("v1.5.0")), "1.4.0", out _);
        var tried = new List<string>();

        var (offer, _) = await ReleaseManifestValidator.ChooseVerifiedAsync(list, a =>
        {
            tried.Add(a.Version);
            return Task.FromResult(a.Version == "1.6.0"
                ? (UpdateCheck.BadSignature, (UpdateManifest?)null)
                : (UpdateCheck.Ok, (UpdateManifest?)Manifest(a.Version)));
        });

        Assert.Equal(new[] { "1.6.0", "1.5.0" }, tried);
        Assert.NotNull(offer);
        Assert.Equal("1.5.0", offer!.Manifest.Version);
        Assert.Equal("v1.5.0", offer.Tag);
    }

    [Fact]
    public async Task When_nothing_verifies_the_newest_failure_is_reported()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(Rel("v1.6.0"), Rel("v1.5.0")), "1.4.0", out _);
        var (offer, failure) = await ReleaseManifestValidator.ChooseVerifiedAsync(list, a => Task.FromResult(a.Version == "1.6.0"
            ? (UpdateCheck.HashMismatch, (UpdateManifest?)null)
            : (UpdateCheck.BadSignature, (UpdateManifest?)null)));

        Assert.Null(offer);
        Assert.Equal(UpdateCheck.HashMismatch, failure);
    }

    [Fact]
    public async Task A_verified_manifest_for_another_version_is_not_offered()
    {
        var list = ReleaseManifestValidator.SelectCandidates(Releases(Rel("v1.6.0")), "1.4.0", out _);
        var (offer, failure) = await ReleaseManifestValidator.ChooseVerifiedAsync(list, a => Task.FromResult((UpdateCheck.Ok, (UpdateManifest?)Manifest("1.5.0"))));

        Assert.Null(offer);
        Assert.Equal(UpdateCheck.WrongRelease, failure);
    }

    [Fact]
    public async Task No_candidates_means_no_offer()
    {
        var (offer, failure) = await ReleaseManifestValidator.ChooseVerifiedAsync(Array.Empty<ReleaseAssets>(),
            _ => throw new InvalidOperationException("nothing to verify"));
        Assert.Null(offer);
        Assert.Equal(UpdateCheck.MissingManifest, failure);
    }
}
