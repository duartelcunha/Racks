# Racks overhaul implementation

The Windows WPF application remains available while the shared .NET/Avalonia application is verified. User files are never migration inputs to move or delete.

## Release gates

- [x] Legacy move/undo regressions covered by isolated tests; rack removal and uninstall code hardened.
- [x] Shared create / drop / return / persisted undo flow runs in native Windows and Mac CI.
- [x] Registry snapshot conversion preserves file references, rack appearance, and paused routing rules in tests.
- [x] Separate-process restoration and persistent undo verified locally.
- [x] Windows Open/Reveal and keyboard Quick Finder checked against isolated real files.
- [x] Native rack drag reaches collision choices; Cancel preserves the original.
- [ ] Explorer/Finder cross-application drag gestures verified end to end.
- [ ] Redesigned Windows workflows and accessibility verified interactively.
- [x] Signed Windows beta feed and package configured with a persistent public key; public download verified by a real NetSparkle client.
- [x] Published Windows installer upgrade / uninstall / reinstall preserves isolated file and registry fixtures.
- [ ] Mac desktop integration, signing and notarization verified on a Mac.
- [ ] Consistent 60 fps and low idle CPU accepted on target hardware, including accessibility clients.

Do not treat successful compilation or cross-publishing as Mac verification. Do not run `test/Test-Racks.ps1` against a personal application profile.

## Scope

Core racks, simple organization and routing, search, JSON settings, durable undo and interrupted-operation records, and standard signed updates. No database, watchdog, remote repair scripts, cloud accounts, content indexing, or automatic version rollback.

## Verification record

Windows [2.0.0-beta.1](https://github.com/duartelcunha/Racks/releases/tag/v2.0.0-beta.1) is published from `57d93d3bb3949447aca1cfd6d6d4d2a0fd3616e3`. [Windows/Mac CI](https://github.com/duartelcunha/Racks/actions/runs/35112890006) and the [signed packaging workflow](https://github.com/duartelcunha/Racks/actions/runs/35112955019) passed. All three release assets match the SHA-256 manifest; the feed and installer also passed Ed25519 verification against the tracked public key. A real strict NetSparkle client retrieved the published HTTPS feed, offered the beta to an older client, downloaded the 54,025,429-byte public installer, and verified its signature without executing it. The feed and its signature were published in one commit. Stable remains v1.1.4.

The final isolated interactive fixture measured 0.0021 CPU cores before UI Automation inspection and 0.0664 afterward, over 15 seconds each. This run did not reproduce the earlier CPU spike; it does not erase that evidence or certify frame presentation. The owned fixture process was closed normally. PresentMon capture remains unavailable under the current Windows ETW permissions; no OS permission changes were made.

The update hardening pass adds real HTTP integration checks for trusted, altered, missing-signature and wrong-key feeds; altered packages; failed-download retry; pause/resume; and installer signature revalidation/veto. It fixes a configuration that skipped graceful shutdown, a retry lockout after download errors, cancellation before the response body starts, and the Mac ZIP extraction directory. Update caches cannot receive user files or become move sources. Current automated coverage is 67 Core cases and 25 Windows cases, plus native smoke/restart and feed-signing scripts.

The publishing scripts now generate and verify signed feeds/packages with the pinned official NetSparkle tool. Tests use ephemeral keys. A separate persistent release key has been initialized outside the repository with restricted Windows file access and a DPAPI backup; its private value is encrypted in the GitHub Actions secret `RACKS_UPDATE_PRIVATE_KEY`. The public key is tracked in `installer/update-public-key.txt`. The signed Windows beta feed lives on `codex/release-feeds`; a real NetSparkle client has verified its HTTPS signature. Apple credentials and interactive Mac access remain external prerequisites; this Windows account also has no Authenticode certificate.

GitHub run [35111763031](https://github.com/duartelcunha/Racks/actions/runs/35111763031) passes the new real installer handoff: a signed HTTP download through the production update service, graceful shutdown while holding the application mutex, upgrade from the published installer, uninstall/reinstall, and retained file/settings fixtures. Both Windows and Mac signed-feed generation checks pass. The subsequent complete local suite passes 67 Core and 25 Windows cases plus native smoke/restart and signing checks.

The subsequent hardening pass adds durable undo for empty/folder racks, restart reconciliation for interrupted removal and cancelled organization, retained definitions when new files arrive during removal, validation of recovery definitions, protected recovery destinations/backups, and persisted routing pauses for unavailable sources. Keyboard selection now runs before ListBox row handling; real Windows input verified Ctrl+A, Escape, right-arrow focus, and Shift+Down range selection. Selection updates existing tiles and retains scroll/focus. Watcher bursts coalesce pending UI callbacks.

The local suite now has 56 core cases and 14 Windows cases, including legacy tray resource fallback. Two isolated native runs before interactive automation measured zero CPU time at the measurement resolution and frame callback p95 of 17.7–17.9 ms. A later run after automation passed all functional and keyboard checks but failed the unchanged idle gate at 0.55 CPU cores (p95 34.7 ms), with no catalog refreshes or settings saves. This remains a release blocker; clean-run timing does not cancel the failed evidence.

After closing the interactive fixture and resetting the automation session, the complete local suite passed: 56 core tests, 14 Windows tests, native smoke, and separate-process restart. That final run measured p95 17.6 ms, no sampled intervals over 25 ms, 94 realized visuals, and 0.106 CPU cores at idle. The performance difference associated with automation still needs investigation on target hardware.

The final UI failure-handling check also passes: a synchronous window command that throws shows its error dialog and leaves the application running. Rack-close persistence errors retain the visible rack. The updated native smoke/restart run measured p95 17.7 ms and zero CPU time at the measurement resolution.

GitHub run [35093580222](https://github.com/duartelcunha/Racks/actions/runs/35093580222) passed the shared builds on Windows and Apple Silicon, 42 core cases on both platforms (platform-specific cases have guards), 11 Windows-specific tests, both native-app smoke/restart runs, and the real Windows installer lifecycle. The smoke run verifies 5,000 files with 94 realized visual elements. Windows measured 0.00624 CPU cores at idle and Mac measured 0.00187. Frame callback p95 was 31.9 ms on the Windows hosted runner and 18.5 ms on Mac. These measurements do not certify 60 fps presentation.

Local unit/integration tests pass. Native performance is inconsistent: earlier runs passed at 0.11–0.18 CPU cores and frame callback p95 around 18 ms; a subsequent run failed at 0.75 CPU cores and p95 33.6 ms. Tracing showed sustained Windows UI Automation queries without application refreshes or settings writes. The idle threshold has not been relaxed. Performance remains an open release blocker despite green CI. Off-screen/oversized window restoration and persistence pass locally.

Interactive Windows checks verified dark/light management surfaces, Ctrl+K, filename filtering, keyboard Open into Notepad, Reveal selecting the fixture in Explorer, Escape dismissal, and native drag cancellation. The drag check caught button event handling swallowing the drag start; the corrected handler observes the tunnelling pointer event. New racks use opaque backgrounds and wider tiles for readable filenames. Cross-window drag cannot be completed with the current automation tool, which rejects endpoints outside the source window.

The disposable installer test reproduced inherited destructive uninstall entries after upgrading v1.1.4. Inno's default append behavior retained the old AppData/registry deletion commands. The installer now overwrites that log, removes only explicitly named obsolete application binaries, and updates existing startup paths without enabling startup for new users. The full upgrade/uninstall/reinstall regression now passes.

## Deliberately deferred from the shared preview

The existing Windows application's optional collision physics remains available there. The shared preview exposes no unfinished physics switch or thumbnail engine; it uses lightweight file-type tiles. Named layouts, content search, local AI, custom rollback/watchdog systems, and a database migration remain out of scope. Background-image and other legacy appearance values are retained in the imported settings even when the new surface has no equivalent control yet.

The shared app is a Windows beta beside the current stable release. A green build does not authorize promoting it to stable. Windows signed feeds and installer lifecycle checks are complete. Cross-application drag, monitor/sleep behavior, real Mac integration/accessibility and Apple signing, and measured visual/performance acceptance remain release gates.
