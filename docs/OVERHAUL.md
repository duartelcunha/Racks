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
- [ ] Signed update feed and packages configured with a maintainer-owned public key.
- [x] Published Windows installer upgrade / uninstall / reinstall preserves isolated file and registry fixtures.
- [ ] Mac desktop integration, signing and notarization verified on a Mac.
- [ ] Consistent 60 fps and low idle CPU accepted on target hardware, including accessibility clients.

Do not treat successful compilation or cross-publishing as Mac verification. Do not run `test/Test-Racks.ps1` against a personal application profile.

## Scope

Core racks, simple organization and routing, search, JSON settings, durable undo and interrupted-operation records, and standard signed updates. No database, watchdog, remote repair scripts, cloud accounts, content indexing, or automatic version rollback.

## Verification record

GitHub run [35093580222](https://github.com/duartelcunha/Racks/actions/runs/35093580222) passed the shared builds on Windows and Apple Silicon, 42 core cases on both platforms (platform-specific cases have guards), 11 Windows-specific tests, both native-app smoke/restart runs, and the real Windows installer lifecycle. The smoke run verifies 5,000 files with 94 realized visual elements. Windows measured 0.00624 CPU cores at idle and Mac measured 0.00187. Frame callback p95 was 31.9 ms on the Windows hosted runner and 18.5 ms on Mac. These measurements do not certify 60 fps presentation.

Local unit/integration tests pass. Native performance is inconsistent: earlier runs passed at 0.11–0.18 CPU cores and frame callback p95 around 18 ms; a subsequent run failed at 0.75 CPU cores and p95 33.6 ms. Tracing showed sustained Windows UI Automation queries without application refreshes or settings writes. The idle threshold has not been relaxed. Performance remains an open release blocker despite green CI. Off-screen/oversized window restoration and persistence pass locally.

Interactive Windows checks verified dark/light management surfaces, Ctrl+K, filename filtering, keyboard Open into Notepad, Reveal selecting the fixture in Explorer, Escape dismissal, and native drag cancellation. The drag check caught button event handling swallowing the drag start; the corrected handler observes the tunnelling pointer event. New racks use opaque backgrounds and wider tiles for readable filenames. Cross-window drag cannot be completed with the current automation tool, which rejects endpoints outside the source window.

The disposable installer test reproduced inherited destructive uninstall entries after upgrading v1.1.4. Inno's default append behavior retained the old AppData/registry deletion commands. The installer now overwrites that log, removes only explicitly named obsolete application binaries, and updates existing startup paths without enabling startup for new users. The full upgrade/uninstall/reinstall regression now passes.

## Deliberately deferred from the shared preview

The existing Windows application's optional collision physics remains available there. The shared preview exposes no unfinished physics switch or thumbnail engine; it uses lightweight file-type tiles. Named layouts, content search, local AI, custom rollback/watchdog systems, and a database migration remain out of scope. Background-image and other legacy appearance values are retained in the imported settings even when the new surface has no equivalent control yet.

The shared app is a development preview beside the current Windows release. A green build does not authorize promoting it to stable. Production feeds, signing credentials, installer lifecycle verification, real Mac integration/accessibility checks, and measured visual acceptance remain release gates.
