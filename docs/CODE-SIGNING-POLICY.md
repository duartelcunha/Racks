# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Racks is free and open source (MIT). This page says what is signed, who decides, and what the signing
does and does not promise. The release process itself is in [`RELEASING.md`](RELEASING.md).

**Status:** the signing is wired into the release workflow but switched off until SignPath Foundation
accepts the project. Until the first release that carries a SignPath signature, no Racks file is
Authenticode-signed. This page applies from that release on.

## Roles

- **Committers and reviewers:** [@duartelcunha](https://github.com/duartelcunha) (Duarte Cunha). A change
  from anyone else goes in only through a pull request that a committer has reviewed.
- **Approvers:** [@duartelcunha](https://github.com/duartelcunha) (Duarte Cunha). Every signing request is
  approved by hand in SignPath before anything is signed. There is no automatic approval.

Everyone with these roles uses multi-factor authentication on GitHub and on SignPath.

## What is signed

- `Racks.exe` (the application) and the `Racks-Setup-<version>.exe` installer.
- Only builds of a release tag `vX.Y.Z`, made by [`.github/workflows/release.yml`](../.github/workflows/release.yml)
  on GitHub-hosted runners from this repository. What stops anything else from getting a signature is the
  owner's manual approval of every request, and SignPath's origin checks (the request must come from a
  GitHub workflow run of this repository on GitHub-hosted runners, not from someone who only holds the
  API token).
- The portable zip contains the signed `Racks.exe`. The other files in the zip, and the uninstaller that
  Inno Setup embeds in the installer, are not signed separately.
- The owner is the only committer. `.github/CODEOWNERS` names him for `.signpath/` and
  `.github/workflows/`, but no GitHub ruleset enforces that review today.

## What the signing does not do

- It does not replace the update signature. The in-app updater does not read Authenticode: it installs a
  release only if the manifest is signed by the update key, and that key and the publishing step stay on
  the owner's own machine (see [`RELEASING.md`](RELEASING.md)).
- The signature shows that SignPath Foundation signed the file after its origin checks and the owner's
  approval. It does not make Windows SmartScreen stop warning at once: that reputation builds up over time.

To check a download: `Get-AuthenticodeSignature .\Racks-Setup-<version>.exe` must report `Valid`, with the
signer `SignPath Foundation`. `SHA256SUMS.txt` on the release covers the signed files.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating it.

- The only network contact is to GitHub, to look for updates (and to download the installer if you press
  Install). It happens when you ask for a check, or when you turn on automatic updates.
- No account, no telemetry, no analytics.
- `%AppData%\Racks\audit.log` and `%AppData%\Racks\crash.log` stay on your PC and are never uploaded.
- Signing sends only the build output (`Racks.exe` and the installer) from GitHub's build servers to
  SignPath. It involves no user data.

More in the "Does it send anything?" and "What does Racks write on my PC?" answers in the [README](../README.md).
