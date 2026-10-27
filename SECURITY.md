# Security Policy

## Reporting a vulnerability

If you find a security issue in Racks, please report it privately so it can be fixed
before it is disclosed publicly.

- Preferred: open a private report via GitHub's **"Report a vulnerability"** button under
  the repository's **Security** tab (Security Advisories).
- Please include what you found, where (file and, if possible, line), and how to reproduce it.

Please do **not** open a public issue for security reports.

## Scope

Racks is a local, single-user Windows desktop app. It has no server or cloud backend, so the
relevant trust boundaries are other local processes, the file system, the network path to
GitHub's release assets, and the contents of an imported layout file. Reports about those areas
are in scope.

## What we already guard against

The codebase is reviewed against a set of security invariants documented in
[`docs/SECURITY-INVARIANTS.md`](docs/SECURITY-INVARIANTS.md), including:

- Every move, rename, delete and Recycle Bin send that Racks itself does on your files goes through one gateway (`FileOperationBroker`): names and paths are validated, protected folders (Desktop, Documents, `AppData`, `C:\Users`...) and the folders that contain them are refused, nothing is overwritten, and each call leaves a line in the local `%AppData%\Racks\audit.log`.
- The shell Delete verb is blocked for files inside a rack (a verb Windows cannot resolve still passes; this is a documented partial), and dragging an item out sends the original to the Recycle Bin only after the copy is checked, never a permanent delete.
- Path guards are canonicalized (junctions, 8.3 names, `\\?\` aliases) before comparison.
- User- and import-supplied regular expressions run with a bounded match timeout (no ReDoS).
- Layout import is size-limited, validated and atomic (a bad file can never wipe your racks; if a restore fails, a copy of your previous racks is saved in `%AppData%\Racks\recovery`); imported folder paths must be local drive paths, never network shares, and font names and colors (from imports or saved settings) can never name a network location. Saved settings are validated when they load: a value that is not usable is ignored and logged, never applied.
- The updater installs a release only if its manifest is signed by the update key built into Racks, names exactly that release and a newer version, and matches the downloaded installer's size and SHA-256. The private key never touches GitHub or CI, and the owner signs only after verifying a release tag signed with a separate key and reviewing the changes, so a compromised GitHub account cannot push an installer to installs that check signatures (the first signed release and later; 1.4.0 and earlier cannot). Anything missing or mismatched fails closed. See [`docs/RELEASING.md`](docs/RELEASING.md).

## How the guards are enforced

Tests in `test/Racks.Tests/Guards/` fail the build if the code drifts from these rules: `FileMutationGuardTests` (raw file
mutations outside the gateway, with an exact-count allowlist that only goes down), `PresentationBoundaryTests` (presentation files do not reference the shell helpers; `MainWindow.xaml.cs` also has no file, registry, updater, hook or hotkey code), `RegistryAccessGuardTests` (presentation does not open the registry) and
`ArchitectureRatchetTests` (size ratchets). They are text scanners with known blind spots (Win32/COM calls, calls through a
variable, tools/); `docs/SECURITY-INVARIANTS.md` lists what each one sees and misses, and every finding that is still open or accepted
(for example folder moves across drives, the shell-verb gate, the audit log policy) is in its section "Riscos aceites e abertos".

## A note on code signing

The Racks installer is not Authenticode code-signed (Racks does not check an Authenticode signature either), so Windows SmartScreen
may warn on first run. What protects updates is the separate update signature above: it covers the version, the file name, the size and
the SHA-256 of the installer. The source is public and can be built from scratch.
