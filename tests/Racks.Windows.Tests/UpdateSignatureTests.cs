using System.Security.Cryptography;
using System.Text;
using Chaos.NaCl;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using Xunit;

namespace Racks.Tests.Windows;

public sealed class UpdateSignatureTests
{
    [Fact] public void StrictVerifierAcceptsTrustedPayloadAndRejectsModification()
    {
        // Ephemeral test keys never become a product trust root.
        Ed25519.KeyPairFromSeed(out var publicKey, out var privateKey, RandomNumberGenerator.GetBytes(32));
        var payload = Encoding.UTF8.GetBytes("signed release feed"); var signature = Convert.ToBase64String(Ed25519.Sign(payload, privateKey));
        var verifier = new Ed25519Checker(SecurityMode.Strict, Convert.ToBase64String(publicKey));
        Assert.Equal(ValidationResult.Valid, verifier.VerifySignature(signature, payload));
        payload[0] ^= 1; Assert.NotEqual(ValidationResult.Valid, verifier.VerifySignature(signature, payload));
    }
    [Fact] public void StrictVerifierRejectsMissingAndUntrustedSignatures()
    {
        Ed25519.KeyPairFromSeed(out var publicKey, out _, RandomNumberGenerator.GetBytes(32));
        Ed25519.KeyPairFromSeed(out _, out var otherPrivateKey, RandomNumberGenerator.GetBytes(32));
        var payload = Encoding.UTF8.GetBytes("update package"); var verifier = new Ed25519Checker(SecurityMode.Strict, Convert.ToBase64String(publicKey));
        Assert.NotEqual(ValidationResult.Valid, verifier.VerifySignature("", payload));
        Assert.NotEqual(ValidationResult.Valid, verifier.VerifySignature(Convert.ToBase64String(Ed25519.Sign(payload, otherPrivateKey)), payload));
    }
}
