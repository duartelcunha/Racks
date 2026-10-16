namespace Racks.Util;

// The update signing public key, compiled into Racks. Updates are only installed when their
// manifest is signed by the matching private key, which lives in the owner's Windows Credential
// Manager (never on GitHub, never in CI). Compromising the GitHub account or a release therefore
// cannot push an installer to installs that check signatures, as long as the owner signs only
// reviewed releases from a signed tag. See docs/RELEASING.md.
//
// While this is the placeholder, the updater refuses EVERY update (fail closed) and tells the user
// to download from the releases page instead. Linked into tools/RacksSigner, which refuses to sign
// with a key that does not match this constant.
public static class UpdateTrust
{
    public const string Placeholder = "PLACEHOLDER: paste the output of 'RacksSigner keygen' here (docs/RELEASING.md)";

    // >>> OWNER: replace the value below with the base64 SPKI printed by `RacksSigner keygen`. <<<
    public const string PublicKeySpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFVkH+ruWMoGzcr5SFsIkZbfn92Tjbb1n1Mnj1Y+890GwlPtWsozEf1xUjye5jkCGZUmQyqhnWkMqp2eN5al46g==";

    // True only for a real P-256 public key. The placeholder is not base64, so it never imports.
    public static bool IsConfigured(string? spkiBase64)
    {
        using var key = UpdateSignature.TryImportPublicKey(spkiBase64);
        return key != null;
    }
}
