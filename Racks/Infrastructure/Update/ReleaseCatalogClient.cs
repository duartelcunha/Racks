using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Racks.UseCases.Updates;

namespace Racks.Infrastructure.Update;

/// <summary>
/// Reads the release feed and the two small signed assets (manifest, signature) over HTTP, with hard limits: the feed is
/// read from this repository's URL only, never more than <see cref="MaxCatalogBytes"/>, and each request has a timeout and a
/// cancellation token. It decides nothing about trust (that is the manifest validator and the signature); it only makes sure
/// that what an attacker-controlled response can cost is bounded.
/// </summary>
internal sealed class ReleaseCatalogClient
{
    /// <summary>The ONLY place the feed URL is written. Releases are read from this repository only: the first page (newest first, up to 30).</summary>
    public const string ReleasesApi = "https://api.github.com/repos/duartelcunha/Racks/releases?per_page=30";

    /// <summary>
    /// 30 releases with notes and four assets each are a few hundred KB; 2 MiB leaves room and still stops a hostile or
    /// broken endpoint from filling memory.
    /// </summary>
    public const int MaxCatalogBytes = 2 * 1024 * 1024;

    public static readonly TimeSpan DefaultCatalogTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultAssetTimeout = TimeSpan.FromSeconds(20);

    private readonly UpdateHttpClientFactory _http;
    private readonly TimeSpan _catalogTimeout;
    private readonly TimeSpan _assetTimeout;

    public ReleaseCatalogClient(UpdateHttpClientFactory http, TimeSpan? catalogTimeout = null, TimeSpan? assetTimeout = null)
    {
        _http = http;
        _catalogTimeout = catalogTimeout ?? DefaultCatalogTimeout;
        _assetTimeout = assetTimeout ?? DefaultAssetTimeout;
    }

    public HttpClient CreateClient(string version) => _http.Create(version);

    public static bool IsReleasesApi(string? url) => string.Equals(url, ReleasesApi, StringComparison.Ordinal);

    /// <summary>
    /// The release feed as text. Throws (nothing is fetched) when <paramref name="url"/> is not <see cref="ReleasesApi"/>;
    /// throws on an HTTP error, on a body over <see cref="MaxCatalogBytes"/> and on a timeout.
    /// </summary>
    public async Task<string> GetCatalogAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        if (!IsReleasesApi(url)) throw new ArgumentException("The release feed address is not allowed.", nameof(url));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_catalogTimeout);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxCatalogBytes)
                throw new InvalidDataException("The release list is too large.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            byte[]? body = await ReadBoundedAsync(input, MaxCatalogBytes, timeout.Token);
            if (body == null) throw new InvalidDataException("The release list is too large.");
            using var reader = new StreamReader(new MemoryStream(body), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("GitHub took too long to answer.");
        }
    }

    /// <summary>
    /// A small asset (manifest, signature) with a hard size cap and a timeout. Null on any failure: an HTTP error, a larger
    /// body, a network error or a timeout. The URL must be on the trusted release path (checked here too), otherwise null and no request.
    /// </summary>
    public async Task<byte[]?> GetSmallAssetAsync(HttpClient http, string url, int maxBytes, CancellationToken ct = default)
    {
        // Defense in depth: the use case checked the URL already; nothing off the trusted release path is ever requested.
        if (!ReleaseManifestValidator.IsTrustedDownloadUrl(url)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_assetTimeout);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > maxBytes) return null;
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadBoundedAsync(input, maxBytes, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Debug.WriteLine($"Update asset download failed: {ex.Message}");
            return null;
        }
    }

    // At most maxBytes; null when the stream has more (reading stops one chunk past the cap).
    private static async Task<byte[]?> ReadBoundedAsync(Stream input, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) return null;
        }
        return buffer.ToArray();
    }
}
