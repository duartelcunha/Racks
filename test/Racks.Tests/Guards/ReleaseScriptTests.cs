using System.IO;

namespace Racks.Tests.Guards;

// INV-RELEASE-1: the signing ceremony on the key machine builds nothing, runs only the pinned
// signer, verifies a signed tag, and asks for confirmation before it signs.
public class ReleaseScriptTests
{
    private static string Script(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", name));

    [Fact]
    public void Release_sign_never_builds_anything()
    {
        string s = Script("release-sign.ps1");
        foreach (var forbidden in new[] { "dotnet build", "dotnet publish", "dotnet run", "msbuild", "RacksSigner.csproj" })
            Assert.DoesNotContain(forbidden, s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_sign_runs_only_the_pinned_signer_and_checks_its_hashes()
    {
        string s = Script("release-sign.ps1");
        Assert.Contains("trusted.json", s);
        Assert.Contains("$signer = Join-Path $pinnedDir 'RacksSigner.dll'", s);
        Assert.Contains("This is not the installed copy", s);
        Assert.DoesNotContain("tools\\RacksSigner\\bin", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_sign_verifies_the_signed_tag_and_the_owner_before_signing()
    {
        string s = Script("release-sign.ps1");
        int sign = s.IndexOf("& dotnet $signer sign", StringComparison.Ordinal);
        Assert.True(sign > 0);
        Assert.InRange(s.IndexOf("verify-tag $tag", StringComparison.Ordinal), 1, sign);
        Assert.InRange(s.IndexOf("Assert-GhOwner", StringComparison.Ordinal), 1, sign);
        Assert.InRange(s.IndexOf("Read-Host", StringComparison.Ordinal), 1, sign);
        Assert.Contains("gpg.ssh.allowedSignersFile=$allowedSigners", s);
    }

    [Fact]
    public void Release_sign_publishes_right_after_upload_and_removes_signed_files_on_failure()
    {
        string s = Script("release-sign.ps1");
        int upload = s.IndexOf("release upload", StringComparison.Ordinal);
        int publish = s.IndexOf("--draft=false", StringComparison.Ordinal);
        Assert.True(upload > 0 && publish > upload);
        Assert.DoesNotContain("Read-Host", s.Substring(upload));
        Assert.Contains("release delete-asset", s);
    }

    [Fact]
    public void Install_signer_builds_from_the_commit_tree_not_the_working_tree()
    {
        string s = Script("install-signer.ps1");
        Assert.Contains("archive --format=zip", s);
        Assert.Contains("trusted.json", s);
        Assert.Contains("verify-tag", s);
    }
}
