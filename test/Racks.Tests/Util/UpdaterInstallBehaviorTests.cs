using Racks.UseCases.Updates;
using Racks.Infrastructure.Update;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-UPDATE-1 / INV-EXEC-1, install side: the download, the size and SHA-256 checks through the held
// handle, the launch and the clean-up, with a fake server, a temp folder and a fake launcher. No real
// download, no installer, no process.
public class UpdaterInstallBehaviorTests
{
    private const string Blocked = "This update can't be verified, so Racks won't install it. ";

    private static async Task<UpdateRig> Offered(Action<UpdateRig>? tweak = null)
    {
        var rig = new UpdateRig();
        tweak?.Invoke(rig);
        rig.Build();
        await rig.CheckAsync();
        Assert.Single(rig.Notifier.Offers);   // the check verified the manifest: this is what Install may use
        return rig;
    }

    private static void AssertRefused(UpdateRig rig, string header, string body)
    {
        Assert.Empty(rig.Launcher.Started);
        Assert.Equal(0, rig.Shutdown.Count);
        Assert.Empty(rig.Downloads());   // the download folder was removed
        Assert.Equal((header, body), rig.Notifier.Plain.Last());
        Assert.True(rig.Notifier.ProgressHidden >= 1, "the progress toast must be dismissed");
    }

    // Same size, other bytes: what a swapped installer on the server looks like.
    private static byte[] Tampered(byte[] original)
    {
        var bytes = (byte[])original.Clone();
        bytes[0] ^= 0xFF;
        return bytes;
    }

    // ---- nothing pending ------------------------------------------------------------------

    [Fact]
    public async Task Install_without_a_verified_offer_is_blocked_and_downloads_nothing()
    {
        using var rig = new UpdateRig();
        rig.Build();

        await rig.Service.InstallAsync();

        Assert.Equal(("Update blocked", Blocked + "Check for updates again."), Assert.Single(rig.Notifier.Plain));
        Assert.Empty(rig.Http.Requests);
        Assert.Equal(0, rig.Notifier.ProgressStarted);
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Downloads());
    }

    // ---- the happy path -------------------------------------------------------------------

    [Fact]
    public async Task A_verified_download_is_launched_once_from_a_private_folder_while_its_handle_is_held_open()
    {
        using var rig = await Offered();
        bool writeBlocked = false, deleteBlocked = false, readable = false;
        string? hashAtLaunch = null;
        rig.Launcher.OnStart = path =>
        {
            try { using var w = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) { writeBlocked = true; }
            try { File.Delete(path); }
            catch (IOException) { deleteBlocked = true; }
            using (var r = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                readable = true;
                hashAtLaunch = Convert.ToHexStringLower(SHA256.HashData(r));
            }
        };

        await rig.Service.InstallAsync();

        string installer = Assert.Single(rig.Launcher.Started);
        string dir = Assert.Single(rig.Downloads());
        Assert.Equal(Path.Combine(dir, "Racks-Setup-1.5.0.exe"), installer);
        Assert.Matches(@"\ARacks-update-[0-9a-f]{32}\z", Path.GetFileName(dir));
        Assert.True(writeBlocked, "nobody can write the installer between the check and the start");
        Assert.True(deleteBlocked, "nobody can delete or swap the installer between the check and the start");
        Assert.True(readable);
        Assert.Equal(rig.Manifest.Sha256, hashAtLaunch);
        Assert.Equal(1, rig.Shutdown.Count);
        Assert.Equal(new[] { ReleaseCatalogClient.ReleasesApi, rig.ManifestAsset, rig.SignatureAsset, rig.InstallerAsset }, rig.Http.Requests);
        // The launcher is the only thing that used the file; afterwards the handle is gone (the installer owns it).
        File.Delete(installer);
        Assert.Equal(100, rig.Notifier.ProgressValues.Last());
        Assert.DoesNotContain(rig.Notifier.Plain, m => m.Header.StartsWith("Update ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_download_folder_is_owner_only()
    {
        using var rig = await Offered();
        string? dir = null;
        rig.Launcher.OnStart = path => dir = Path.GetDirectoryName(path);

        await rig.Service.InstallAsync();

        var acl = new DirectoryInfo(dir!).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        var me = WindowsIdentity.GetCurrent().User;
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Equal(me, r.IdentityReference));
    }

    [Fact]
    public async Task The_install_download_identifies_itself_with_the_running_version_like_the_check()
    {
        using var rig = await Offered();
        await rig.Service.InstallAsync();
        Assert.Equal("Racks/" + UpdateRig.Running, rig.Http.UserAgents.Last());
    }

    // ---- integrity: size and SHA-256 -------------------------------------------------------

    [Fact]
    public async Task A_download_with_the_right_size_and_a_wrong_hash_is_deleted_and_never_launched()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, Tampered(rig.SignedInstaller));

        await rig.Service.InstallAsync();

        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.HashMismatch));
    }

    [Fact]
    public async Task A_download_smaller_than_the_signed_size_is_deleted_and_never_launched()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller.Take(rig.SignedInstaller.Length - 1).ToArray());

        await rig.Service.InstallAsync();

        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.SizeMismatch));
    }

    [Fact]
    public async Task A_download_one_byte_larger_than_the_signed_size_is_stopped_deleted_and_never_launched()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, rig.SignedInstaller.Append((byte)0).ToArray());

        await rig.Service.InstallAsync();

        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.SizeMismatch));
    }

    [Fact]
    public async Task A_download_that_never_ends_is_aborted_soon_after_the_signed_size()
    {
        using var rig = await Offered();
        var endless = new EndlessContent();
        rig.Http.Route(rig.InstallerAsset, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless });

        await rig.Service.InstallAsync();

        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.SizeMismatch));
        // Never more than one read buffer past the signed size is requested from the server.
        Assert.True(endless.Produced <= rig.SignedInstaller.Length + 2 * 81920, $"pulled {endless.Produced} bytes");
    }

    [Fact]
    public async Task An_empty_download_is_refused()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, Array.Empty<byte>());

        await rig.Service.InstallAsync();

        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.SizeMismatch));
    }

    // ---- transport failures ---------------------------------------------------------------

    [Fact]
    public async Task A_server_error_for_the_installer_fails_the_update_and_cleans_up()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await rig.Service.InstallAsync();

        var last = rig.Notifier.Plain.Last();
        Assert.Equal("Update failed", last.Header);
        Assert.Contains("404", last.Body);
        Assert.Empty(rig.Launcher.Started);
        Assert.Equal(0, rig.Shutdown.Count);
        Assert.Empty(rig.Downloads());
    }

    [Fact]
    public async Task A_connection_error_during_the_download_fails_the_update_and_cleans_up()
    {
        using var rig = await Offered();
        rig.Http.Route(rig.InstallerAsset, _ => throw new HttpRequestException("connection reset"));

        await rig.Service.InstallAsync();

        Assert.Equal(("Update failed", "connection reset"), rig.Notifier.Plain.Last());
        Assert.Empty(rig.Launcher.Started);
        Assert.Empty(rig.Downloads());
    }

    [Fact]
    public async Task An_installer_that_cannot_be_started_leaves_the_app_running_and_removes_the_download()
    {
        using var rig = await Offered();
        rig.Launcher.Throws = new IOException("blocked by policy");

        await rig.Service.InstallAsync();

        Assert.Equal(("Update failed to start", "blocked by policy"), rig.Notifier.Plain.Last());
        Assert.Single(rig.Launcher.Started);
        Assert.Equal(0, rig.Shutdown.Count);
        Assert.Empty(rig.Downloads());
    }

    // ---- redirects ------------------------------------------------------------------------

    private const string CdnUrl = "https://release-assets.githubusercontent.com/github-production-release-asset/1/Racks-Setup-1.5.0.exe";

    [Fact]
    public async Task A_redirect_to_another_host_is_still_checked_against_the_signed_hash()
    {
        // GitHub answers the release URL with a redirect to its asset storage on another host. The host check
        // covers the URL taken from the API; what makes the bytes trustworthy is the signature + SHA-256.
        using var rig = await Offered(r => r.HandlerOverride = new FollowRedirectsHandler(r.Http));
        rig.Http.Route(rig.InstallerAsset, _ => Redirect(CdnUrl));
        rig.Http.Route(CdnUrl, Tampered(rig.SignedInstaller));

        await rig.Service.InstallAsync();

        Assert.Contains(CdnUrl, rig.Http.Requests);
        AssertRefused(rig, "Update blocked", Blocked + UpdateVerifier.Describe(UpdateCheck.HashMismatch));
    }

    [Fact]
    public async Task A_redirect_to_another_host_serving_the_signed_bytes_is_installed()
    {
        using var rig = await Offered(r => r.HandlerOverride = new FollowRedirectsHandler(r.Http));
        rig.Http.Route(rig.InstallerAsset, _ => Redirect(CdnUrl));
        rig.Http.Route(CdnUrl, rig.SignedInstaller);

        await rig.Service.InstallAsync();

        Assert.Contains(CdnUrl, rig.Http.Requests);
        Assert.Single(rig.Launcher.Started);
        Assert.Equal(1, rig.Shutdown.Count);
    }

    private static HttpResponseMessage Redirect(string to)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(to);
        return response;
    }
}
