using System.Text.Json;
using Racks.Util;

namespace Racks.UseCases.Updates;

internal sealed record ReleaseAssets(string Version, string InstallerUrl, string ManifestUrl, string SignatureUrl);

/// <summary>The offer that <see cref="UpdateService"/> keeps after a manifest verified. Install uses nothing else.</summary>
internal sealed record PendingUpdate(string Tag, ReleaseAssets Assets, UpdateManifest Manifest);

/// <summary>
/// The pure rules of INV-UPDATE-1 that decide which release may be offered, with no I/O: which releases of the feed are
/// candidates, which assets and URLs count, which candidate verified. The signature, size and hash maths are
/// <see cref="UpdateVerifier"/> (Util/UpdateManifest.cs, shared with RacksSigner); this class only picks WHAT is verified.
/// </summary>
internal static class ReleaseManifestValidator
{
    // The only host/path the update binary may come from. TLS proves it's really GitHub; this proves it's OUR repo's
    // release asset and not an arbitrary URL from the JSON.
    private const string TrustedAssetHost = "github.com";
    private const string TrustedAssetPathPrefix = "/duartelcunha/Racks/releases/download/";

    /// <summary>Only the first page of the feed is read (newest first, up to 30).</summary>
    internal const int MaxReleasesScanned = 30;

    /// <summary>
    /// From a /releases page: the releases newer than currentVersion that carry the three signed assets, newest version
    /// first. newestVersion is the highest newer version seen at all (signed or not), for the "can't be verified" message;
    /// null when nothing is newer. Drafts, prereleases and tags that are not exactly v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;
    /// are ignored.
    /// </summary>
    internal static List<ReleaseAssets> SelectCandidates(JsonElement releases, string currentVersion, out string? newestVersion)
    {
        newestVersion = null;
        var found = new List<ReleaseAssets>();
        if (releases.ValueKind != JsonValueKind.Array) return found;
        Version? newest = null;
        int seen = 0;
        foreach (var release in releases.EnumerateArray())
        {
            if (++seen > MaxReleasesScanned) break;
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (IsTrue(release, "draft") || IsTrue(release, "prerelease")) continue;
            if (!TryGetTagVersion(release, out string tag, out string version)) continue;
            if (!UpdateVerifier.IsNewer(version, currentVersion)) continue;

            var v = Version.Parse(version);
            if (newest == null || v > newest) { newest = v; newestVersion = version; }
            if (TrySelectSignedRelease(release, tag, out var assets)) found.Add(assets);
        }
        found.Sort((a, b) => Version.Parse(b.Version).CompareTo(Version.Parse(a.Version)));
        return found;
    }

    /// <summary>tag_name must be "v&lt;version&gt;" (or the bare version) with a strictly valid version.</summary>
    internal static bool TryGetTagVersion(JsonElement release, out string tag, out string version)
    {
        tag = version = "";
        if (!release.TryGetProperty("tag_name", out var t) || t.ValueKind != JsonValueKind.String) return false;
        string raw = t.GetString() ?? "";
        string v = raw.StartsWith('v') ? raw.Substring(1) : raw;
        if (!UpdateManifest.IsValidVersion(v) || !UpdateVerifier.TagMatches(raw, v)) return false;
        tag = raw;
        version = v;
        return true;
    }

    private static bool IsTrue(JsonElement o, string name)
        => o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Verify candidates newest first; the first that verifies is offered. Otherwise the reason the newest one failed
    /// (for the log, never for the user: INV-UPDATE-1).
    /// </summary>
    internal static async Task<(PendingUpdate? Offer, UpdateCheck Failure)> ChooseVerifiedAsync(
        IReadOnlyList<ReleaseAssets> candidates, Func<ReleaseAssets, Task<(UpdateCheck, UpdateManifest?)>> verify)
    {
        UpdateCheck? firstFailure = null;
        foreach (var assets in candidates)
        {
            var (check, manifest) = await verify(assets);
            if (check == UpdateCheck.Ok && manifest != null && manifest.Version == assets.Version)
                return (new PendingUpdate("v" + assets.Version, assets, manifest), UpdateCheck.Ok);
            firstFailure ??= check == UpdateCheck.Ok ? UpdateCheck.WrongRelease : check;
        }
        return (null, firstFailure ?? UpdateCheck.MissingManifest);
    }

    /// <summary>
    /// Pick the release's three assets BY EXACT NAME for the tag's version (never assets[0], which an extra asset uploaded
    /// first could hijack) and only on our GitHub release path over HTTPS. A tag that is not "v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;"
    /// has no signed assets.
    /// </summary>
    internal static bool TrySelectSignedRelease(JsonElement root, string releaseTag, out ReleaseAssets assets)
    {
        assets = new ReleaseAssets("", "", "", "");
        string version = releaseTag != null && releaseTag.StartsWith('v') ? releaseTag.Substring(1) : releaseTag ?? "";
        if (!UpdateManifest.IsValidVersion(version)) return false;
        if (!TryFindAsset(root, UpdateManifest.InstallerName(version), out string installer)
            || !TryFindAsset(root, UpdateManifest.ManifestName(version), out string manifest)
            || !TryFindAsset(root, UpdateManifest.SignatureName(version), out string signature))
            return false;
        assets = new ReleaseAssets(version, installer, manifest, signature);
        return true;
    }

    /// <summary>
    /// The first asset with exactly this name whose URL is our trusted release path and ends in that name. A bad asset does
    /// not hide a good later one.
    /// </summary>
    internal static bool TryFindAsset(JsonElement root, string assetName, out string url)
    {
        url = "";
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            string aname = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? (n.GetString() ?? "") : "";
            if (!string.Equals(aname, assetName, StringComparison.Ordinal)) continue;
            string durl = asset.TryGetProperty("browser_download_url", out var d) && d.ValueKind == JsonValueKind.String ? (d.GetString() ?? "") : "";
            if (!IsTrustedDownloadUrl(durl)) continue;
            if (!new Uri(durl).AbsolutePath.EndsWith("/" + assetName, StringComparison.Ordinal)) continue;
            url = durl;
            return true;
        }
        return false;
    }

    /// <summary>
    /// HTTPS + github.com + our repo's /releases/download/ path. The GitHub API always returns such a URL; the 302 to
    /// GitHub's asset storage happens at download time and is covered by TLS. This is transport hygiene only: the
    /// signature and the SHA-256 are what make an installer trusted (a redirect to another host changes nothing there).
    /// </summary>
    internal static bool IsTrustedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        return string.Equals(uri.Host, TrustedAssetHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(TrustedAssetPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The manifest decision with the key compiled into this build (or the one given). While UpdateTrust holds the
    /// placeholder this returns NoTrustedKey for every release.
    /// </summary>
    internal static UpdateCheck VerifyReleaseManifest(byte[]? manifestBytes, string? signatureText, string releaseTag,
                                                      string currentVersion, out UpdateManifest? manifest,
                                                      string? publicKeySpkiBase64 = null)
        => UpdateVerifier.VerifyManifest(manifestBytes, signatureText, publicKeySpkiBase64 ?? UpdateTrust.PublicKeySpkiBase64,
                                         releaseTag, currentVersion, out manifest);
}
