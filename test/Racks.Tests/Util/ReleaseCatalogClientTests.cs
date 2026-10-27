using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Racks.Infrastructure.Update;
using Racks.UseCases.Updates;
using Racks.Util;

namespace Racks.Tests.Util;

// The release feed and the two small signed assets are read with hard limits: this repository's URL only, a response size cap,
// a timeout and a cancellation token. No network: a fake handler answers.
public class ReleaseCatalogClientTests
{
    private static ReleaseCatalogClient Client(HttpMessageHandler handler, TimeSpan? catalogTimeout = null, TimeSpan? assetTimeout = null)
        => new(new UpdateHttpClientFactory(handler), catalogTimeout, assetTimeout);

    private const string Asset = UpdateRig.Repo + "v1.5.0/Racks-Setup-1.5.0.manifest.json";

    // ---- the URL ---------------------------------------------------------------------------

    [Theory]
    [InlineData("https://api.github.com/repos/duartelcunha/Racks/releases?per_page=100")]
    [InlineData("https://api.github.com/repos/duartelcunha/Racks/releases?per_page=30&x=1")]
    [InlineData("https://api.github.com/repos/someone-else/Racks/releases?per_page=30")]
    [InlineData("http://api.github.com/repos/duartelcunha/Racks/releases?per_page=30")]
    [InlineData("https://api.github.com/repos/duartelcunha/racks/releases?per_page=30")]
    [InlineData("https://evil.example/repos/duartelcunha/Racks/releases?per_page=30")]
    [InlineData("")]
    public async Task Only_this_repositorys_release_feed_can_be_read(string url)
    {
        var http = new FakeHttp();
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCatalogAsync(c, url));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task The_feed_url_constant_is_accepted_and_read()
    {
        var http = new FakeHttp();
        http.Route(ReleaseCatalogClient.ReleasesApi, Encoding.UTF8.GetBytes("[]"));
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        Assert.Equal("[]", await client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));
        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi }, http.Requests);
    }

    // ---- the size cap ----------------------------------------------------------------------

    [Fact]
    public async Task A_feed_of_exactly_the_cap_is_read_and_one_byte_more_is_refused()
    {
        foreach (var (size, ok) in new[] { (ReleaseCatalogClient.MaxCatalogBytes, true), (ReleaseCatalogClient.MaxCatalogBytes + 1, false) })
        {
            var http = new FakeHttp();
            http.Route(ReleaseCatalogClient.ReleasesApi, new byte[size].Select(_ => (byte)' ').ToArray());
            var client = Client(http);
            using var c = client.CreateClient("1.4.0.0");

            if (ok) Assert.Equal(size, (await client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi)).Length);
            else await Assert.ThrowsAsync<InvalidDataException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));
        }
    }

    [Fact]
    public async Task A_feed_that_announces_more_than_the_cap_is_refused_without_reading_it()
    {
        var http = new FakeHttp();
        var endless = new EndlessContent();
        endless.Headers.ContentLength = ReleaseCatalogClient.MaxCatalogBytes + 1;
        http.Route(ReleaseCatalogClient.ReleasesApi, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless });
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));

        Assert.Equal(0, endless.Produced);
    }

    [Fact]
    public async Task A_feed_that_never_ends_is_cut_off_just_past_the_cap()
    {
        var http = new FakeHttp();
        var endless = new EndlessContent();
        http.Route(ReleaseCatalogClient.ReleasesApi, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless });
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));

        Assert.Equal("The release list is too large.", ex.Message);
        Assert.True(endless.Produced <= ReleaseCatalogClient.MaxCatalogBytes + 4096, $"read {endless.Produced} bytes");
    }

    [Fact]
    public async Task A_server_error_for_the_feed_throws()
    {
        var http = new FakeHttp();
        http.Route(ReleaseCatalogClient.ReleasesApi, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));
    }

    [Fact]
    public async Task Small_assets_keep_their_caps_and_fail_to_null()
    {
        var http = new FakeHttp();
        http.Route(Asset, new byte[UpdateManifest.MaxManifestBytes]);
        http.Route(Asset + "-big", new byte[UpdateManifest.MaxManifestBytes + 1]);
        http.Route(Asset + "-404", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        http.Route(Asset + "-boom", _ => throw new HttpRequestException("reset"));
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");

        Assert.Equal(UpdateManifest.MaxManifestBytes, (await client.GetSmallAssetAsync(c, Asset, UpdateManifest.MaxManifestBytes))!.Length);
        Assert.Null(await client.GetSmallAssetAsync(c, Asset + "-big", UpdateManifest.MaxManifestBytes));
        Assert.Null(await client.GetSmallAssetAsync(c, Asset + "-404", UpdateManifest.MaxManifestBytes));
        Assert.Null(await client.GetSmallAssetAsync(c, Asset + "-boom", UpdateManifest.MaxManifestBytes));
    }

    // ---- time and cancellation ----------------------------------------------------------------

    [Fact]
    public async Task A_feed_that_does_not_answer_times_out()
    {
        var hang = new HangingHandler();
        var client = Client(hang, catalogTimeout: TimeSpan.FromMilliseconds(80));
        using var c = client.CreateClient("1.4.0.0");

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi));

        Assert.Equal(1, hang.Requests);
    }

    [Fact]
    public async Task A_small_asset_that_does_not_answer_times_out_to_null()
    {
        var client = Client(new HangingHandler(), assetTimeout: TimeSpan.FromMilliseconds(80));
        using var c = client.CreateClient("1.4.0.0");

        Assert.Null(await client.GetSmallAssetAsync(c, Asset, UpdateManifest.MaxManifestBytes));
    }

    [Fact]
    public async Task Cancelling_stops_both_reads_and_is_not_mistaken_for_a_timeout()
    {
        var client = Client(new HangingHandler());
        using var c = client.CreateClient("1.4.0.0");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi, cts.Token));
        using var cts2 = new CancellationTokenSource();
        cts2.CancelAfter(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetSmallAssetAsync(c, Asset, 1024, cts2.Token));
    }

    [Fact]
    public async Task Requests_carry_the_user_agent_given()
    {
        var http = new FakeHttp();
        http.Route(ReleaseCatalogClient.ReleasesApi, Encoding.UTF8.GetBytes("[]"));
        var client = Client(http);
        using var c = client.CreateClient("1.4.0.0");
        await client.GetCatalogAsync(c, ReleaseCatalogClient.ReleasesApi);
        Assert.Equal("Racks/1.4.0.0", http.UserAgents.Single());
    }
}
