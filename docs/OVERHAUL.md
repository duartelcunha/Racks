# Racks overhaul implementation

The Windows WPF application remains available while the shared .NET/Avalonia application is verified. User files are never migration inputs to move or delete.

## Release gates

- [x] Legacy move/undo regressions covered by isolated tests; rack removal and uninstall code hardened.
- [x] Shared create / drop / return / persisted undo flow runs in native Windows and Mac CI.
- [x] Registry snapshot conversion preserves file references, rack appearance, and paused routing rules in tests.
- [x] Separate-process restoration and persistent undo verified locally.
- [ ] Open/reveal and actual OS drag gestures verified end to end.
- [ ] Redesigned Windows workflows and accessibility verified interactively.
- [ ] Signed update feed and packages configured with a maintainer-owned public key.
- [ ] Real Windows installer upgrade / uninstall verified in an isolated environment.
- [ ] Mac desktop integration, signing and notarization verified on a Mac.

Do not treat successful compilation or cross-publishing as Mac verification. Do not run `test/Test-Racks.ps1` against a personal application profile.

## Scope

Core racks, simple organization and routing, search, JSON settings, durable undo and interrupted-operation records, and standard signed updates. No database, watchdog, remote repair scripts, cloud accounts, content indexing, or automatic version rollback.

## Verification record

GitHub run [35089411903](https://github.com/duartelcunha/Racks/actions/runs/35089411903) passed the shared builds on Windows and Apple Silicon, 37 core tests on both platforms, 10 Windows-specific tests, and both native-app smoke runs. The smoke run verifies 5,000 files with 94 realized visual elements. The Windows runner measured 0.00625 CPU cores at idle; Mac measured 0.00097. Frame callback p95 was 31.9 ms on the Windows hosted runner and 17.9 ms on Mac. Those are diagnostic measurements, not a 60 fps presentation guarantee.

The expanded local suite passed 39 core tests, 11 Windows-specific tests, live routing/pause checks, and separate-process restoration/undo. After correcting the smoke harness to use actual app-owned windows, its latest 5,000-file run passed the idle gate at 0.181 CPU cores, with frame callback p95 18.3 ms. Earlier local tracing showed sustained Windows UI Automation queries without application refreshes or settings writes. The higher local idle result and frame outliers still need visual/performance acceptance; do not describe this as performance-complete.

## Deliberately deferred from the shared preview

The existing Windows application's optional collision physics remains available there. The shared preview exposes no unfinished physics switch or thumbnail engine; it uses lightweight file-type tiles. Named layouts, content search, local AI, custom rollback/watchdog systems, and a database migration remain out of scope. Background-image and other legacy appearance values are retained in the imported settings even when the new surface has no equivalent control yet.

The shared app is a development preview beside the current Windows release. A green build does not authorize promoting it to stable. Production feeds, signing credentials, installer lifecycle verification, real Mac integration/accessibility checks, and measured visual acceptance remain release gates.
