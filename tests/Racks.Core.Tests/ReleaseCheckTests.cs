using System.Net;
using Racks.Core;
using Xunit;

namespace Racks.Tests.Core;

public sealed class ReleaseCheckTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    private static HttpClient Client(HttpStatusCode status, string body = "") => new(new Handler((_, _) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) })));

    [Fact]
    public async Task ReadsStableVersionFromOfficialEndpointWithoutFollowingAssetLinks()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(ReleaseCheck.LatestReleaseApi, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/vnd.github+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"tag_name":"v1.1.4","draft":false,"prerelease":false,"html_url":"https://untrusted.invalid","assets":[{"browser_download_url":"https://untrusted.invalid/setup.exe"}]}""") });
        }));
        Assert.Equal("v1.1.4", await ReleaseCheck.LatestStableVersionAsync(client));
        Assert.Equal("https://github.com/duartelcunha/Racks/releases", ReleaseCheck.ReleasesUrl);
    }

    [Fact]
    public async Task NoPublishedReleaseHasAnExplicitResult()
    {
        using var client = Client(HttpStatusCode.NotFound);
        Assert.Null(await ReleaseCheck.LatestStableVersionAsync(client));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NetworkErrorsAreNotReportedAsUpToDate(HttpStatusCode status)
    {
        using var client = Client(status);
        await Assert.ThrowsAsync<HttpRequestException>(() => ReleaseCheck.LatestStableVersionAsync(client));
    }

    [Theory]
    [InlineData("""{"tag_name":"v2.0.0","draft":true,"prerelease":false}""")]
    [InlineData("""{"tag_name":"v2.0.0","draft":false,"prerelease":true}""")]
    [InlineData("""{"tag_name":"https://untrusted.invalid","draft":false,"prerelease":false}""")]
    [InlineData("""{}""")]
    public async Task IncompleteOrUnexpectedMetadataIsRejected(string body)
    {
        using var client = Client(HttpStatusCode.OK, body);
        await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseCheck.LatestStableVersionAsync(client));
    }

    [Fact]
    public async Task CancellationStopsTheRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReleaseCheck.LatestStableVersionAsync(client, cancellation.Token));
    }
}
