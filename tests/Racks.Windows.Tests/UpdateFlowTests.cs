using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Chaos.NaCl;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using Racks.Desktop;
using Xunit;

namespace Racks.Tests.Windows;

public sealed class UpdateFlowTests
{
    [Fact]
    public async Task TrustedFeedDownloadsVerifiedPackageWithoutStartingInstaller()
    {
        await using var fixture = new Feed();
        using var updater = fixture.Updater();
        var installerStarted = false;
        updater.InstallerProcessAboutToStart += (_, _) => { installerStarted = true; return false; };
        var info = await updater.CheckForUpdatesQuietly();
        Assert.Equal(UpdateStatus.UpdateAvailable, info.Status);
        var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.DownloadFinished += (_, path) => completed.TrySetResult(path);
        await updater.InitAndBeginDownload(Assert.Single(info.Updates));
        var path = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(fixture.Package, await File.ReadAllBytesAsync(path));
        Assert.StartsWith(fixture.Root, path);
        Assert.False(installerStarted);
        Assert.True(updater.ShouldKillParentProcessWhenStartingInstaller);
        Assert.Equal(UserInteractionMode.DownloadNoInstall, updater.UserInteractionMode);
        Assert.StartsWith("2.0.0-beta.1", updater.Configuration.InstalledVersion);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("missing")]
    [InlineData("wrong-key")]
    public async Task UntrustedFeedCannotOfferAnUpdate(string failure)
    {
        await using var fixture = new Feed();
        fixture.FeedFailure = failure;
        using var updater = fixture.Updater();
        Assert.Equal(UpdateStatus.CouldNotDetermine, (await updater.CheckForUpdatesQuietly()).Status);
        Assert.Equal(0, fixture.PackageRequests);
    }

    [Fact]
    public async Task AlteredPackageIsRejectedAndNeverBecomesReady()
    {
        await using var fixture = new Feed();
        fixture.Package = Encoding.UTF8.GetBytes("altered payload");
        using var updater = fixture.Updater();
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = false;
        updater.DownloadedFileIsCorrupt += (_, _) => rejected.TrySetResult();
        updater.DownloadFinished += (_, _) => accepted = true;
        await updater.InitAndBeginDownload(Assert.Single((await updater.CheckForUpdatesQuietly()).Updates));
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppCanRetryTheSameUpdateAfterAFailedDownload(bool truncated)
    {
        await using var fixture = new Feed();
        using var session = new Session(Path.Combine(fixture.Root, "profile"), true);
        var updater = fixture.Updater();
        using var service = new UpdateService(session, updater);
        fixture.PackageStatus = truncated ? 200 : 503;
        fixture.ExtraLength = truncated ? 10 : 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.DownloadHadError += (_, _, _) => failed.TrySetResult();
        updater.DownloadFinished += (_, _) => downloaded.TrySetResult();
        await service.CheckAsync();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        fixture.PackageStatus = 200;
        fixture.ExtraLength = 0;
        await service.CheckAsync();
        await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, fixture.PackageRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppCanResumeAfterPausingADownload(bool beforeHeaders)
    {
        await using var fixture = new Feed();
        using var session = new Session(Path.Combine(fixture.Root, "profile"), true);
        var updater = fixture.Updater();
        using var service = new UpdateService(session, updater);
        fixture.HoldPackage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.HoldHeaders = beforeHeaders;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.DownloadCanceled += (_, _) => cancelled.TrySetResult();
        updater.DownloadHadError += (_, _, _) => cancelled.TrySetResult();
        updater.DownloadFinished += (_, _) => downloaded.TrySetResult();
        await service.CheckAsync();
        await fixture.PackageRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        service.Pause();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
        fixture.HoldPackage.SetResult();
        await service.CheckAsync();
        await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, fixture.PackageRequests);
    }

    [Fact]
    public async Task PausingAfterCompletionRetainsTheVerifiedPackage()
    {
        await using var fixture = new Feed();
        using var session = new Session(Path.Combine(fixture.Root, "profile"), true);
        var updater = fixture.Updater();
        using var service = new UpdateService(session, updater);
        var downloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.DownloadFinished += (_, path) => downloaded.TrySetResult(path);
        await service.CheckAsync();
        var package = await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        service.Pause();
        Assert.Equal(fixture.Package, await File.ReadAllBytesAsync(package));
    }

    [Fact]
    public async Task InstallerRechecksSignatureAndRespectsVeto()
    {
        await using var fixture = new Feed();
        using var updater = fixture.Updater();
        var item = Assert.Single((await updater.CheckForUpdatesQuietly()).Updates);
        var packagePath = Path.Combine(fixture.Root, "fixture.exe");
        await File.WriteAllBytesAsync(packagePath, fixture.Package);
        var installerStarted = false;
        updater.InstallerProcessAboutToStart += (_, _) => { installerStarted = true; return false; };
        System.ComponentModel.CancelEventHandler veto = (_, args) => args.Cancel = true;
        updater.PreparingToExit += veto;
        await updater.InstallUpdate(item, packagePath);
        Assert.False(installerStarted);
        updater.PreparingToExit -= veto;
        await File.AppendAllTextAsync(packagePath, "changed after download");
        InstallUpdateFailureReason? failure = null;
        updater.InstallUpdateFailed += (reason, _) => { failure = reason; return true; };
        await updater.InstallUpdate(item, packagePath);
        Assert.Equal(InstallUpdateFailureReason.InvalidSignature, failure);
        Assert.False(installerStarted);
    }

    // Real HTTP + NetSparkle parsing/downloading with ephemeral keys and inert bytes.
    // Never executes a package, changes the user profile, or contacts the internet.
    private sealed class Feed : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task serving;
        private readonly byte[] feed;
        private readonly string signature, wrongSignature;
        private readonly string publicKey;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "RacksUpdaterTests-" + Guid.NewGuid().ToString("N"));
        public string Url { get; }
        public byte[] Package = Encoding.UTF8.GetBytes("Inert update test data. This is not an executable.");
        public int PackageStatus = 200;
        public string FeedFailure = "";
        public int PackageRequests;
        public int ExtraLength;
        public bool HoldHeaders;
        public TaskCompletionSource? HoldPackage;
        public TaskCompletionSource PackageRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Feed()
        {
            Directory.CreateDirectory(Root);
            listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Ed25519.KeyPairFromSeed(out var key, out var secret, RandomNumberGenerator.GetBytes(32));
            publicKey = Convert.ToBase64String(key);
            var packageSignature = Convert.ToBase64String(Ed25519.Sign(Package, secret));
            feed = Encoding.UTF8.GetBytes($"""
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle"><channel><title>Racks fixture</title>
                <item><title>Racks test</title><pubDate>Wed, 16 Sep 2026 12:00:00 GMT</pubDate><enclosure url="{Url}/fixture.exe" sparkle:version="99.0.0" sparkle:os="windows-x64" sparkle:edSignature="{packageSignature}" length="{Package.Length}" type="application/octet-stream" /></item>
                </channel></rss>
                """);
            signature = Convert.ToBase64String(Ed25519.Sign(feed, secret));
            Ed25519.KeyPairFromSeed(out _, out var otherSecret, RandomNumberGenerator.GetBytes(32));
            wrongSignature = Convert.ToBase64String(Ed25519.Sign(feed, otherSecret));
            serving = ServeAsync();
        }

        public SparkleUpdater Updater() => UpdateService.CreateUpdater(Url + "/appcast.xml", publicKey, Path.Combine(Root, "cache"));

        private async Task ServeAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var line = await reader.ReadLineAsync(stop.Token);
                    if (line == null) continue;
                    var path = line.Split(' ')[1];
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                    var status = 200;
                    byte[] body;
                    if (path == "/appcast.xml") body = FeedFailure == "tampered" ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(feed).Replace("99.0.0", "100.0.0")) : feed;
                    else if (path == "/appcast.xml.signature")
                    {
                        status = FeedFailure == "missing" ? 404 : 200;
                        body = Encoding.UTF8.GetBytes(FeedFailure == "wrong-key" ? wrongSignature : signature);
                    }
                    else if (path == "/fixture.exe") { Interlocked.Increment(ref PackageRequests); body = Package; status = PackageStatus; }
                    else { status = 404; body = []; }
                    try
                    {
                        if (path == "/fixture.exe" && HoldHeaders)
                        {
                            PackageRequested.TrySetResult();
                            if (HoldPackage != null) await HoldPackage.Task.WaitAsync(stop.Token);
                        }
                        var length = body.Length + (path == "/fixture.exe" ? ExtraLength : 0);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n"), stop.Token);
                        if (path == "/fixture.exe")
                        {
                            PackageRequested.TrySetResult();
                            if (HoldPackage != null) await HoldPackage.Task.WaitAsync(stop.Token);
                        }
                        await stream.WriteAsync(body, stop.Token);
                    }
                    catch (IOException) { /* The client can disconnect when pausing a download. */ }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel(); listener.Stop(); await serving; stop.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
