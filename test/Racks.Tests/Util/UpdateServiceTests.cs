using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Update;
using Racks.UseCases.Updates;
using Racks.Util;

namespace Racks.Tests.Util;

// The orchestration of the update (P10b): bounded feed, audited failures, one install at a time, a pending offer that survives
// parallel use, toasts that cannot break an update, and every call on the caller's thread. All with fakes.
public class UpdateServiceTests
{
    private const string Blocked = "This update can't be verified, so Racks won't install it. ";

    private static async Task<UpdateRig> Offered(Action<UpdateRig>? tweak = null)
    {
        var rig = new UpdateRig();
        tweak?.Invoke(rig);
        rig.Build();
        await rig.CheckAsync();
        Assert.Single(rig.Notifier.Offers);
        return rig;
    }

    private static IReadOnlyList<AuditEvent> Events(UpdateRig rig) => rig.Audit.Events;

    private static void AssertNoQueryInAudit(UpdateRig rig)
    {
        foreach (var e in Events(rig))
            foreach (var field in new[] { e.Source, e.Destination ?? "", e.Reason, e.Detail, e.Kind, e.Status })
            {
                Assert.DoesNotContain("?", field);
                Assert.DoesNotContain("per_page", field);
            }
    }

    // ---- the feed ----------------------------------------------------------------------------

    [Fact]
    public async Task A_feed_over_the_cap_is_a_failed_check_that_is_shown_and_audited()
    {
        using var rig = new UpdateRig();
        rig.Build();
        var endless = new EndlessContent();
        rig.Http.Route(ReleaseCatalogClient.ReleasesApi, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless });

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: true);

        Assert.Equal(("Failed to check for updates.", "The release list is too large."), Assert.Single(rig.Notifier.Plain));
        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi }, rig.Http.Requests);
        Assert.True(endless.Produced <= ReleaseCatalogClient.MaxCatalogBytes + 4096);
        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.CheckFailedKind, "Failed"), (e.Kind, e.Status));
    }

    [Fact]
    public async Task A_feed_url_that_is_not_ours_is_refused_before_any_request()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Serve();

        await rig.Service.CheckAsync("https://evil.example/releases?token=SECRET", showToastIfNoUpdate: true);

        Assert.Empty(rig.Http.Requests);
        Assert.Equal("Failed to check for updates.", Assert.Single(rig.Notifier.Plain).Header);
        var e = Assert.Single(Events(rig));
        Assert.Equal(UpdateAudit.CheckFailedKind, e.Kind);
        Assert.Equal("https://evil.example/releases", e.Source);
        AssertNoQueryInAudit(rig);
        Assert.DoesNotContain("SECRET", e.Source + e.Reason + e.Detail);
    }

    [Fact]
    public async Task A_silent_check_that_fails_leaves_no_toast_but_an_audit_record()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Http.Route(ReleaseCatalogClient.ReleasesApi, _ => throw new HttpRequestException("offline"));

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: false);

        Assert.Empty(rig.Notifier.Messages);
        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.CheckFailedKind, "Failed", "offline", "exception=HttpRequestException"), (e.Kind, e.Status, e.Reason, e.Detail));
        Assert.Equal("https://api.github.com/repos/duartelcunha/Racks/releases", e.Source);
        AssertNoQueryInAudit(rig);
    }

    [Fact]
    public async Task A_feed_that_hangs_times_out_and_the_failure_is_audited()
    {
        using var rig = new UpdateRig { CatalogTimeout = TimeSpan.FromMilliseconds(80), HandlerOverride = new HangingHandler() };
        rig.Build();

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: true);

        Assert.Equal("Failed to check for updates.", Assert.Single(rig.Notifier.Plain).Header);
        Assert.Equal("exception=TimeoutException", Assert.Single(Events(rig)).Detail);
    }

    [Fact]
    public async Task A_manifest_that_hangs_means_not_verified_not_a_hung_check()
    {
        using var rig = new UpdateRig { AssetTimeout = TimeSpan.FromMilliseconds(80) };
        rig.Build();
        rig.Serve();
        rig.Http.Hang(rig.ManifestAsset);

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, true);

        Assert.Empty(rig.Notifier.Offers);
        Assert.Equal((UpdateMessages.NotVerifiedHeader, UpdateMessages.NotVerifiedBody), Assert.Single(rig.Notifier.Plain));
        Assert.Empty(rig.Launcher.Started);
    }

    [Fact]
    public async Task A_cancelled_check_throws_and_shows_nothing()
    {
        using var rig = new UpdateRig { HandlerOverride = new HangingHandler() };
        rig.Build();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, true, cts.Token));

        Assert.Empty(rig.Notifier.Messages);
    }

    [Fact]
    public async Task Reading_the_running_version_failing_is_a_failed_check_not_a_crash()
    {
        using var rig = new UpdateRig();
        rig.Build(versionSource: () => throw new NullReferenceException("no main module"));

        await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate: true);

        Assert.Empty(rig.Http.Requests);
        Assert.Equal("Failed to check for updates.", Assert.Single(rig.Notifier.Plain).Header);
        Assert.Equal(UpdateAudit.CheckFailedKind, Assert.Single(Events(rig)).Kind);
    }

    [Fact]
    public async Task A_newer_release_that_does_not_verify_is_audited_with_the_reason_and_still_not_named()
    {
        using var rig = new UpdateRig { SigningKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256) };
        rig.Build();

        await rig.CheckAsync(showToastIfNoUpdate: false);

        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.CheckFailedKind, "Rejected", "reason=BadSignature"), (e.Kind, e.Status, e.Detail));
        Assert.Equal(UpdateVerifier.Describe(UpdateCheck.BadSignature), e.Reason);
        Assert.Empty(rig.Notifier.Messages);
        AssertNoQueryInAudit(rig);
    }

    // ---- install: audited refusals --------------------------------------------------------------

    [Fact]
    public async Task Every_refused_install_is_audited_with_its_reason_and_no_query()
    {
        var cases = new (Action<UpdateRig> Break, string Reason)[]
        {
            (rig => rig.Http.Route(rig.InstallerAsset, Flip(rig.SignedInstaller)), "reason=HashMismatch"),
            (rig => rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller.Take(10).ToArray()), "reason=SizeMismatch"),
            (rig => rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller.Append((byte)1).ToArray()), "reason=SizeMismatch"),
        };
        foreach (var (brk, reason) in cases)
        {
            using var rig = await Offered();
            brk(rig);

            await rig.Service.InstallAsync();

            var e = Assert.Single(Events(rig), x => x.Kind == UpdateAudit.InstallRefusedKind);
            Assert.Equal(("Rejected", reason), (e.Status, e.Detail));
            Assert.Equal(rig.InstallerAsset, e.Source);
            Assert.Empty(rig.Launcher.Started);
            AssertNoQueryInAudit(rig);
        }
    }

    private static byte[] Flip(byte[] b) { var c = (byte[])b.Clone(); c[0] ^= 0xFF; return c; }

    [Fact]
    public async Task Install_without_an_offer_is_audited()
    {
        using var rig = new UpdateRig();
        rig.Build();

        await rig.Service.InstallAsync();

        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.InstallRefusedKind, "Rejected", "reason=NoVerifiedOffer"), (e.Kind, e.Status, e.Detail));
        Assert.Equal(("Update blocked", Blocked + "Check for updates again."), Assert.Single(rig.Notifier.Plain));
    }

    [Fact]
    public async Task A_failed_download_and_a_failed_start_are_audited_with_query_stripped_from_the_message()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, _ => throw new HttpRequestException("Failed https://objects.example/x.exe?X-Amz-Signature=abc123&y=1 (reset)"));

        await rig.Service.InstallAsync();

        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.InstallFailedKind, "Failed", "reason=DownloadFailed"), (e.Kind, e.Status, e.Detail));
        Assert.Contains("https://objects.example/x.exe", e.Reason);
        Assert.DoesNotContain("abc123", e.Reason);
        AssertNoQueryInAudit(rig);
        Assert.Empty(rig.Downloads());

        using var rig2 = await Offered();
        rig2.Launcher.Throws = new IOException("blocked by policy");
        await rig2.Service.InstallAsync();
        var e2 = Assert.Single(Events(rig2));
        Assert.Equal((UpdateAudit.InstallFailedKind, "Failed", "reason=LaunchFailed"), (e2.Kind, e2.Status, e2.Detail));
    }

    [Fact]
    public async Task A_started_install_is_audited_and_uses_up_the_offer()
    {
        using var rig = await Offered();

        await rig.Service.InstallAsync();

        var e = Assert.Single(Events(rig));
        Assert.Equal((UpdateAudit.InstallStartedKind, "Succeeded", "version=1.5.0"), (e.Kind, e.Status, e.Detail));
        Assert.Null(rig.Service.Pending);
        // A second click on the old toast finds nothing to install.
        await rig.Service.InstallAsync();
        Assert.Single(rig.Launcher.Started);
        Assert.Equal("Update blocked", rig.Notifier.Plain.Last().Header);
    }

    [Fact]
    public async Task A_failed_install_keeps_the_offer_so_it_can_be_retried()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await rig.Service.InstallAsync();
        Assert.NotNull(rig.Service.Pending);

        rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller);
        await rig.Service.InstallAsync();
        Assert.Single(rig.Launcher.Started);
    }

    // ---- one install at a time, and the pending offer under parallel use --------------------------

    [Fact]
    public async Task A_second_install_while_one_runs_is_refused_quietly()
    {
        using var rig = await Offered();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Http.Route(rig.InstallerAsset, _ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(rig.SignedInstaller) };
        });

        var first = Task.Run(() => rig.Service.InstallAsync());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        int messagesBefore = rig.Notifier.Messages.Count;

        var others = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () => { await rig.Service.InstallAsync(); return true; })));

        Assert.All(others, Assert.True);
        Assert.Equal(messagesBefore, rig.Notifier.Messages.Count);   // no toast for the refused calls
        Assert.Equal(1, rig.Notifier.ProgressStarted);                // one download, one progress toast
        Assert.Single(rig.Http.Requests, r => r == rig.InstallerAsset);
        Assert.Equal(10, Events(rig).Count(e => e.Detail == "reason=AlreadyRunning" && e.Kind == UpdateAudit.InstallRefusedKind));
        release.Set();
        await first;
        Assert.Single(rig.Launcher.Started);
        Assert.Equal(1, rig.Shutdown.Count);
    }

    [Fact]
    public async Task Installing_can_be_started_again_after_an_install_ended()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await rig.Service.InstallAsync();
        rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller);
        await rig.Service.InstallAsync();
        Assert.Single(rig.Launcher.Started);   // the flag was released in the finally
    }

    [Fact]
    public async Task Many_parallel_checks_and_installs_never_overlap_an_install_and_leave_a_consistent_offer()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Serve();
        int active = 0, maxActive = 0;
        rig.Http.Route(rig.InstallerAsset, _ =>
        {
            int now = Interlocked.Increment(ref active);
            int seen;
            while ((seen = Volatile.Read(ref maxActive)) < now && Interlocked.CompareExchange(ref maxActive, now, seen) != seen) { }
            Thread.Sleep(5);
            Interlocked.Decrement(ref active);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(rig.SignedInstaller) };
        });

        var work = new List<Task>();
        for (int i = 0; i < 24; i++)
        {
            work.Add(Task.Run(() => rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, false)));
            work.Add(Task.Run(() => rig.Service.InstallAsync()));
        }
        await Task.WhenAll(work);   // no exception from any of them

        Assert.True(maxActive <= 1, $"{maxActive} installs downloaded at the same time");
        var pending = rig.Service.Pending;
        if (pending != null)
        {
            Assert.Equal("v1.5.0", pending.Tag);
            Assert.Equal("1.5.0", pending.Manifest.Version);
        }
        // Each folder holds exactly the one verified installer (a launched install keeps its folder).
        Assert.All(rig.Downloads(), d => Assert.Single(Directory.GetFiles(d)));
        Assert.Equal(rig.Launcher.Started.Count, rig.Shutdown.Count);
    }

    [Fact]
    public async Task Parallel_checks_that_alternately_verify_and_fail_never_leave_a_torn_offer()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Serve();
        byte[] good = System.Text.Encoding.ASCII.GetBytes(UpdateSignature.Sign(rig.Manifest.ToCanonicalBytes(), rig.Key));
        int n = 0;
        rig.Http.Route(rig.SignatureAsset, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Interlocked.Increment(ref n) % 2 == 0 ? good : new byte[] { 1, 2, 3 })
        });

        var checks = Enumerable.Range(0, 40).Select(_ => Task.Run(() => rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, false)));
        var readers = Enumerable.Range(0, 40).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                var p = rig.Service.Pending;
                if (p != null)
                {
                    Assert.Equal(p.Assets.Version, p.Manifest.Version);
                    Assert.Equal("v" + p.Manifest.Version, p.Tag);
                }
            }
        }));
        await Task.WhenAll(checks.Concat(readers));
        Assert.Empty(rig.Launcher.Started);
    }

    // ---- toasts cannot break an update ------------------------------------------------------------

    [Fact]
    public async Task A_notifier_that_throws_never_breaks_the_check_or_the_install()
    {
        using var rig = new UpdateRig();
        rig.Notifier.Throws = new InvalidOperationException("toast service is down");
        rig.Build();

        await rig.CheckAsync();                                  // offer toast throws: swallowed
        Assert.NotNull(rig.Service.Pending);

        await rig.Service.InstallAsync();                        // progress toast throws: the install still runs

        Assert.Single(rig.Launcher.Started);
        Assert.Equal(1, rig.Shutdown.Count);
    }

    [Fact]
    public async Task A_failing_toast_while_reporting_a_refusal_does_not_escape()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, Flip(rig.SignedInstaller));
        rig.Notifier.Throws = new InvalidOperationException("toast service is down");

        await rig.Service.InstallAsync();   // must not throw

        Assert.Empty(rig.Launcher.Started);
        Assert.Contains(Events(rig), e => e.Detail == "reason=HashMismatch");
    }

    // ---- threads ------------------------------------------------------------------------------------

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Cb, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        public void Complete() => _queue.CompleteAdding();
        public void Pump() { foreach (var (cb, state) in _queue.GetConsumingEnumerable()) cb(state); }
    }

    // Runs `work` on a dedicated thread with a single-threaded context (like the WPF dispatcher) and returns that thread's id.
    private static int RunOnPump(Func<Task> work)
    {
        int id = 0;
        var thread = new Thread(() =>
        {
            id = Environment.CurrentManagedThreadId;
            var ctx = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            var task = work();
            task.ContinueWith(_ => ctx.Complete(), TaskScheduler.Default);
            ctx.Pump();
            task.GetAwaiter().GetResult();
        });
        thread.Start();
        thread.Join();
        return id;
    }

    [Fact]
    public void The_interfaces_are_only_called_on_the_callers_thread()
    {
        using var rig = new UpdateRig();
        rig.Build();
        rig.Serve();

        int ui = RunOnPump(async () =>
        {
            await rig.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, true);
            await rig.Service.InstallAsync();
        });

        Assert.Single(rig.Launcher.Started);
        Assert.NotEmpty(rig.Notifier.Threads);
        Assert.All(rig.Notifier.Threads, t => Assert.Equal(ui, t));
        Assert.All(rig.Launcher.Threads, t => Assert.Equal(ui, t));
        Assert.All(rig.Shutdown.Threads, t => Assert.Equal(ui, t));
    }

    // ---- the texts stay what they were -------------------------------------------------------------------

    [Fact]
    public void The_user_visible_texts_are_unchanged()
    {
        Assert.Equal("This update can't be verified, so Racks won't install it.", UpdateMessages.CantVerify);
        Assert.Equal("Update not verified", UpdateMessages.NotVerifiedHeader);
        Assert.Equal("A newer release exists but Racks can't verify it. Only install updates that Racks verifies.", UpdateMessages.NotVerifiedBody);
        Assert.Equal("Updates are off in this build", UpdateMessages.NoKeyHeader);
        Assert.Equal("This build of Racks can't verify updates, so it doesn't look for any.", UpdateMessages.NoKeyBody);
        Assert.Equal("You are up to date!", UpdateMessages.UpToDateHeader);
        Assert.Equal("There is no available update.", UpdateMessages.UpToDateBody);
        Assert.Equal("Failed to check for updates.", UpdateMessages.CheckFailedHeader);
        Assert.Equal("Update blocked", UpdateMessages.BlockedHeader);
        Assert.Equal("Update failed", UpdateMessages.FailedHeader);
        Assert.Equal("Update failed to start", UpdateMessages.FailedToStartHeader);
        Assert.Equal("A signed update is ready to install. See what's new on the releases page.", UpdateMessages.OfferBody);
        Assert.Equal("Updating to v1.5.0", UpdateMessages.ProgressHeader("v1.5.0"));
        Assert.Equal("🚀 Racks 1.5.0 is available", UpdateMessages.OfferHeader(UpdateManifest.ForInstaller("1.5.0", new byte[32], 1)));
    }

    // ---- the audit helper ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://github.com/a/b/c.exe?token=1#frag", "https://github.com/a/b/c.exe")]
    [InlineData("https://user:pw@github.com/a?x=1", "https://github.com/a")]
    [InlineData("", "(none)")]
    [InlineData("not a url", "(invalid url)")]
    public void Audit_sources_never_carry_a_query_fragment_or_user_info(string url, string expected)
        => Assert.Equal(expected, UpdateAudit.SafeUrl(url));

    [Fact]
    public void Audit_text_loses_the_query_of_any_url_in_it()
        => Assert.Equal("GET https://a.example/p failed; see https://b.example/q",
            UpdateAudit.StripQueries("GET https://a.example/p?k=v&z=1 failed; see https://b.example/q#top."));
}
