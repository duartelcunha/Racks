using Racks.Util;

namespace Racks.UseCases.Updates;

/// <summary>
/// What the user reads about an update. INV-UPDATE-1: only the version of a verified manifest is ever named;
/// nothing from the release feed (tag, name, body) is shown, and a release that does not verify is never
/// named or linked (it may be an attacker's, and a link would steer the user to install it by hand).
/// </summary>
internal static class UpdateMessages
{
    public const string CantVerify = "This update can't be verified, so Racks won't install it.";

    public const string NotVerifiedHeader = "Update not verified";
    public const string NotVerifiedBody = "A newer release exists but Racks can't verify it. Only install updates that Racks verifies.";

    // A build without the update key (local and development builds) checks nothing and contacts nobody.
    // Only a check the user asked for says so.
    public const string NoKeyHeader = "Updates are off in this build";
    public const string NoKeyBody = "This build of Racks can't verify updates, so it doesn't look for any.";

    public const string UpToDateHeader = "You are up to date!";
    public const string UpToDateBody = "There is no available update.";

    public const string CheckFailedHeader = "Failed to check for updates.";

    public const string BlockedHeader = "Update blocked";
    public const string FailedHeader = "Update failed";
    public const string FailedToStartHeader = "Update failed to start";

    public static string OfferHeader(UpdateManifest verified) => $"🚀 Racks {verified.Version} is available";
    public const string OfferBody = "A signed update is ready to install. See what's new on the releases page.";

    public static string ProgressHeader(string verifiedTag) => $"Updating to {verifiedTag}";

    public const string CheckAgain = "Check for updates again.";
    public static string BlockedBody(UpdateCheck why) => $"{CantVerify} {UpdateVerifier.Describe(why)}";
    public static string BlockedNoOfferBody => $"{CantVerify} {CheckAgain}";
}
