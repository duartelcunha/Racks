using System.Net;
using System.Text.Json;

namespace Racks.Core;

public static class ReleaseCheck
{
    public const string ReleasesUrl = "https://github.com/duartelcunha/Racks/releases";
    public const string LatestReleaseApi = "https://api.github.com/repos/duartelcunha/Racks/releases/latest";

    // Discovery only. GitHub metadata never authorizes a download or installation.
    public static async Task<string?> LatestStableVersionAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("Racks-UpdateCheck/2.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var release = json.RootElement;
        if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("GitHub did not return a stable release.");
        var version = tag.GetString()!;
        if (version.Length > 64 || !Version.TryParse(version.TrimStart('v', 'V'), out _))
            throw new InvalidDataException("The release version could not be read.");
        return version;
    }
}
