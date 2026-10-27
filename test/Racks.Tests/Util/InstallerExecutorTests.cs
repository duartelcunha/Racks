using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Racks.Infrastructure.Update;
using Racks.UseCases.Updates;
using Racks.Util;

namespace Racks.Tests.Util;

// The download itself: timeouts, cancellation, the folder it uses and what it leaves behind. Fake server, temp folder, fake
// launcher: no network and no process.
public class InstallerExecutorTests
{
    private const string Url = UpdateRig.Repo + "v1.5.0/Racks-Setup-1.5.0.exe";

    private sealed class Rig : IDisposable
    {
        public readonly FakeHttp Http = new();
        public readonly FakeLauncher Launcher = new();
        public readonly FakeShutdown Shutdown = new();
        public readonly FakeNotifier Notifier = new();
        public readonly string Root = Path.Combine(Path.GetTempPath(), "racks-executor-test-" + Guid.NewGuid().ToString("N"));
        public readonly byte[] Bytes = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        public UpdateManifest Manifest => UpdateManifest.ForInstaller("1.5.0", SHA256.HashData(Bytes), Bytes.Length);

        public Rig() => Directory.CreateDirectory(Root);

        public InstallerExecutor Executor(TimeSpan? readTimeout = null)
            => new(new UpdateHttpClientFactory(Http), Launcher, Shutdown, Root, readTimeout);

        public Task<InstallOutcome> Run(TimeSpan? readTimeout = null, CancellationToken ct = default)
            => Executor(readTimeout).RunAsync(Url, Manifest, "1.4.0.0", Notifier.StartProgress("Updating"), ct);

        public string[] Dirs() => Directory.GetDirectories(Root, "Racks-update-*");

        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { } }
    }

    [Fact]
    public async Task A_matching_download_is_launched_and_its_folder_stays_for_the_installer()
    {
        using var rig = new Rig();
        rig.Http.Route(Url, rig.Bytes);

        var outcome = await rig.Run();

        Assert.Equal(InstallStatus.Launched, outcome.Status);
        Assert.Single(rig.Launcher.Started);
        Assert.Equal(1, rig.Shutdown.Count);
        Assert.Single(Directory.GetFiles(Assert.Single(rig.Dirs())));
    }

    [Fact]
    public async Task A_download_that_stalls_is_stopped_by_the_read_timeout_and_cleaned_up()
    {
        using var rig = new Rig();
        rig.Http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(rig.Bytes.Take(100).ToArray()) });

        var outcome = await rig.Run(readTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Equal(InstallStatus.DownloadFailed, outcome.Status);
        Assert.Equal("The download stalled and was stopped.", outcome.Message);
        Assert.Empty(rig.Launcher.Started);
        Assert.Equal(0, rig.Shutdown.Count);
        Assert.Empty(rig.Dirs());
        Assert.Equal(1, rig.Notifier.ProgressHidden);
    }

    [Fact]
    public async Task Cancelling_stops_the_download_and_cleans_up()
    {
        using var rig = new Rig();
        rig.Http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(rig.Bytes.Take(100).ToArray()) });
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        var outcome = await rig.Run(readTimeout: TimeSpan.FromSeconds(30), ct: cts.Token);

        Assert.Equal(InstallStatus.DownloadFailed, outcome.Status);
        Assert.NotEqual("The download stalled and was stopped.", outcome.Message);   // a cancellation, not a stall
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Dirs());
    }

    [Fact]
    public async Task A_server_that_never_answers_is_cancelled_and_leaves_no_folder()
    {
        using var rig = new Rig();
        rig.Http.Hang(Url);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        var outcome = await rig.Run(ct: cts.Token);

        Assert.Equal(InstallStatus.DownloadFailed, outcome.Status);
        Assert.Empty(rig.Dirs());
    }

    [Fact]
    public async Task The_signed_size_bounds_what_is_written_to_disk()
    {
        using var rig = new Rig();
        var endless = new EndlessContent();
        rig.Http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless });

        var outcome = await rig.Run();

        Assert.Equal((InstallStatus.VerificationFailed, UpdateCheck.SizeMismatch), (outcome.Status, outcome.Check));
        Assert.Empty(rig.Dirs());
        Assert.True(endless.Produced <= rig.Bytes.Length + 2 * 81920);
    }

    [Fact]
    public async Task Each_download_gets_its_own_new_folder_and_never_reuses_one()
    {
        using var rig = new Rig();
        rig.Http.Route(Url, rig.Bytes);

        await rig.Run();
        await rig.Run();

        var dirs = rig.Dirs();
        Assert.Equal(2, dirs.Length);
        Assert.All(dirs, d => Assert.Matches(@"\ARacks-update-[0-9a-f]{32}\z", Path.GetFileName(d)));
    }

    [Fact]
    public async Task A_shutdown_that_throws_reports_a_failure_after_the_installer_started()
    {
        // Characterization: the installer is already running, so its folder cannot be deleted; the user is told.
        using var rig = new Rig();
        rig.Http.Route(Url, rig.Bytes);
        var executor = new InstallerExecutor(new UpdateHttpClientFactory(rig.Http), rig.Launcher, new ThrowingShutdown(), rig.Root);

        var outcome = await executor.RunAsync(Url, rig.Manifest, "1.4.0.0", rig.Notifier.StartProgress("x"));

        Assert.Equal(InstallStatus.DownloadFailed, outcome.Status);
        Assert.Single(rig.Launcher.Started);
    }

    private sealed class ThrowingShutdown : IAppShutdown
    {
        public void Shutdown() => throw new InvalidOperationException("no application");
    }
}
