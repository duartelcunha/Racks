using Racks.Util;

namespace Racks.Tests.Util;

// Runs only in the release workflow, which sets RACKS_RELEASE_GATE=1 and fails the build when it
// does not pass (or does not run). A release built without a real update key could never verify,
// and so never install, a later update. Everywhere else it shows as skipped, so the placeholder
// key does not break normal builds.
public sealed class ReleaseGateFactAttribute : FactAttribute
{
    public const string Variable = "RACKS_RELEASE_GATE";

    public ReleaseGateFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Release gate: runs only with {Variable}=1 (release workflow).";
    }
}

public class ReleaseGateTests
{
    [ReleaseGateFact]
    [Trait("Category", "ReleaseGate")]
    public void The_embedded_update_key_is_a_real_p256_key()
        => Assert.True(UpdateTrust.IsConfigured(UpdateTrust.PublicKeySpkiBase64),
            "Racks/Util/UpdateTrust.cs has no real update signing key. Paste the output of 'RacksSigner keygen' (docs/RELEASING.md).");
}
