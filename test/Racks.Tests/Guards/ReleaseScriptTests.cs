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
    public void Release_sign_removes_signed_files_in_finally_so_a_stopped_run_leaves_no_signed_draft()
    {
        // A catch block does not run on Ctrl+C or when the console is closed; finally does.
        string s = Script("release-sign.ps1");
        int upload = s.IndexOf("release upload", StringComparison.Ordinal);
        int uploadedFlag = s.IndexOf("$uploaded = $true", StringComparison.Ordinal);
        int publishedFlag = s.IndexOf("$published = $true", StringComparison.Ordinal);
        int fin = s.IndexOf("} finally {", upload, StringComparison.Ordinal);
        int guard = s.IndexOf("if ($uploaded -and -not $published)", StringComparison.Ordinal);
        int delete = s.IndexOf("release delete-asset", StringComparison.Ordinal);
        int cleanup = s.IndexOf("Remove-Item -LiteralPath $work", StringComparison.Ordinal);

        Assert.InRange(uploadedFlag, 1, upload);                    // set before the upload starts
        Assert.True(publishedFlag > s.IndexOf("--draft=false", StringComparison.Ordinal));
        Assert.True(fin > publishedFlag, "the removal must sit in the finally after publishing");
        Assert.True(guard > fin && delete > guard && cleanup > delete);
        Assert.DoesNotContain("} catch {", s.Substring(upload, fin - upload));
        // Only a release that GitHub reports as published (isDraft false) keeps its signature.
        Assert.Contains("$isDraft -is [bool] -and -not $isDraft", s.Substring(guard, delete - guard));
    }

    [Fact]
    public void Install_signer_builds_from_the_commit_tree_not_the_working_tree()
    {
        string s = Script("install-signer.ps1");
        Assert.Contains("archive --format=zip", s);
        Assert.Contains("trusted.json", s);
        Assert.Contains("verify-tag", s);
    }

    [Fact]
    public void Install_signer_runs_only_as_the_copy_from_the_commit_it_installs()
    {
        string s = Script("install-signer.ps1");
        int self = s.IndexOf("cat-file blob \"${Commit}:tools/install-signer.ps1\"", StringComparison.Ordinal);
        int compare = s.IndexOf("if ($running -cne $committed)", StringComparison.Ordinal);
        Assert.True(self > 0 && compare > self);
        Assert.Contains("[IO.File]::ReadAllText($PSCommandPath)", s);
        Assert.InRange(compare, 1, s.IndexOf("archive --format=zip", StringComparison.Ordinal));
        Assert.InRange(compare, 1, s.IndexOf("& dotnet publish", StringComparison.Ordinal));
        Assert.InRange(compare, 1, s.IndexOf("Read-Host", StringComparison.Ordinal));
        // No default clone next to the script: a copy in %TEMP% must be told where the clone is.
        Assert.DoesNotContain("Split-Path -Parent $PSScriptRoot", s);
    }

    [Theory]
    [InlineData("install-signer.ps1")]
    [InlineData("release-sign.ps1")]
    public void Signing_scripts_are_ascii_so_the_self_check_cannot_be_fooled_by_decoding(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepoPaths.Root, "tools", name));
        Assert.DoesNotContain(bytes, b => b > 0x7F);
    }

    [Fact]
    public void Both_scripts_review_the_same_signer_and_build_inputs()
    {
        static string[] Sources(string script, string variable)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Script(script),
                System.Text.RegularExpressions.Regex.Escape(variable) + @" = @\(\r?\n(.*?)\r?\n\s*\)",
                System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(1));
            Assert.True(m.Success, $"{script} has no {variable} list");
            return m.Groups[1].Value.Split('\n')
                .Select(l => l.Trim().TrimEnd(','))
                .Where(l => l.StartsWith('\'') && l.EndsWith('\''))
                .Select(l => l.Trim('\''))
                .ToArray();
        }

        string[] install = Sources("install-signer.ps1", "$SignerSources");
        string[] release = Sources("release-sign.ps1", "$signerSources");
        Assert.Equal(install, release);
        foreach (var required in new[]
                 {
                     "tools", "Racks/Util/UpdateManifest.cs", "Racks/Util/UpdateTrust.cs",
                     ":(glob)**/Directory.Build.*", ":(glob)**/Directory.Packages.props", ":(glob)**/global.json",
                     ":(glob,icase)**/nuget.config", ":(glob)**/.gitattributes",
                 })
            Assert.Contains(required, install);

        Assert.Contains("'--' @SignerSources", Script("install-signer.ps1"));
        Assert.Contains("'--' @signerSources", Script("release-sign.ps1"));
    }

    [Theory]
    [InlineData("install-signer.ps1")]
    [InlineData("release-sign.ps1")]
    public void Git_pathspecs_are_separated_with_a_quoted_double_dash(string name)
    {
        // PowerShell swallows a bare -- when calling a function, so Invoke-Git would hand git the
        // pathspecs as revisions. Only the quoted '--' reaches git.
        foreach (var line in Script(name).Split('\n').Where(l => l.Contains("Invoke-Git")))
            Assert.DoesNotMatch(@"\s--\s", line);
    }

    [Fact]
    public void Pinned_signer_files_are_listed_with_hidden_files_included()
    {
        Assert.Contains("Get-ChildItem -LiteralPath $dest -Recurse -File -Force", Script("install-signer.ps1"));
        Assert.Contains("Get-ChildItem -LiteralPath $pinnedDir -Recurse -File -Force", Script("release-sign.ps1"));
    }

    [Fact]
    public void Releasing_doc_runs_install_signer_from_the_reviewed_commit()
    {
        string doc = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "RELEASING.md"));
        Assert.Contains("show \"${sha}:tools/install-signer.ps1\" > \"$env:TEMP\\install-signer-$sha.ps1\"", doc);
        Assert.Contains("& \"$env:TEMP\\install-signer-$sha.ps1\" -Commit $sha -Clone $clone", doc);
        Assert.DoesNotContain("\\tools\\install-signer.ps1\" -Commit", doc);
    }
}
