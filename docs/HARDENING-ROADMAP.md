# Hardening roadmap (resume point)

Goal: move Racks from "works" to "high confidence" for a Windows desktop app that moves real user files.
Order: **file safety first, then architecture, then tests.** No new features while this runs.
Each phase is one PR, stacked on the previous one, and says what was refactored, which risk went down and
which tests were added.

## Where we are

- **Current phase:** P4 (`AutoRouteService` + `RoutingPolicy`), in review; P3 (`ShellOperationGateway`) is its base.
- **Next step:** P5, typed `AppSettings` and `MigrationMarkers` for the root registry values. The file allowlist
  (`test/Racks.Tests/Guards/fs-allowlist.json`) is only Racks-owned state; its numbers only go down.
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
| P1 | `FileOperationBroker` (facade over `SafeMove`/`SafeDelete`), `PathPolicy`, `AuditLog`, file-mutation guard; fix inline rename and Magic Organize folder names | in review |
| P2 | Route the remaining raw file mutations through the broker; shrink the allowlist; refuse ancestors of protected folders | in review |
| P3 | `ShellOperationGateway` (shell change notifications, desktop icons) | in review |
| P4 | `AutoRouteService` + `RoutingPolicy`, testable `DesktopRouter`, watcher hardening | in review |
| P5 | Typed `AppSettings` and `MigrationMarkers` for root registry values | todo |
| P6 | `RackSettingsSchema` + validator + repository (golden fixtures first) | todo |
| P7 | Decompose `MainWindow` | todo |
| P8 | Decompose `App` (bootstrap, migrations, crash reporting, uninstall mode) | todo |
| P9 | Layout import/export: parser, validator, policy, executor | todo |
| P10 | `UpdateService` split (behavior tests first; stays fail-closed) | todo |
| P11 | Adversarial test gaps and Windows regression pass | todo |
| P12 | Dependency notes/updates and dead code | todo |
| P13 | Final docs: `SECURITY-INVARIANTS.md`, `ARCHITECTURE.md`, README, CHANGELOG | todo |

### P1 in short (what changed)

- New `Racks/Infrastructure/FileSystem/` (`FileOperationBroker`, `PathPolicy`, intent/result/context types) and
  `Racks/Infrastructure/Logging/` (`IAuditSink`, `FileAuditSink` -> `%AppData%\Racks\audit.log`, `AuditLog`).
  `SafeMove`/`SafeDelete` are now `internal` primitives; every former caller (auto-route, drop, drag-out,
  Magic Organize and its undo, workspace return, remove rack, bootstrap rack) calls the broker with a reason.
- Fixed: inline rename built its target with `Path.Combine(dir, typedText)` (slash, `..`, `C:\x` moved the item);
  Magic Organize "Folders" mode created folders from the raw group name. Both now go through `PathPolicy`.
- Guard: `FileMutationGuardTests` counts raw `File`/`Directory` mutations per file against
  `Guards/fs-allowlist.json` (ratchet) and forbids `SafeMove.`/`SafeDelete.` outside the gateway. Its blind
  spots are listed in the test's header comment.
- Not done on purpose: shell notifications stay where they were (P3); the 12 files then in the allowlist were P2.
- Known gap (closed in P2): protection was by equality with a protected folder, so a path that *contained* a
  protected folder (for example `C:\Users`) was not refused by `DeleteTree`/`Move`. Admin shares
  (`\\host\C$\...`) are refused by `PathPolicy` because over SMB the canonical form is the UNC one.
- Audit log privacy: only the exact user-profile prefix becomes `%USERPROFILE%`; file names and other paths are
  logged. The log is local only and is written synchronously.
- Review fixes (second commit): rename acts on the link itself (no volume check, protected check on the item,
  not its target), case-only rename is one direct move with a temp-name fallback that keeps the extension,
  `DeleteToRecycleBin` fails closed on UNC / non-fixed drives (residual risk documented in INV-FS-1), only
  0x00-0x1F are control characters, the guard requires exact counts and also looks at `File.Create`,
  `AppendAll*`, write-intent `FileStream`/`File.Open` and `using static`/alias imports.

### P2 in short (what changed)

- New broker operations, each validate -> preflight -> execute -> confirm -> one audit record, never throwing for
  an expected failure: `DeleteEmptyDirectory` (one empty real folder, never recursive), `Unlink` (a junction,
  a symlink or an extra hard link name; never data), `DeleteRacksShortcut` (a `.lnk`/`.url` up to 1 MiB directly
  inside a folder the caller names). `MoveSameVolume` is now used by the uninstall path.
- Routed: orphan shortcut in a virtual rack (`Rack/OrphanShortcutCleanup`, extracted so it is testable),
  emptied rack folder on the Desktop, workspace hard link in `WorkspaceReturn`, empty folders in Magic Organize
  undo, `RackMirror.RemoveJunctions`, and in `UninstallCleanup` the renames, unlinks and empty-folder removals
  (reasons start with `uninstall:` or `remove rack:`).
- Containment: `SafeMove.Check` (so `Move`, `MoveSameVolume`, `DeleteTree`, `DeleteToRecycleBin` and `Rename`)
  refuses an ancestor of a protected folder, by canonical prefix with a trailing backslash (INV-FS-1).
- Behavior changes that are deliberate (they only make things safer): a reparse point that is not a junction or
  symlink (OneDrive/cloud placeholder) is no longer deleted by uninstall, remove-rack or the mirror cleanup (uninstall
  renames it back like any item; the mirror leaves it); a file in a virtual rack that is the only name of its
  data is no longer deleted as an "orphan shortcut"; a protected, non-empty or linked folder is no longer
  removed by the rack-folder and undo cleanups.
- Final allowlist (was 45 calls in 12 files, now 32 calls in 9 files; every entry is Racks-owned state or a
  non-destructive copy): `Core/DesktopIconManager.cs` 7, `RackWindow.DragDrop.cs` 2, `Util/CrashLog.cs` 3,
  `Util/JunctionHelper.cs` 5, `Util/RackLayoutIO.cs` 1, `Util/RackMirror.cs` 2, `Util/ShortcutFactory.cs` 1,
  `Util/UninstallCleanup.cs` 8, `Util/Updater.cs` 3.
- Review fixes (second commit): `IsProtectedLink`/`IsProtectedOrAncestorLink` now match each protected folder in
  three spellings (resolved, parent resolved + own name, raw) and expand an 8.3 leaf, so a Desktop/Documents/
  AppData that was relocated with a junction is recognised under the junction's own path (`Unlink`/`Rename` of
  that junction are refused; any other link pointing at it is still just a link); a per-thread test seam
  (`SafeMove.UseProtectedFoldersForTest`) lets tests declare a temp folder protected without touching real
  folders; the orphan-shortcut cleanup only runs for a rack whose folder is inside the virtual frames root (a
  duplicated Desktop rack is shortcuts-only too, with the real Desktop as folder); classes that call
  `AuditLog.Use` share the xunit collection `AuditLog` (chosen over an AsyncLocal sink because the broker's
  sink is also written from non-test threads); tests that need a second drive, symlink rights, 8.3 names or a
  SendTo folder are skipped with a message instead of returning early; the installer removes `audit.log*` like
  `crash.log*`.
- Not done on purpose: the Win32/COM and `DirectoryInfo` attribute changes the guard cannot see (listed in the
  guard header) were reviewed by hand, not routed; `Unlink` deletes by path after the checks (a swap of the item
  between the check and the delete is a tiny window; a handle-based delete is a P11 candidate).

### P3 in short (what changed)

- New `Racks/Infrastructure/Shell/ShellOperationGateway` (`Default`, like the broker): `NotifyItemMoved`,
  `NotifyDirectoryChanged`, `ArrangeDesktopIcons`, `SetDesktopIconsHidden`, `EnsureDesktopVisible`,
  `FindDesktopIconList`, `RestoreDesktopIconsIfHiddenByRacks`, `InitializeWorkspace`, `WorkspacePath`. Pure
  delegation: same calls, same thread, notifications stay best-effort (an empty path is skipped, as before).
- Moved: 6 files (MainWindow.xaml.cs, RackWindow.DragDrop/Menu/Files.cs, App.xaml.cs, plus Core/MagicOrganizeUndo.cs).
  `DesktopIcons`, `DesktopIconPositioner` and `DesktopIconManager` are now `internal`; `DesktopIconPositioner` lost its
  duplicate `SHChangeNotify` P/Invoke and uses the one in `Interop` (now `internal`, with `NotifyShell*`). The dead
  `Interop.RefreshDesktop` (no callers, no tests) was deleted.
- Guard: `PresentationBoundaryTests` allows zero references to these helpers in presentation files (no baseline
  file needed). Its blind spots are in the test header.
- Out of scope on purpose (stays in presentation): `ShellContextMenu`, `ShellMenuHandlers`, `ShellViewWaiter`, window
  geometry/style/blur/hotkey Win32 calls, `Process.Start` launches, the tray and `ShellLinkHelper` (only
  `ShortcutFactory` uses it). `UninstallCleanup` still calls `DesktopIcons.ForceShow` and `DesktopRouter` still reads
  `DesktopIconManager.RacksWorkspacePath` directly: neither is presentation (P4 for the router).

### P4 in short (what changed)

- New `Racks/UseCases/AutoRoute/` (the first Application-layer code; folder and namespace `UseCases`): `RoutingPolicy` (is this Desktop entry
  eligible), `RoutingDecision`/`RoutingOutcome`, `AutoRouteService` (match, policy, broker move, shell notify,
  desktop-rack claim, persist, one audit record per decision). The MainWindow callback is gone; MainWindow wires
  `AutoRouteHost` (racks, save, workspace path) and `new DesktopRouter(service)`.
- `DesktopRouter` is now the thin watcher: `IDelayScheduler` (Dispatcher-based in the app) for the debounce, a
  64 KB buffer, names-only filter, one window (= one batch) per burst with de-duplication by path, `Error`
  recovery (rebuild the watcher, rescan the Desktop top level once, 250 ms doubling to 30 s while errors keep
  coming without a real event in between). The rescan routes only names that were NOT on the Desktop while the
  watcher was healthy (a snapshot taken at start, plus every arrival since): files the user keeps there are never
  swept up. A start that failed and has no snapshot yet routes nothing.
  `DestinationFor` stays as a forwarding member, `DesktopRouterDestinationTests` is unedited.
- Fixed: events lost under bursts and a watcher that stayed dead after an error; raw `Regex.IsMatch` (now
  `SafeRegex`, compiled once per rack, one audit record per bad rule); half-written downloads, Office `~$` lock
  files, `desktop.ini`, hidden/system entries, junctions/symlinks and files still being written were routed (or
  failed in the broker); `Instance.AutoRouteRegex` accepted anything (now only what `SafeRegex.IsAcceptable` takes).
- Deliberate behavior changes: see CHANGELOG `[Unreleased]`. Cloud files: online-only ones (offline, recall-on-open,
  recall-on-data-access) are skipped; fully local synced files (reparse point, no recall flag) stay routable and are
  never opened for the in-use probe. A reparse point whose kind can't be read is skipped (fail closed).
- Rules: `Instance.AutoRouteRegex` stores any text (a saved rule is never lost to a load-time check); the service
  makes a rack with an invalid or too-slow rule inactive (one audit record per rack and pattern, text untouched);
  only the rule dialog (`Views/AutoRouteRuleDialog`) refuses to save such a pattern, with a message.
- Namespace: the first P4 commit used `Racks.Application`, which shadows `System.Windows.Application` inside
  `namespace Racks`; the review renamed it to `Racks.UseCases` and reverted the 9 files touched for that.
- Ratchet: moving the rule dialog out of `RackWindow.Menu.cs` lowered `rackWindowPartMaxLines` to 919 and
  `rackWindowTotalLines` to 5935 in `Guards/baseline.json`.
- Not done on purpose: no first scan of the Desktop at startup (only after an `Error`), the watcher still ignores
  folders (`FileName` filter, as before), no `Changed` handling for a big copy that outlasts the ~30 s of retries (it
  stays on the Desktop with an audit record), no user-facing message when the rule dialog rejects a pattern (the
  previous rule just stays, like the font and color settings).

### Follow-ups recorded by the P2 review (not implemented)

- **Deliberate change to revisit:** in a virtual rack a file that is the only name of its data is no longer
  deleted as an "orphan shortcut" after an Explorer Move drag-out that crossed volumes. The original stays in the
  rack, so the user sees a duplicate. The proper fix is the desktop-rack pattern: `CopyLooksComplete`, then
  `DeleteToRecycleBin`.
- `Move`/`DeleteTree`/`DeleteToRecycleBin` still judge a junction by its TARGET, so the ancestor rule refuses to
  move or delete a Ctrl+dropped link to the OneDrive root, `C:\Users` or `AppData`. Making those link-aware (as
  `Rename` and `Unlink` already are) is safe for `DeleteTree`/Recycle and for a same-volume `Move`.
- `RackWindow.Menu.cs` (remove rack on the Desktop) ignores the `DeleteEmptyDirectory` result: the rack is removed
  but the folder may remain although the dialog said it would be deleted.
- `UninstallCleanup` and `WorkspaceReturn` treat "link count above 1" as "the data survives elsewhere" but do not
  know where the other name lives (it could be in `$Recycle.Bin`), and `WorkspaceReturn` does not compare file
  identity (volume serial + file index) with the Desktop file.
- `Unlink` and `DeleteEmptyDirectory` act by path after their checks; a handle-based delete closes the
  check-to-delete window (P11).

### Follow-ups recorded by the P1 review

- ~~**P2:** refuse an ancestor of a protected folder~~ Done in P2 (see above).
- **P11 or later:** `SafeMove.CopyThenDelete` cleanup after a failed copy should delete only what this call
  created (CreateNew / a did-not-exist flag), because of a TOCTOU window; the audit sink does synchronous I/O on
  the calling (often UI) thread, so consider a background queue; `MoveChecked` runs `SafeMove.Check` twice (once
  directly, once inside `TryMove`); reason texts are English only.

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
