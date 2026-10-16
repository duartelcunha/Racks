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

The updater checks the signature against the public key compiled into the app
(`Racks/Util/UpdateTrust.cs`), that the signed version is exactly the release tag and newer than
what runs, and that the downloaded installer has the signed size and SHA-256. Any mismatch or
missing file: nothing is launched and the user gets a message.

The private key exists only in the owner's **Windows Credential Manager** (target
`Racks/update-signing`) plus an optional passphrase-encrypted offline backup. It is never on
GitHub, in CI, in 1Password or in a plain file. So a stolen GitHub token (the 2026-10-09 incident)
can replace release assets, but cannot make existing installs run them.

CI only builds: it uploads the installer, the portable zip and `SHA256SUMS.txt` to a **draft**
release. It never signs and never publishes.

## One-time setup (owner)

Do this once, on the machine that will sign releases.

1. Build the signer (it is not part of the app or the installer):

   ```powershell
   dotnet build tools\RacksSigner -c Release
   $signer = "tools\RacksSigner\bin\Release\net10.0-windows\RacksSigner.dll"
   ```

2. Create the key. The private key goes straight into Credential Manager; only the public key is
   printed:

   ```powershell
   dotnet $signer keygen
   ```

   `keygen` refuses to run if a key already exists. Check with `cmdkey /list:Racks/update-signing`.
   The credential is stored with `CRED_PERSIST_LOCAL_MACHINE`: it survives reboots but stays on this
   PC (`CRED_PERSIST_ENTERPRISE` would roam it with a roaming profile). Windows protects it with
   DPAPI under your logon, so any program running as you can read it: sign from a machine you
   trust.

3. Paste the printed public key into `Racks/Util/UpdateTrust.cs`:

   ```csharp
   public const string PublicKeySpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…";
   ```

   Run `dotnet test`: `The_embedded_key_is_the_placeholder_or_a_valid_p256_key` checks that it
   parses. Commit it. Until this is done, every build refuses every update (and `release.yml`
   refuses to build a release).

4. Make an encrypted offline backup (recommended). You type the passphrase; it is never an
   argument:

   ```powershell
   dotnet $signer export-backup --to E:\racks-signing-key.pem
   ```

   The file is PKCS#8 encrypted with PBES2 (AES-256-CBC, HMAC-SHA-256, 600 000 iterations). Keep it
   on removable media and the passphrase somewhere else. To restore on another PC:
   `dotnet $signer import-backup --from E:\racks-signing-key.pem`.

5. Log `gh.exe` in to the personal GitHub account (its token stays in Credential Manager).
   `tools/release-sign.ps1` calls `gh.exe` explicitly.

### The first signed release

Installs older than the first release that contains the key (1.4.0 and earlier) still run the old
updater, which checks only the asset name, URL and size. They will take the next release like
before, and from then on they verify signatures. Until every user has moved past 1.4.0 a compromised
GitHub account can still reach those installs, so tell users to update (or reinstall from the
releases page) once the signed release is out.

## Every release

1. Bump `<Version>` in `Racks/Racks.csproj`, add the `## [x.y.z]` section to `CHANGELOG.md`, merge
   to `main`.
2. Tag and push the tag from your clone:

   ```powershell
   git tag v1.4.1
   git push origin v1.4.1
   ```

3. Wait for the **release** workflow. It builds and tests, installs the pinned Inno Setup, builds
   the installer and creates a **draft** release with the installer, the portable zip and
   `SHA256SUMS.txt`.
4. Sign and publish from your clone:

   ```powershell
   .\tools\release-sign.ps1 -Version 1.4.1
   ```

   The script checks that the tag on GitHub points at the same commit as your local tag, that the
   release is still a draft, that a successful `release.yml` run built that commit, and that the
   draft's installer, `SHA256SUMS.txt` and the run's artifact all have the same SHA-256. Then it
   signs with RacksSigner, verifies the result with the app's own checks, uploads the manifest and
   signature, re-downloads them to compare, and publishes **only** after you type `publish v1.4.1`.

To check a release by hand:
`dotnet $signer verify --installer .\Racks-Setup-1.4.1.exe` (manifest and `.sig` next to it).

## If the key is lost or leaks

- **Lost, with a backup:** `import-backup` on the signing PC.
- **Lost without a backup, or leaked:** generate a new key, put the new public key in
  `UpdateTrust.cs`, and release. Installed copies trust only the old key, so they cannot auto-update
  to this release: announce it and ask users to install it from the releases page. If the key
  leaked, also say which versions to distrust.

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
