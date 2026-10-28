using System.IO;
using System.Text.RegularExpressions;

namespace Racks.Tests.Guards;

// INV-RELEASE-1: the release workflow may get an Authenticode signature from SignPath, but only when the
// repository variable SIGNPATH_ENABLED is 'true', never at the cost of the pins and permissions, and
// nothing that is not validly signed by SignPath Foundation reaches the zip, the checksums or the draft.
// Like ReleaseScriptTests this reads the YAML text (a text scanner, see R-21), not a running workflow.
public class ReleaseWorkflowSigningTests
{
    private const string Enabled = "vars.SIGNPATH_ENABLED == 'true'";
    private const string Disabled = "vars.SIGNPATH_ENABLED != 'true'";

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepoPaths.Root }.Concat(path).ToArray())).Replace("\r\n", "\n");

    // The workflow without comment-only lines (so a comment never counts as, or hides, a step).
    private static string Workflow() =>
        string.Join('\n', Read(".github", "workflows", "release.yml").Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

    private static List<string> Steps()
    {
        var parts = Regex.Split(Workflow(), @"(?m)^      - name: ", RegexOptions.None, TimeSpan.FromSeconds(1));
        return parts.Skip(1).Select(p => "- name: " + p).ToList();
    }

    private static bool IsSigningStep(string step) =>
        step.Contains("SIGNPATH", StringComparison.Ordinal) || step.Contains("signpath/", StringComparison.Ordinal) ||
        step.Contains("signed/", StringComparison.Ordinal) || step.Contains("signing-input", StringComparison.Ordinal) ||
        step.Contains("unsigned-racks", StringComparison.Ordinal);

    private static int StepIndex(List<string> steps, Func<string, bool> match)
    {
        int i = steps.FindIndex(s => match(s));
        Assert.True(i >= 0, "step not found");
        return i;
    }

    [Fact]
    public void Top_level_permissions_stay_empty_and_only_the_draft_job_can_write()
    {
        string w = Workflow();
        Assert.Matches(new Regex(@"(?m)^permissions: \{\}\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)), w);
        Assert.Single(Regex.Matches(w, @": write\b", RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Contains("  draft:", w);
        Assert.True(w.IndexOf("contents: write", StringComparison.Ordinal) > w.IndexOf("  draft:", StringComparison.Ordinal));
        // The build job (which holds the SignPath steps) only reads; the action needs these two.
        string build = w.Substring(w.IndexOf("  build:", StringComparison.Ordinal), w.IndexOf("  draft:", StringComparison.Ordinal) - w.IndexOf("  build:", StringComparison.Ordinal));
        Assert.Contains("contents: read", build);
        Assert.Contains("actions: read", build);
    }

    [Fact]
    public void Every_uses_is_pinned_to_a_full_commit_sha()
    {
        string w = Workflow();
        var uses = w.Split('\n').Where(l => Regex.IsMatch(l, @"^\s*(- )?uses:", RegexOptions.None, TimeSpan.FromSeconds(1))).ToList();
        Assert.NotEmpty(uses);
        foreach (var line in uses)
            Assert.Matches(new Regex(@"uses:\s+[\w.\-]+/[\w.\-/]+@[0-9a-f]{40}(\s+#\s*v\d[\w.\-]*)?\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)), line);
        Assert.Contains(uses, l => l.Contains("signpath/github-action-submit-signing-request@"));
    }

    [Fact]
    public void Every_signing_step_runs_only_when_the_switch_is_on_and_the_old_build_step_only_when_it_is_off()
    {
        var steps = Steps();
        var signing = steps.Where(IsSigningStep).ToList();

        var submits = signing.Where(s => s.Contains("signpath/github-action-submit-signing-request@")).ToList();
        Assert.Equal(2, submits.Count);   // Racks.exe and the installer

        // Exactly one step may carry the negated switch: the original "Build installer" (checked below).
        // Every other SignPath step must carry the positive one; none is skipped from the check.
        var negated = signing.Where(s => s.Contains(Disabled)).ToList();
        Assert.Single(negated);
        Assert.StartsWith("- name: Build installer\n", negated[0], StringComparison.Ordinal);
        foreach (var step in signing.Where(s => !ReferenceEquals(s, negated[0])))
            Assert.True(Regex.IsMatch(step, @"(?m)^        if: \$\{\{ " + Regex.Escape(Enabled) + @" \}\}\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)),
                "A SignPath step without the switch condition: " + step.Split('\n')[0]);

        // The original build step is the one that must not run when signing is on; it is otherwise as before.
        var plain = steps.Single(s => s.StartsWith("- name: Build installer\n", StringComparison.Ordinal));
        Assert.Contains("if: ${{ " + Disabled + " }}", plain);
        Assert.Contains("./build-installer.ps1 -IsccPath $env:ISCC", plain);
        Assert.DoesNotContain("-SkipPublish", plain);

        // Steps that are not about signing carry no switch condition at all (the unsigned path is unchanged).
        foreach (var step in steps.Where(s => !IsSigningStep(s)))
            Assert.DoesNotContain("\n        if:", step);
    }

    [Fact]
    public void The_installer_is_built_from_the_signed_exe_and_the_exe_is_signed_before_that()
    {
        var steps = Steps();
        int signExe = StepIndex(steps, s => s.Contains("id: sign-exe"));
        int putExe = StepIndex(steps, s => s.Contains("signed/exe/Racks.exe"));
        int build = StepIndex(steps, s => s.Contains("-SkipPublish"));
        int signSetup = StepIndex(steps, s => s.Contains("id: sign-setup"));
        int putSetup = StepIndex(steps, s => s.Contains("signed/setup/Racks-Setup.exe"));
        int unsignedExe = StepIndex(steps, s => s.Contains("id: unsigned-exe"));
        int publish = StepIndex(steps, s => s.Contains("./publish.ps1 -SelfContained"));

        Assert.True(publish < unsignedExe && unsignedExe < signExe && signExe < putExe && putExe < build);
        Assert.True(build < signSetup && signSetup < putSetup);
        Assert.Contains("wait-for-completion: true", steps[signExe]);
        Assert.Contains("wait-for-completion: true", steps[signSetup]);
    }

    [Fact]
    public void An_authenticode_check_for_signpath_foundation_runs_after_both_signatures_and_before_packaging()
    {
        var steps = Steps();
        int verify = StepIndex(steps, s => s.Contains("Get-AuthenticodeSignature") && s.Contains("SignPath Foundation"));
        string v = steps[verify];
        Assert.Contains("-ne 'Valid'", v);
        Assert.Contains("-notmatch '^CN=SignPath Foundation(,|$)'", v);
        Assert.Contains("throw", v);
        Assert.Contains("publish/Racks.exe", v);
        Assert.Contains("Racks-Setup-$env:VERSION.exe", v);
        Assert.Contains(Enabled, v);

        int package = StepIndex(steps, s => s.Contains("id: package"));
        int putSetup = StepIndex(steps, s => s.Contains("signed/setup/Racks-Setup.exe"));
        Assert.True(putSetup < verify && verify < package);
        // The zip, SHA256SUMS.txt and the draft all come after the check.
        Assert.True(steps.FindIndex(s => s.Contains("Compress-Archive")) > verify);
        Assert.True(StepIndex(steps, s => s.Contains("Upload build output")) > verify);
        // The Inno Setup check (Pyrsys B.V.) is still there and still unconditional.
        var inno = steps.Single(s => s.Contains("Pyrsys B\\.V\\."));
        Assert.DoesNotContain("\n        if:", inno);
    }

    [Fact]
    public void The_api_token_is_only_ever_secrets_SIGNPATH_API_TOKEN_in_an_api_token_input()
    {
        string w = Workflow();
        foreach (Match m in Regex.Matches(w, @"secrets\.[A-Za-z0-9_]+", RegexOptions.None, TimeSpan.FromSeconds(1)))
            Assert.Equal("secrets.SIGNPATH_API_TOKEN", m.Value);
        foreach (var line in w.Split('\n').Where(l => l.Contains("SIGNPATH_API_TOKEN")))
            Assert.Matches(new Regex(@"^\s+api-token: \$\{\{ secrets\.SIGNPATH_API_TOKEN \}\}\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)), line);
        Assert.DoesNotContain("toJSON(secrets", w);
        Assert.DoesNotContain("secrets: inherit", w);
        // Ids and slugs come from variables, not from literals in the workflow.
        foreach (var name in new[] { "SIGNPATH_ORGANIZATION_ID", "SIGNPATH_PROJECT_SLUG", "SIGNPATH_POLICY_SLUG" })
            Assert.Contains("${{ vars." + name + " }}", w);
        // One artifact configuration per file, each used by its own signing request.
        foreach (var name in new[] { "SIGNPATH_EXE_ARTIFACT_CONFIG_SLUG", "SIGNPATH_SETUP_ARTIFACT_CONFIG_SLUG" })
            Assert.Contains("artifact-configuration-slug: ${{ vars." + name + " }}", w);
    }

    [Fact]
    public void No_step_or_job_can_fail_open_with_continue_on_error()
    {
        // A failed signing, download or verification must fail the build, never be swallowed.
        Assert.DoesNotContain("continue-on-error", Workflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_from_the_event_is_pasted_into_a_script()
    {
        // Same rule as before the signing steps: ${{ }} reaches scripts only through env.
        foreach (var step in Steps())
        {
            int run = step.IndexOf("\n        run:", StringComparison.Ordinal);
            if (run < 0) continue;
            Assert.DoesNotContain("${{", step.Substring(run));
        }
    }

    [Fact]
    public void The_workflow_only_runs_by_hand_and_the_policy_files_say_what_gets_signed()
    {
        string w = Workflow();
        string on = w.Substring(w.IndexOf("on:", StringComparison.Ordinal), w.IndexOf("permissions:", StringComparison.Ordinal) - w.IndexOf("on:", StringComparison.Ordinal));
        Assert.Contains("workflow_dispatch", on);
        Assert.DoesNotContain("push", on);
        Assert.DoesNotContain("branches", on);
        Assert.DoesNotContain("pull_request", on);
        Assert.DoesNotContain("schedule", on);

        string policy = Read(".signpath", "policies", "racks", "release-signing.yml");
        Assert.Contains("github-build-policies:", policy);
        Assert.Contains("require_github_hosted: true", policy);

        string exe = Read(".signpath", "artifact-configurations", "racks-exe.xml");
        string setup = Read(".signpath", "artifact-configurations", "racks-setup.xml");
        Assert.Contains("<pe-file path=\"Racks.exe\">", exe);
        Assert.Contains("<pe-file path=\"Racks-Setup.exe\">", setup);
        foreach (var xml in new[] { exe, setup })
        {
            Assert.Contains("xmlns=\"http://signpath.io/artifact-configuration/v1\"", xml);
            Assert.Contains("<authenticode-sign/>", xml);
        }
    }

    [Fact]
    public void Code_owners_cover_the_signing_policy_and_the_workflows()
    {
        string owners = Read(".github", "CODEOWNERS");
        Assert.Matches(new Regex(@"(?m)^/\.signpath/\s+@duartelcunha\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)), owners);
        Assert.Matches(new Regex(@"(?m)^/\.github/workflows/\s+@duartelcunha\s*$", RegexOptions.None, TimeSpan.FromSeconds(1)), owners);
    }
}
