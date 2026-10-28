# Releasing Racks

A release is built by a manual workflow, then signed and published from the maintainer's own machine.

## Trust model

The in-app updater (`Racks/UseCases/Updates/UpdateService.cs`, with `Racks/Infrastructure/Update/*` for HTTP, files and the installer start) installs a release only if it carries a manifest signed by the **update signing key**:

| Asset | What it is |
| --- | --- |
| `Racks-Setup-<v>.exe` | the installer |
| `Racks-Setup-<v>.manifest.json` | `{"product":"Racks","version":"<v>","fileName":"Racks-Setup-<v>.exe","sha256":"…","size":…}`, canonical UTF-8, fixed field order |
| `Racks-Setup-<v>.manifest.json.sig` | base64 ECDSA P-256 / SHA-256 signature of the manifest bytes (64-byte r‖s) |

The updater reads the first page of `/releases` (up to 30) and offers the newest release that is newer than what runs and whose signature checks out against the public key compiled into the app (`Racks/Util/UpdateTrust.cs`). The signed version must be exactly the release tag, and the downloaded installer must have the signed size and SHA-256. On any mismatch or missing file, nothing is launched. Notifications show only the validated version number, never text from the release page. A build without the key (the placeholder) does not contact GitHub at all.

The private key lives only in the maintainer's Windows Credential Manager (target `Racks/update-signing`), with an optional passphrase-encrypted offline backup. It is never on GitHub or in CI.

The workflow only builds: it uploads the installer, the portable zip and `SHA256SUMS.txt` to a **draft** release. It never signs updates and never publishes. A compromised GitHub account can replace release assets, but installs that check signatures will not run them. That protection depends on the maintainer reviewing what gets signed, which is why signing requires a signed release tag, a typed confirmation after the change list, and a signer built once from a reviewed commit.

## One-time setup

Do this once, on the machine that will sign releases. Never run `release-sign.ps1` or build RacksSigner from a working tree on this machine: the only build step is `install-signer.ps1`, run from a copy taken out of a commit you reviewed (step 4).

1. **Tag signing key.** A separate SSH key whose only job is signing release tags:

   ```powershell
   ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\racks_tag_signing" -C "racks release tags"
   ```

   Give it a passphrase. Do not add it to GitHub as an authentication key, and do not load it into an agent that GitHub sessions use.

2. **Configure git in your clone** to sign tags with it:

   ```powershell
   $clone = (Resolve-Path "<path to your clone>").Path
   git -C $clone config gpg.format ssh
   git -C $clone config user.signingkey "$env:USERPROFILE\.ssh\racks_tag_signing.pub"
   git -C $clone config gpg.ssh.allowedSignersFile "$env:LOCALAPPDATA\RacksSigner\allowed_signers"
   ```

3. **Trust anchor outside the repo.** The scripts verify tags only against this file:

   ```powershell
   $rs = Join-Path $env:LOCALAPPDATA 'RacksSigner'
   New-Item -ItemType Directory -Force $rs | Out-Null
   $pub = (Get-Content "$env:USERPROFILE\.ssh\racks_tag_signing.pub").Trim()
   [IO.File]::WriteAllText("$rs\allowed_signers", "<your GitHub user> namespaces=`"git`" $pub`n")
   ```

4. **Install the signer from a reviewed commit.** Take the script out of that commit, read it, and run that copy, never the one in the working tree:

   ```powershell
   $sha = '<full 40-char sha>'
   git -C $clone show --stat $sha
   git -C $clone show "${sha}:tools/install-signer.ps1" > "$env:TEMP\install-signer-$sha.ps1"
   notepad "$env:TEMP\install-signer-$sha.ps1"     # read it before you run it
   & "$env:TEMP\install-signer-$sha.ps1" -Commit $sha -Clone $clone
   ```

   Write `${sha}` with braces: `$sha:tools` would be read as a different variable. The script refuses to run unless its own content is exactly `tools/install-signer.ps1` at `-Commit`. It builds RacksSigner from that commit's tree (`git archive`, not the working tree) into `%LOCALAPPDATA%\RacksSigner\<commit>\`, copies that commit's `release-sign.ps1` next to it, and records the SHA-256 of every file in `%LOCALAPPDATA%\RacksSigner\trusted.json`. A commit inside a tag signed with your key is accepted directly; otherwise it shows `git show --stat` and what changed in the signer inputs since your installed signer, and you type the commit sha back. Reinstall the same way whenever the signer or its build inputs change (`release-sign.ps1` tells you). Delete the `%TEMP%` copy afterwards.

5. **Create the update signing key** with the installed signer. The private key goes straight into Credential Manager; only the public key is printed:

   ```powershell
   $t = Get-Content "$env:LOCALAPPDATA\RacksSigner\trusted.json" | ConvertFrom-Json
   $signer = "$env:LOCALAPPDATA\RacksSigner\$($t.commit)\RacksSigner.dll"
   dotnet $signer keygen
   ```

   `keygen` refuses to run if a key already exists (check with `cmdkey /list:Racks/update-signing`). The credential stays on this PC and is protected by DPAPI under your logon, so any program running as you can read it: sign only from a machine you trust.

6. **Paste the public key** into `Racks/Util/UpdateTrust.cs` (`PublicKeySpkiBase64`), run `dotnet test`, and merge. Until then every build refuses every update, and the release gate test (`ReleaseGateTests`) fails the release build. Reinstall the signer (step 4) from a commit that contains the real key.

7. **Encrypted offline backup** (recommended). You type the passphrase; it is never an argument:

   ```powershell
   dotnet $signer export-backup --to E:\racks-signing-key.pem
   ```

   Keep the file on removable media and the passphrase somewhere else. To restore on another PC: `dotnet $signer import-backup --from E:\racks-signing-key.pem`.

8. Log `gh.exe` in to the GitHub account that owns the repository. `release-sign.ps1` refuses to upload if `gh.exe api user` is any other account.

## Every release

1. Run the checks from `CONTRIBUTING.md` locally. Bump `<Version>` in `Racks/Racks.csproj`, add the `## [x.y.z]` section to `CHANGELOG.md`, and merge to `main`.
2. Create a **signed** tag on the reviewed `main`:

   ```powershell
   git -C $clone switch main
   git -C $clone pull --ff-only
   git -C $clone log --oneline -10            # review what you are about to release
   git -C $clone tag -s v1.4.1 -m "Racks 1.4.1"
   git -C $clone tag -v v1.4.1                # must say: Good "git" signature
   git -C $clone push origin v1.4.1
   ```

3. Run the **release** workflow by hand (GitHub, Actions, release, Run workflow) and choose the tag as the ref. It builds and tests, runs the release gate (the update key must be a real P-256 key), installs the pinned Inno Setup, builds the installer and creates a **draft** release with the installer, the portable zip and `SHA256SUMS.txt`.
4. Sign and publish with the **installed** script (never `tools\release-sign.ps1` from the clone):

   ```powershell
   $t = Get-Content "$env:LOCALAPPDATA\RacksSigner\trusted.json" | ConvertFrom-Json
   & "$env:LOCALAPPDATA\RacksSigner\$($t.commit)\release-sign.ps1" -Version 1.4.1 -Clone $clone
   ```

   The script builds nothing. In order, it:
   - checks that it runs from the pinned folder, that every file there has its pinned hash, and that `gh.exe` is logged in as the repository owner;
   - verifies the tag in your clone is an annotated tag signed with your tag key and that GitHub has the same tag object;
   - checks the release is still a draft, that a successful `release.yml` run built that commit, and that the draft's installer, `SHA256SUMS.txt` and the run's artifact have the same SHA-256;
   - prints `git log --oneline` and `git diff --stat` since the last signed tag and asks you to type `sign and publish v1.4.1`. Nothing is signed before you type it;
   - signs, verifies, uploads the manifest and signature, checks them byte for byte and publishes. If any step after signing fails, or you stop the run, the signed files are deleted from the draft and from disk.

To check a release by hand: `dotnet $signer verify --installer .\Racks-Setup-1.4.1.exe` (manifest and `.sig` next to it).

## If the key is lost or leaks

- **Lost, with a backup:** `import-backup` on the signing PC.
- **Lost without a backup, or leaked:** generate a new key, put the new public key in `UpdateTrust.cs`, and release. Installed copies trust only the old key, so they cannot auto-update to this release: tell users to install it from the releases page. If the key leaked, also say which versions to distrust.
- **Tag signing key lost or leaked:** make a new one (setup steps 1 to 3), replace the line in `allowed_signers`, and sign the next tag with it.

## Keeping the pipeline pinned

- Actions in `.github/workflows/*.yml` are pinned by full commit SHA with the version in a comment. Update them by hand and read the diff first.
- Inno Setup is downloaded from `files.jrsoftware.org` and must match `INNO_SHA256` and be signed by "Pyrsys B.V.". To upgrade, take the new version's SHA-256 from the vendor's `.issig` file, and change `INNO_VERSION` and `INNO_SHA256` together.
- Job permissions: `build` has `contents: read` and `actions: read`; only `draft` has `contents: write`, and it receives the build output by artifact id and checks the installer digest the build job reported. The checkout does not persist the token, and the tag name reaches scripts only through `env`.

## Authenticode signing (SignPath)

Racks is applying for free code signing from the [SignPath Foundation](https://signpath.org). When it is on, Windows sees `Racks.exe` and `Racks-Setup-<v>.exe` signed with a SignPath Foundation certificate, which reduces the SmartScreen warning over time. Policy page: [`CODE-SIGNING-POLICY.md`](CODE-SIGNING-POLICY.md).

This does not change the update trust model. The updater does not check Authenticode. You still sign the update manifest with `release-sign.ps1` over the installer the workflow built (already Authenticode-signed). The manifest's SHA-256 and the `SHA256SUMS.txt` line are of the signed file, because the workflow signs before it zips and checksums.

Version 1.5.0 waits for SignPath's approval, so it will be the first release with Authenticode. Do not create the tag `v1.5.0` before that. The switch is off by default, so releases made without it are unchanged.

### Settings

Everything is off unless the repository variable `SIGNPATH_ENABLED` is `true` (GitHub, Settings, Secrets and variables, Actions, Variables).

| Kind | Name | What it is |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath user with submitter rights on the project and policy. Only the SignPath steps of `release.yml` read it. |
| Variable | `SIGNPATH_ORGANIZATION_ID` | the organization id shown in SignPath |
| Variable | `SIGNPATH_PROJECT_SLUG` | `racks` |
| Variable | `SIGNPATH_POLICY_SLUG` | `release-signing` |
| Variable | `SIGNPATH_EXE_ARTIFACT_CONFIG_SLUG` | slug of the artifact configuration for `Racks.exe` (copy: `.signpath/artifact-configurations/racks-exe.xml`) |
| Variable | `SIGNPATH_SETUP_ARTIFACT_CONFIG_SLUG` | slug of the artifact configuration for the installer (copy: `.signpath/artifact-configurations/racks-setup.xml`) |
| Variable | `SIGNPATH_ENABLED` | `true` turns signing on. Set it last. |

### Setup, once SignPath accepts the project

1. In SignPath, add the "GitHub.com" trusted build system and link it to the project. Create the project `racks`, the two artifact configurations (paste the XML from `.signpath/artifact-configurations/`), and the signing policy `release-signing` (release certificate, origin verification on, manual approval, allowed refs limited to the release tags). Add the pipeline policy from `.signpath/policies/racks/release-signing.yml`, then create a submitter user and an API token.
2. In GitHub, add the secret and the variables from the table. Set `SIGNPATH_ENABLED=true` last.

### Each release with signing on

Run the workflow as in "Every release". It publishes `Racks.exe`, submits it to SignPath and waits. Open the request link from the log, check that the repository, tag and commit are the ones you tagged, and approve. Do the same for the installer. The workflow then verifies both with `Get-AuthenticodeSignature` (status `Valid`, subject starting with `CN=SignPath Foundation`), builds the zip and checksums from the signed files, and creates the draft. Then run `release-sign.ps1` as usual.

If a request is not approved within 2 hours, the step times out and the build fails with nothing published. Run the workflow again or push a new tag. SignPath evaluates at most 3 re-runs of a build.

### Open points to check against SignPath's screens

- The pipeline policy file path and the runner key spelling (`require_github_hosted`) follow SignPath's documentation at the time of writing; the SignPath screen is the authority.
- Whether SignPath accepts tag refs as allowed refs on the signing policy.
- The exact certificate subject; if the first signed release fails the `CN=SignPath Foundation` check, read the real subject in the log and tighten the check with the issuer and thumbprint (`ReleaseWorkflowSigningTests`).
- The API token is a repository secret. A GitHub Environment named `signpath`, restricted to the release tags, would limit it to the signing job.
- Not signed by SignPath: the loose files beside `Racks.exe` (native runtime DLLs signed by Microsoft, plus `Racks.pdb`) and the uninstaller that Inno Setup embeds in the installer.
