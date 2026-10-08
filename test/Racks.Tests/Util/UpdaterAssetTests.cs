using System.Text.Json;

namespace Racks.Tests.Util;

public class UpdaterAssetTests
{
    private const string Good = "https://github.com/duartelcunha/Racks/releases/download/v9.9.9/Racks-Setup-9.9.9.exe";

    private static JsonElement Release(params (string name, string url, long size)[] assets)
    {
        var items = assets.Select(a => new { name = a.name, browser_download_url = a.url, size = a.size });
        return JsonSerializer.SerializeToElement(new { assets = items });
    }

    [Fact]
    public void Picks_the_installer_by_name_and_reads_its_size()
    {
        var root = Release(("SHA256SUMS.txt", Good.Replace("Racks-Setup-9.9.9.exe", "SHA256SUMS.txt"), 5),
                           ("Racks-Setup-9.9.9.exe", Good, 1234));
        Assert.True(Updater.TrySelectTrustedAsset(root, out var url, out var size));
        Assert.Equal(Good, url);
        Assert.Equal(1234, size);
    }

    [Theory]
    [InlineData("http://github.com/duartelcunha/Racks/releases/download/v1/Racks-Setup-1.exe")]       // not HTTPS
    [InlineData("https://evil.example/duartelcunha/Racks/releases/download/v1/Racks-Setup-1.exe")]    // other host
    [InlineData("https://github.com/someone-else/Racks/releases/download/v1/Racks-Setup-1.exe")]      // other repo
    [InlineData("https://github.com/duartelcunha/Racks/archive/refs/heads/main.zip")]                 // not a release asset
    [InlineData("not a url")]
    public void Rejects_untrusted_download_urls(string url)
    {
        Assert.False(Updater.IsTrustedDownloadUrl(url));
        Assert.False(Updater.TrySelectTrustedAsset(Release(("Racks-Setup-1.exe", url, 10)), out _, out _));
    }

    [Fact]
    public void Ignores_assets_that_are_not_the_installer_even_on_a_trusted_url()
    {
        var root = Release(("Racks-portable-1.0.0.zip", Good.Replace("Racks-Setup-9.9.9.exe", "Racks-portable-1.0.0.zip"), 10),
                           ("setup.exe", Good, 10));
        Assert.False(Updater.TrySelectTrustedAsset(root, out _, out _));
    }

    [Fact]
    public void A_bad_first_asset_does_not_hide_a_good_later_one()
    {
        var root = Release(("Racks-Setup-1.exe", "https://evil.example/x.exe", 1), ("Racks-Setup-2.exe", Good, 2));
        Assert.True(Updater.TrySelectTrustedAsset(root, out var url, out _));
        Assert.Equal(Good, url);
    }

    [Fact]
    public void No_assets_property_means_no_update()
        => Assert.False(Updater.TrySelectTrustedAsset(JsonSerializer.SerializeToElement(new { tag_name = "v1" }), out _, out _));
}
