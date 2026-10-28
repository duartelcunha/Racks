# Security Policy

## Reporting a vulnerability

If you find a security issue in Racks, please report it privately so it can be fixed before it is disclosed.

- Open a private report with the **Report a vulnerability** button under the repository's **Security** tab.
- Say what you found, where (file and, if possible, line), and how to reproduce it.

Please do not open a public issue for security reports.

## Scope

Racks is a local, single-user Windows desktop app with no server or cloud backend. The relevant trust boundaries are other local processes, the file system, the network path to GitHub's release assets, and the contents of an imported layout file. Reports about those areas are in scope.

## What Racks guards against

- Every move, rename, delete and Recycle Bin send that Racks does on your files goes through one gateway (`FileOperationBroker`). It validates names and paths, refuses protected folders (Desktop, Documents, `AppData`, `C:\Users`...) and the folders that contain them, never overwrites, and writes one line per call to the local `%AppData%\Racks\audit.log`.
- The shell Delete verb is blocked for files inside a rack. Dragging an item out sends the original to the Recycle Bin only after the copy is checked, never a permanent delete.
- Path checks use canonical paths (junctions, 8.3 names and `\\?\` aliases are resolved first).
- Regular expressions typed by the user or read from an import run with a bounded match timeout.
- Layout import is size-limited, validated and atomic. A bad file cannot wipe your racks, and if a restore fails a copy of your previous racks is saved in `%AppData%\Racks\recovery`. Imported folder paths must be local drive paths, and font names and colors can never name a network location. Saved settings are validated when they load: an unusable value is ignored and logged.
- The updater installs a release only if its manifest is signed by the update key built into Racks, names exactly that release and a newer version, and matches the downloaded installer's size and SHA-256. Anything missing or mismatched stops the update. See [`docs/RELEASING.md`](docs/RELEASING.md).

## How the guards are enforced

Tests in `test/Racks.Tests/Guards/` fail the build if the code drifts from these rules: `FileMutationGuardTests` (raw file mutations outside the gateway, with an exact-count allowlist that only goes down), `PresentationBoundaryTests`, `RegistryAccessGuardTests` and `ArchitectureRatchetTests`. They are text scanners and do not see Win32/COM calls, calls through a variable, or `tools/`, so those need a manual review. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Code signing

The installer is not yet Authenticode-signed, so Windows SmartScreen may warn on first run. Free signing from the SignPath Foundation is in progress; see the [code signing policy](docs/CODE-SIGNING-POLICY.md). Updates are protected by the separate update signature described above, which covers the version, file name, size and SHA-256 of the installer. The source is public and can be built from scratch.
