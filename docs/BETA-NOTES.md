# Racks 2.0 Windows beta

This is a beta of the shared desktop overhaul. The existing stable Windows release remains available.

The beta includes redesigned racks and management, filename search, organization previews, simple routing, persistent undo, recovery records, and verified automatic update downloads. Installation waits for file operations to finish and a user-selected restart. User files remain outside the application installation directory.

Automated Windows/Mac tests cover file protection, collisions, cancellation, restart, signatures, and recovery. A disposable Windows VM verifies an actual signed download, graceful shutdown, installer upgrade, uninstall and reinstall without losing file/settings fixtures.

Still being validated: consistent frame presentation and idle CPU with accessibility clients; Explorer/Finder cross-application drag gestures; monitor changes and sleep/resume. Callback timing is not a guarantee of 60 fps. The shared interface currently uses English and lightweight file-type tiles; legacy physics and background-image controls remain in the existing Windows app.

The Mac release requires Apple signing/notarization and interactive Mac validation. This beta download is Windows x64 only.

For a first trial, use an isolated profile with test files:

```powershell
& "$env:LOCALAPPDATA\Programs\Racks\Racks.Next.exe" --profile "$env:LOCALAPPDATA\RacksBetaTrial"
```

An isolated profile does not import your current registry settings or start automatic update checks. Normal startup imports existing rack references without moving their files. Keep your normal file backups; exported layouts contain settings and references, not file contents.

The installer and update feed have Ed25519 signatures verified by Racks. This is separate from Windows Authenticode publisher certification; the beta installer currently has no Authenticode certificate.
