using System.Net.Http;
using System.Net.Http.Headers;

namespace Racks.Infrastructure.Update;

/// <summary>
/// Makes the HttpClient of one update operation, with the "Racks/&lt;version&gt;" User-Agent GitHub asks for. Production
/// uses the default handler (TLS, automatic redirects: GitHub redirects a release asset to its storage on another host,
/// which is safe because the bytes are checked against the signed size and SHA-256, not against a host). Tests pass a
/// fake <see cref="HttpMessageHandler"/>, which is never disposed here.
/// </summary>
internal sealed class UpdateHttpClientFactory
{
    private readonly HttpMessageHandler? _handler;

    public UpdateHttpClientFactory(HttpMessageHandler? handler = null) => _handler = handler;

    public HttpClient Create(string version)
    {
        var client = _handler != null ? new HttpClient(_handler, disposeHandler: false) : new HttpClient();
        string v = string.IsNullOrWhiteSpace(version) ? "0" : version.Trim();
        try { client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Racks", v)); }
        catch (FormatException) { client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Racks", "0")); }
        return client;
    }
}
