# Racks overhaul implementation

The Windows WPF application remains available while the shared .NET/Avalonia application is verified. User files are never migration inputs to move or delete.

## Release gates

- [ ] Legacy safety regressions covered and fixed.
- [ ] Shared create / drop / open / return / restart flow verified.
- [ ] Registry import preserves paths and appearance.
- [ ] Redesigned Windows workflows and accessibility verified interactively.
- [ ] Signed update feed and packages configured with a maintainer-owned public key.
- [ ] Real Windows installer upgrade / uninstall verified in an isolated environment.
- [ ] Mac desktop integration, signing and notarization verified on a Mac.

Do not treat successful compilation or cross-publishing as Mac verification. Do not run `test/Test-Racks.ps1` against a personal application profile.

## Scope

Core racks, simple organization and routing, search, JSON settings, durable undo and interrupted-operation records, and standard signed updates. No database, watchdog, remote repair scripts, cloud accounts, content indexing, or automatic version rollback.
