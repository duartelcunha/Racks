# Hardening roadmap (resume point)

Goal: move Racks from "works" to "high confidence" for a Windows desktop app that moves real user files.
Order: **file safety first, then architecture, then tests.** No new features while this runs.
Each phase is one PR, stacked on the previous one, and says what was refactored, which risk went down and
which tests were added.

## Where we are

- **Current phase:** P0 (base + docs), in review.
- **Next step:** P1, the file-safety core (see below).
- **Base branch:** `feature/scandi-branding-and-stability` (4 commits not yet on `main`). Phase branches are
  `hardening/pN-<name>`; each PR targets the previous phase's branch until the base lands on `main`.
- **Baseline (P0):** `dotnet test` = 493 passed, 1 skipped (`Category=ReleaseGate`, only runs in the release
  workflow), 0 failed. Build has 2 distinct pre-existing warnings (`RackWindow._lastBorder` unused,
  `ArchitectureRatchetTests` nullability). `dotnet format whitespace --verify-no-changes` was failing on the base
  branch (trailing spaces in `RackWindow.DragDrop.cs`); fixed in P0.

## Phases

| # | Phase | Status |
|---|---|---|
| P0 | Base, `ARCHITECTURE.md`, `AGENTS.md`, this file | in review |
| P1 | `FileOperationBroker` (facade over `SafeMove`/`SafeDelete`), `PathPolicy`, `AuditLog`, file-mutation guard; fix inline rename and Magic Organize folder names | todo |
| P2 | Route the remaining raw file mutations through the broker; shrink the allowlist | todo |
| P3 | `ShellOperationGateway` (shell change notifications, desktop icons) | todo |
| P4 | `AutoRouteService` + `RoutingPolicy`, testable `DesktopRouter`, watcher hardening | todo |
| P5 | Typed `AppSettings` and `MigrationMarkers` for root registry values | todo |
| P6 | `RackSettingsSchema` + validator + repository (golden fixtures first) | todo |
| P7 | Decompose `MainWindow` | todo |
| P8 | Decompose `App` (bootstrap, migrations, crash reporting, uninstall mode) | todo |
| P9 | Layout import/export: parser, validator, policy, executor | todo |
| P10 | `UpdateService` split (behavior tests first; stays fail-closed) | todo |
| P11 | Adversarial test gaps and Windows regression pass | todo |
| P12 | Dependency notes/updates and dead code | todo |
| P13 | Final docs: `SECURITY-INVARIANTS.md`, `ARCHITECTURE.md`, README, CHANGELOG | todo |

Good stopping points: after P2 (files), P6 (persistence), P10 (updates), P13 (done).

## Findings that drive the order

1. Inline rename (`RackWindow.Items.cs`) builds the target with `Path.Combine(dir, typedText)` and no validation: a name with `\` or a rooted path moves the file elsewhere.
2. Magic Organize "folders" mode creates directories from an unsanitized cluster name.
3. Registry load (`InstanceController.InitInstances`) uses one `try` for all racks, accepts NaN/Infinity and undefined enum values, writes doubles with the user's culture, and writes back while reading.
4. `DesktopRouter` has no watcher `Error` handling, uses `Regex` directly, ignores partial downloads and reparse points, and `Flush` is untested.
5. `RackLayoutIO.Import()` is untested; failures only reach `Debug.WriteLine`; no size or range limits.
6. `Updater` mixes toasts with logic, has no response size cap on the catalog and `InstallUpdate` is untested.
7. 185 `Debug.WriteLine` calls, none persisted.

## Constraints to respect

- Replace source-text-reading tests with behavior tests **before** moving the code they watch: `InstanceKeyTests`, `RackLayoutImportValidationTests` (switch vs allow-list), `SafeWpfValuesTests`, `UpdaterConfigTests`/`UpdaterMessageTests`/`UpdaterReleaseScanTests`, `SafeSvgTests` (startup order).
- `UpdateManifest.cs` and `UpdateTrust.cs` keep their paths (`tools/RacksSigner`, release scripts).
- `release.yml` expects exactly one `Category=ReleaseGate` test.
- `MainHwnd` registry value and `WM_RACKS_*` messages are used by `tools/*E2ERunner.ps1`.
- Saved racks must load identically after the registry refactor (golden fixtures written by the old code).
- Do not mass-convert `Debug.WriteLine` or `catch {}`; audit only the broker, registry, import and updater paths.

## Out of scope (follow-ups)

gitleaks/CodeQL in Actions, `Directory.Packages.props`, unresolved shell verbs still passing the delete gate
(M3, documented), converting every `catch {}`.

## Commands (run locally before every PR)

```powershell
dotnet restore Racks.sln
dotnet format whitespace --verify-no-changes
dotnet build Racks.sln -c Release
git diff --exit-code -- Racks/Properties/Lang.Designer.cs
dotnet test Racks.sln -c Release --no-build
dotnet publish Racks/Racks.csproj -c Release -r win-x64 --self-contained false
gitleaks detect --no-banner
```

UI-touching phases (P3, P4, P7, P8) also run `test/Test-Racks.ps1` and `tools/dev-tools.ps1 e2e`, and a manual
pass: start the app, drag a file into a rack, rename it, remove the rack, trigger an auto-route.

## Done means

- No user-data filesystem mutation outside the broker (a guard test fails otherwise).
- Presentation does not touch the registry, file mutation, shell notifications or the updater (ratchets at zero or justified).
- All registry reads/writes go through the schema + repository; bad values are never applied and always logged.
- Auto-route, import and update have a validated policy and adversarial tests; the update path is still fail-closed.
- `MainWindow` and `App` carry no domain logic (ratchets); invariants and architecture docs match the code.
