using Racks.Util;

namespace Racks.Tests.Util;

// INV-UPDATE-1: a release that does not verify is never named or linked. Naming its version or
// pointing at the releases page would steer the user to install an unverified build by hand.
// What the updater actually shows in each situation is pinned by UpdaterCheckBehaviorTests (fake server,
// fake toasts), not by reading the source.
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
}
