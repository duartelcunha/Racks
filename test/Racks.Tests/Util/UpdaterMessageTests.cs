using System.IO;
using System.Text.RegularExpressions;
using Racks.Tests.Guards;
using Racks.Util;

namespace Racks.Tests.Util;

// INV-UPDATE-1: a release that does not verify is never named or linked. Naming its version or
// pointing at the releases page would steer the user to install an unverified build by hand.
public class UpdaterMessageTests
{
    public static TheoryData<string> Messages() => new()
    {
        Updater.NotVerifiedHeader, Updater.NotVerifiedBody, Updater.NoKeyHeader, Updater.NoKeyBody,
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Unverified_and_no_key_messages_name_no_version_and_no_link(string message)
    {
        Assert.DoesNotMatch(@"\d", message);
        foreach (var banned in new[] { "http", "github", "releases page", "download", "www." })
            Assert.DoesNotContain(banned, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_not_verified_message_says_to_install_only_verified_updates()
    {
        Assert.Contains("can't verify it", Updater.NotVerifiedBody);
        Assert.Contains("Only install updates that Racks verifies", Updater.NotVerifiedBody);
    }

    [Fact]
    public void No_verification_reason_points_at_the_releases_page()
    {
        foreach (UpdateCheck check in Enum.GetValues<UpdateCheck>())
        {
            string text = UpdateVerifier.Describe(check);
            Assert.DoesNotContain("releases page", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Updater_toasts_never_interpolate_an_unverified_version_or_a_releases_link()
    {
        string src = File.ReadAllText(RepoPaths.App("Util", "Updater.cs"));
        Assert.DoesNotContain("ReleasesPage", src);
        Assert.DoesNotContain("github.com/duartelcunha/Racks/releases\"", src);
        // newestVersion is any newer tag, signed or not: it may decide WHICH message, never appear in one.
        Assert.DoesNotMatch(new Regex(@"\{newestVersion\}", RegexOptions.None, TimeSpan.FromSeconds(1)), src);
        // The only version a toast names is the one from the verified manifest.
        var named = Regex.Matches(src, @"\$""[^""\r\n]*Racks \{([^}]+)\}", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(new[] { "offer.Manifest.Version" }, named);
    }

    [Fact]
    public void A_build_without_the_update_key_contacts_nobody()
    {
        string src = File.ReadAllText(RepoPaths.App("Util", "Updater.cs"));
        int method = src.IndexOf("public static async Task CheckUpdateAsync", StringComparison.Ordinal);
        int keyCheck = src.IndexOf("UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64)", method, StringComparison.Ordinal);
        int fetch = src.IndexOf("GetStringAsync(url)", method, StringComparison.Ordinal);
        Assert.True(method > 0 && keyCheck > method && fetch > keyCheck);
        string noKeyBranch = src.Substring(keyCheck, fetch - keyCheck);
        Assert.Contains("ShowToast(NoKeyHeader, NoKeyBody)", noKeyBranch);
        Assert.Contains("if (showToastIfNoUpdate)", noKeyBranch);
    }
}
