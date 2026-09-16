# Developing Racks

The current Windows app is in `Racks/`. The shared app lives alongside it so the migration does not replace a working release before verification.

## Structure

- `src/Racks.Core`: settings, catalog, routing conditions, filesystem safety, and durable operation records. No UI dependency.
- `src/Racks.Desktop`: Avalonia surfaces, application session, and small Windows/Mac platform integrations.
- `tests/Racks.Core.Tests`: isolated filesystem and persistence regressions; runs on Windows and Mac.
- `tests/Racks.Windows.Tests`: tests the actual legacy assembly, settings conversion, Windows shortcuts, and update signatures.

Use .NET 10. `global.json` accepts installed .NET 10 feature releases. No global SDK change is required.

```powershell
dotnet build Racks.Overhaul.sln -c Release --warnaserror
dotnet test tests/Racks.Core.Tests -c Release
dotnet test tests/Racks.Windows.Tests -c Release # Windows only
./scripts/Test-Desktop.ps1
# Or run all applicable checks:
./scripts/Test-All.ps1
```

`Test-Desktop.ps1` runs the real native app with a fresh isolated profile. Its JSON reports cover create/drop/return, partial removal, organization undo, live first-match routing and pause, a 5,000-file rack, realized visual count, frame-callback intervals while scrolling, and idle process CPU. It then launches a second process to check restored rack state and persistent undo. Windows also checks desktop attachment/fallback and position preservation on detach. Frame callbacks are a diagnostic; they do not measure GPU presentation or certify 60 fps.

```powershell
dotnet run --project src/Racks.Desktop -- --profile "$PWD/.artifacts/manual-profile"
```

The isolated profile never imports registry settings or checks for updates. Its Desktop and workspace are inside that profile. The default production profile is `%LOCALAPPDATA%\RacksData` on Windows, or `~/Library/Application Support/Racks` on Mac. Owned files are outside the installer, in `~/RacksWorkspace`; imported racks keep their existing locations.

Do not run `test/Test-Racks.ps1` in a normal Windows account. That older WPF tray test requires a fresh disposable account/VM, the explicit `-DisposableWindowsAccount` switch, and no existing rack settings or Racks process. It never kills an existing personal instance.

## Behavior and recovery

- Moves never overwrite and never fall back to an application-written copy/delete algorithm. Unsupported cross-volume or link cases retain their original and explain the Explorer/Finder workflow.
- The entire transfer intent is saved before the first mutation, then each confirmed result is saved. Undo records its reverse intent before moving anything.
- A platform error after a mutation is ambiguous. It stays in Recovery for manual inspection; it is not automatically retried or cleaned up.
- JSON replacement is atomic and keeps a last-good backup. A settings error opens recovery mode rather than silently resetting the user's layout.
- Registry import makes a separate backup and converts settings once. The pure snapshot converter has isolated tests.
- Scanning runs off the UI thread; refreshes are serialized. Rack grids virtualize rows. Position writes are delayed until movement settles. There is no thumbnail cache or idle animation loop in the shared app.
- New shared-app shortcuts use Alt-drop; the existing WPF app retains its Ctrl-drop behavior. Ctrl/Cmd+K opens filename search, Ctrl/Cmd+Z undoes the last operation, F2 renames a selection. Arrow keys navigate files, Shift extends a range, Ctrl/Cmd+A selects all, and Escape clears selection without resetting scroll or focus.
- Empty and folder-rack removal is undoable. Interrupted definition changes are reconciled on restart. Cancelled organization drops only empty, fully accounted-for rack definitions; uncertain folders remain accessible.
- Application binaries, operation records, settings, and settings backups cannot be transfer sources or destinations. Routing pauses unavailable source folders with an explanation instead of silently appearing active.

## Translation contributions

The existing WPF tray uses `Racks/Properties/Lang.resx` and its `Lang.<culture>.resx` companions for all visible action labels and tooltips. Add matching `TrayContextMenu.*` keys to a language file; missing translations fall back to English. Chinese tray translations and fallback behavior have regression coverage. Regenerate `Lang.Designer.cs` with the existing resource generator when introducing keys, and keep the generated accessors in the commit.

This addresses the hardcoded tray strings raised in [issue #3](https://github.com/duartelcunha/Racks/issues/3). The unused `Microsoft.CodeAnalysis` package is removed. The shared preview separates filesystem and persistence logic into Core; its UI copy is currently English and does not yet share the legacy translation resources. The existing app stays available throughout that migration.

## Releases and signing

Never distribute a stable overhaul build based only on compilation and unit tests. Complete the gates in `OVERHAUL.md` first.

The Windows publishing script requires a maintainer-owned Ed25519 **public** key and an HTTPS feed base:

```powershell
./scripts/Publish-Windows.ps1 -PublicKey '<base64 public key>' -FeedBase 'https://your-release-host/racks'
```

NetSparkle uses strict verification for both feeds and packages. The application chooses `win-x64/appcast.xml` or `osx-arm64/appcast.xml` beneath the configured base. Publish each feed with its `.signature`, and sign every enclosed package using NetSparkle's standard appcast generator. Keep private signing keys outside this repository. Do not generate a replacement production trust root during a build.

Automatic checks download verified updates; installation is a user-selected safe restart and refuses active file operations. A development build without a key/feed does not expose inactive update buttons or contact a release server.

Inno Setup keeps the existing application ID and retains files/settings outside the installation directory. Installer startup refuses a running app instead of forcibly terminating it. Validate upgrade, uninstall, and reinstall in a disposable Windows VM, including upgrades from the last published installer.

`scripts/Test-Installer.ps1 -DisposableRunner` is restricted to GitHub-hosted Windows runners. It verifies the published v1.1.4 installer against GitHub's SHA-256 digest, upgrades it with a locally built package, then uninstalls and reinstalls while checking registry and file fixtures. The new installer must overwrite the old uninstall log: appending would retain v1.1.4's destructive cleanup entries. Only exact obsolete application filenames are removed; user-data folders are never recursively deleted.

Mac CI runs the shared build, core tests, and a native-window experiment on an Apple Silicon runner. This is not a substitute for Finder/Spaces, multi-monitor, sleep/resume, accessibility, signed package, and notarization checks on a real Mac session.

`bash scripts/Publish-Mac.sh` builds an Apple Silicon `.app`, signs its native binaries and bundle, submits it using an existing `notarytool` keychain profile, staples the ticket, and produces a ZIP. It requires `RACKS_PUBLIC_KEY`, `RACKS_FEED_BASE`, `RACKS_CODESIGN_IDENTITY`, and `RACKS_NOTARY_PROFILE`. The script has not been exercised with production signing credentials. NetSparkle feed/package signing remains a separate maintainer step; neither publishing script uploads a release.
