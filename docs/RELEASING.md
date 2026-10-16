# Releasing Racks

How a Racks release gets from a tag to users, and why it is split between CI and the owner's
machine.

## Trust model

The in-app updater (`Racks/Util/Updater.cs`) installs a release only if it carries a manifest
signed by the **update signing key**:

| Asset | What it is |
| --- | --- |
| `Racks-Setup-<v>.exe` | the installer |
| `Racks-Setup-<v>.manifest.json` | `{"product":"Racks","version":"<v>","fileName":"Racks-Setup-<v>.exe","sha256":"…","size":…}`, canonical UTF-8, fixed field order |
| `Racks-Setup-<v>.manifest.json.sig` | base64 ECDSA P-256 / SHA-256 signature of the manifest bytes (64-byte r‖s) |

The updater reads the first page of `/releases` (up to 30) and offers the newest release that is
newer than what runs and whose signature checks out against the public key compiled into the app
(`Racks/Util/UpdateTrust.cs`): the signed version must be exactly the release tag, and the
downloaded installer must have the signed size and SHA-256. Any mismatch or missing file: nothing
is launched. Toasts show only the validated version number, never text from the release page.
A release that does not verify is never named or linked: the user sees only that a newer release
exists that Racks can't verify, and to install only updates Racks verifies. A build without the
key (the placeholder) does not contact GitHub at all.

The private key exists only in the owner's **Windows Credential Manager** (target
`Racks/update-signing`) plus an optional passphrase-encrypted offline backup. It is never on
GitHub, in CI, in 1Password or in a plain file.

What that buys, honestly:

- A stolen GitHub token (the 2026-10-09 incident) can replace release assets, but installs that
  check signatures (the first signed release and later) will not run them.
- That holds **only if the owner reviews what gets signed and signs the release tag**. The
  signature says "the owner approved this installer". If the owner signs whatever CI built from a
  commit an attacker pushed, the attacker's installer is signed. That is why signing requires a
  tag signed with a separate SSH key that is not a GitHub credential, a typed confirmation after
  the change list, and a signer that was built once from a reviewed commit (below).
- Installs of 1.4.0 and earlier cannot check signatures at all (see "The first signed release").

CI only builds: it uploads the installer, the portable zip and `SHA256SUMS.txt` to a **draft**
release. It never signs and never publishes.

## One-time setup (owner)

Do this once, on the machine that will sign releases. Paths below are examples; use your own
clone. Never run `release-sign.ps1` or build RacksSigner from a working tree on this machine: the
only build step is `install-signer.ps1`, from a commit you reviewed.

1. **Tag signing key.** A separate SSH key whose only job is signing release tags:

   ```powershell
   ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\racks_tag_signing" -C "racks release tags"
   ```

   Give it a passphrase. **Do not add this key to GitHub as an authentication key** (and do not load
   it into an agent that GitHub sessions use): a stolen GitHub token or session must not be able to
   use it. Adding it to GitHub as a *signing* key is optional; it only adds a "Verified" badge.

2. **Configure git in your clone** to sign tags with it:

   ```powershell
   $clone = (Resolve-Path "$env:USERPROFILE\Personal Projects\Racks").Path
   git -C $clone config gpg.format ssh
   git -C $clone config user.signingkey "$env:USERPROFILE\.ssh\racks_tag_signing.pub"
   git -C $clone config gpg.ssh.allowedSignersFile "$env:LOCALAPPDATA\RacksSigner\allowed_signers"
   ```

   The last line only makes `git tag -v` work by hand; the scripts always pass the file themselves.

3. **Trust anchor outside the repo.** The scripts verify tags only against this file:

   ```powershell
   $rs = Join-Path $env:LOCALAPPDATA 'RacksSigner'
   New-Item -ItemType Directory -Force $rs | Out-Null
   $pub = (Get-Content "$env:USERPROFILE\.ssh\racks_tag_signing.pub").Trim()
   [IO.File]::WriteAllText("$rs\allowed_signers", "duartelcunha namespaces=`"git`" $pub`n")
   ```

4. **Install the signer from a reviewed commit.** Pick the commit (normally the latest signed
   release tag, or `main` after you reviewed it). Never run `install-signer.ps1` from the working
   tree: take it out of that commit, read that copy, and run that copy:

   ```powershell
   $sha = '<full 40-char sha>'
   git -C $clone show --stat $sha
   git -C $clone show "${sha}:tools/install-signer.ps1" > "$env:TEMP\install-signer-$sha.ps1"
   notepad "$env:TEMP\install-signer-$sha.ps1"     # read it before you run it
   & "$env:TEMP\install-signer-$sha.ps1" -Commit $sha -Clone $clone
   ```

   Write `${sha}` with braces: `$sha:tools` would be read as a different variable. The script
   refuses to run unless its own content is exactly `tools/install-signer.ps1` at `-Commit`. It
   builds RacksSigner from that commit's tree (`git archive`, not the working tree) into
   `%LOCALAPPDATA%\RacksSigner\<commit>\`, copies that commit's `release-sign.ps1` next to it, and
   records the SHA-256 of every file (hidden ones included) in
   `%LOCALAPPDATA%\RacksSigner\trusted.json`. A commit inside a tag signed with your key is
   accepted directly; otherwise it shows `git show --stat`, the history of the signer and build
   inputs, and what changed in them since your installed signer, and you type the commit sha back.
   The signer and build inputs are everything under `tools/`, `Racks/Util/UpdateManifest.cs`,
   `Racks/Util/UpdateTrust.cs`, and any `Directory.Build.*`, `Directory.Packages.props`,
   `global.json`, `NuGet.config` and `.gitattributes` in the tree. Reinstall the same way whenever
   one of them changes (`release-sign.ps1` tells you). Delete the `%TEMP%` copy afterwards.

5. **Create the update signing key** with the installed signer. The private key goes straight into
   Credential Manager; only the public key is printed:

   ```powershell
   $t = Get-Content "$env:LOCALAPPDATA\RacksSigner\trusted.json" | ConvertFrom-Json
   $signer = "$env:LOCALAPPDATA\RacksSigner\$($t.commit)\RacksSigner.dll"
   dotnet $signer keygen
   ```

   `keygen` refuses to run if a key already exists. Check with `cmdkey /list:Racks/update-signing`.
   The credential is stored with `CRED_PERSIST_LOCAL_MACHINE`: it survives reboots but stays on this
   PC (`CRED_PERSIST_ENTERPRISE` would roam it with a roaming profile). Windows protects it with
   DPAPI under your logon, so any program running as you can read it: sign from a machine you
   trust.

6. **Paste the public key** into `Racks/Util/UpdateTrust.cs`:

   ```csharp
   public const string PublicKeySpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…";
   ```

   Run `dotnet test`: `The_embedded_key_is_the_placeholder_or_a_valid_p256_key` checks that it
   parses. Commit and merge it. Until this is done, every build refuses every update, and the
   release workflow's gate test (`ReleaseGateTests`) fails the release build.

   RacksSigner compiles in the same `UpdateTrust.cs` and refuses to sign with a key that does not
   match it, so **reinstall the signer (step 4) from a commit that contains the real key**.

7. **Encrypted offline backup** (recommended). You type the passphrase; it is never an argument:

   ```powershell
   dotnet $signer export-backup --to E:\racks-signing-key.pem
   ```

   The file is PKCS#8 encrypted with PBES2 (AES-256-CBC, HMAC-SHA-256, 600 000 iterations). Keep it
   on removable media and the passphrase somewhere else. To restore on another PC:
   `dotnet $signer import-backup --from E:\racks-signing-key.pem`.

8. Log `gh.exe` in to the personal GitHub account `duartelcunha` (its token stays in Credential
   Manager). `release-sign.ps1` calls `gh.exe` explicitly and refuses to upload if
   `gh.exe api user` is any other account.

### The first signed release

Installs older than the first release that contains the key (1.4.0 and earlier) still run the old
updater, which checks only the asset name, URL and size. They take the next release like before,
and from then on they verify signatures. Until every user has moved past 1.4.0 a compromised
GitHub account can still reach those installs. Pin the notice below at the top of that release's
body (the release workflow copies the `CHANGELOG.md` section, which already contains it; check
it is there before signing).

#### Release body template

```markdown
> **Security notice.** Racks 1.4.0 and earlier cannot check signatures. They take this update the
> old way. Update now, or download this version from the Releases page and check it against
> SHA256SUMS.txt. Signatures are checked only from this version on.

## What's new
<!-- the CHANGELOG section for this version -->

## Verify your download
`Get-FileHash .\Racks-Setup-<v>.exe -Algorithm SHA256` must match the line for it in
`SHA256SUMS.txt`. The in-app updater checks `Racks-Setup-<v>.manifest.json(.sig)` for you.
```

## Every release

1. Bump `<Version>` in `Racks/Racks.csproj`, add the `## [x.y.z]` section to `CHANGELOG.md`, merge
   to `main`.
2. Update your clone and create a **signed** tag on the reviewed `main`:

   ```powershell
   $clone = (Resolve-Path "$env:USERPROFILE\Personal Projects\Racks").Path
   git -C $clone switch main
   git -C $clone pull --ff-only
   git -C $clone log --oneline -10            # review what you are about to release
   git -C $clone tag -s v1.4.1 -m "Racks 1.4.1"
   git -C $clone tag -v v1.4.1                # must say: Good "git" signature
   git -C $clone push origin v1.4.1
   ```

3. Wait for the **release** workflow. It builds and tests, runs the release gate test (the update
   key must be a real P-256 key), installs the pinned Inno Setup, builds the installer and creates a
   **draft** release with the installer, the portable zip and `SHA256SUMS.txt`.
4. Sign and publish with the **installed** script (never `tools\release-sign.ps1` from the clone):

   ```powershell
   $t = Get-Content "$env:LOCALAPPDATA\RacksSigner\trusted.json" | ConvertFrom-Json
   & "$env:LOCALAPPDATA\RacksSigner\$($t.commit)\release-sign.ps1" -Version 1.4.1 -Clone $clone
   ```

   The script builds nothing. In order, it:
   - checks that it runs from the pinned folder and that every file there has its pinned hash,
     and that `gh.exe` is logged in as `duartelcunha`;
   - verifies `v1.4.1` in your clone is an annotated tag signed with your tag key (against
     `%LOCALAPPDATA%\RacksSigner\allowed_signers`) and that GitHub has the same tag object;
   - checks the release is still a draft, that a successful `release.yml` run built that commit,
     and that the draft's installer, `SHA256SUMS.txt` and the run's artifact have the same SHA-256;
   - prints `git log --oneline` and `git diff --stat` from the last signed tag, notes any change to
     the signer sources since your installed signer, and asks you to type `sign and publish v1.4.1`.
     **Nothing is signed before you type it.** Anything else stops with the draft untouched;
   - signs, verifies, uploads the manifest and signature, checks them byte for byte and publishes
     immediately. If any step after signing fails, or you stop the run (Ctrl+C, closing the
     window), its `finally` block deletes the signed files from the draft and from disk: no signed
     draft is left behind. If it says it could not remove them, delete them on the release page.

To check a release by hand:
`dotnet $signer verify --installer .\Racks-Setup-1.4.1.exe` (manifest and `.sig` next to it).

## If the key is lost or leaks

- **Lost, with a backup:** `import-backup` on the signing PC.
- **Lost without a backup, or leaked:** generate a new key, put the new public key in
  `UpdateTrust.cs`, and release. Installed copies trust only the old key, so they cannot auto-update
  to this release: announce it and ask users to install it from the releases page. If the key
  leaked, also say which versions to distrust.
- **Tag signing key lost or leaked:** make a new one (setup steps 1 to 3), replace the line in
  `allowed_signers`, and re-sign the next tag with it.

## Keeping the pipeline pinned

- Actions in `.github/workflows/*.yml` are pinned by full commit SHA with the version in a comment.
  Dependabot (`.github/dependabot.yml`) proposes updates; review the action's diff before merging.
- Inno Setup is downloaded from `files.jrsoftware.org` and must match `INNO_SHA256` and be signed by
  "Pyrsys B.V." (the publisher since Inno Setup 6.4.2). To upgrade, take the new version's SHA-256
  from the vendor's `.issig` file or Chocolatey's moderated `VERIFICATION.txt`, and change
  `INNO_VERSION` and `INNO_SHA256` together.
- Job permissions: `build` has `contents: read`; only `draft` has `contents: write`, and it receives
  the build output by artifact id and checks the installer digest the build job reported. The
  checkout does not persist the token, and the tag name reaches scripts only through `env`.
