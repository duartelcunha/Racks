# Hardening roadmap (closed out at P13)

Goal: move Racks from "works" to "high confidence" for a Windows desktop app that moves real user files.
Order was: **file safety first, then architecture, then tests.** No new features while this ran.
Each phase is one PR, stacked on the previous one, and says what was refactored, which risk went down and
which tests were added.

This file is self-contained: another person or agent can resume from it without any chat or any file outside
the repository. Sections: [Where we are](#where-we-are), [The plan in one page](#the-plan-in-one-page),
[Phases and PRs](#phases-and-prs), [Final numbers](#final-numbers), [What remains open](#what-remains-open),
[Manual verification checklist](#manual-verification-checklist-for-the-owner), [Merge order](#merge-order),
[How to resume](#how-to-resume), then the per-phase notes (P1 to P12) as history.

## Where we are

- **Status:** P0 to P13 are done and in review as PRs #41 to #54 (#54 is this documentation sync, the last phase), on
  branch `hardening/p13-final-docs` (stacked on `hardening/p12-deps-deadcode`). **Nothing of this is on `main` yet.**
- **Next step:** the owner runs the manual checklist below, then merges the chain in order (see "Merge order"). The
  work that remains after that is not part of the plan; it is the list in `docs/SECURITY-INVARIANTS.md`, section
  "Riscos aceites e abertos" (R-01 to R-28). Of those, R-01 (a folder move to another volume is refused), R-06 (a rack on a
  drive root is lost on restart) and R-13 (a startup failure leaves a windowless process) are the ones worth a phase of their own.
- **Base branch:** `feature/scandi-branding-and-stability` (4 commits on top of `main`; `main` is an ancestor of it). Phase
  branches are `hardening/pN-<name>`; each PR targets the previous phase's branch until the base lands on `main`.
- **Baseline (P0):** `dotnet test` = 493 passed, 1 skipped (`Category=ReleaseGate`, only runs in the release workflow), 0
  failed; the build had 2 distinct warnings; `dotnet format whitespace --verify-no-changes` was failing on the base branch
  (trailing spaces in `RackWindow.DragDrop.cs`), fixed in P0.
- **End of P13:** `dotnet test Racks.sln -c Release` = **1874 tests, 0 failed; the skipped count varies by machine (1 to 14: symlink rights, second drive, short names, SendTo, ReleaseGate)**; build 0 warnings.

## The plan in one page

(The original plan lived outside the repository; this is everything of it that still matters.)

- **Principle:** the risk is data loss in the filesystem, shell and registry, not the network. So: first make every
  destructive file operation go through one audited gateway, then split the big classes behind interfaces, then fill
  the test gaps. No new features, no mass `catch {}` cleanups, no mass `Debug.WriteLine` conversion.
- **Rules for every phase:** one phase = one PR, stacked; behavior tests before moving the code they watch (a test that
  reads source text is replaced by a behavior test first); saved racks must load identically (golden fixtures written by the
  old code); `Guards/*.json` ratchets only go down, in the same PR that lowers them; the update path stays fail-closed;
  every PR says what was refactored, which risk went down and which tests were added, plus the local checks it ran.
- **Phase goals:** P0 map and notes; P1 `FileOperationBroker` + `PathPolicy` + audit + guard; P2 route the remaining
  raw mutations and refuse ancestors of protected folders; P3 `ShellOperationGateway`; P4 `AutoRouteService` +
  `RoutingPolicy` and a hardened watcher; P5 typed `AppSettings`/`MigrationMarkers`; P6 `RackSettingsSchema` + validator +
  repository; P7 decompose `MainWindow`; P8 decompose `App`; P9 layout import/export split with limits and recoverable
  rollback; P10 `UpdateService` split; P11 adversarial filesystem tests on real NTFS; P12 dependencies and dead code; P13
  documentation sync.
- **Constraints kept throughout:** `UpdateManifest.cs` and `UpdateTrust.cs` keep their paths (`tools/RacksSigner` and the
  release scripts link them); `release.yml` expects exactly one `Category=ReleaseGate` test; the `MainHwnd` registry value
  (REG_SZ, decimal) and the `WM_RACKS_*` messages are used by `tools/*E2ERunner.ps1`.
- **Done means** (all met, with the open items listed in the invariants file): no user-data filesystem mutation outside the
  broker (a guard fails otherwise); presentation does not touch the registry, file mutation, shell notifications or the updater;
  all registry reads/writes go through the schema and repository, and bad values are never applied and always logged;
  auto-route, import and update have a validated policy and adversarial tests, and the update path is still fail-closed;
  `MainWindow` and `App` carry no domain logic (ratchets); the invariants and architecture docs match the code.

## Phases and PRs

All PRs are open (none merged). Each PR's base is the previous phase's branch; P0's base is the feature branch above.
Titles are abbreviated; all are stacked in this order.

| # | Phase | PR | Branch | Base | Status |
|---|---|---|---|---|---|
| P0 | Base, `ARCHITECTURE.md`, `AGENTS.md`, this file | #41 | `hardening/p0-base` | `feature/scandi-branding-and-stability` | in review |
| P1 | `FileOperationBroker`, `PathPolicy`, `AuditLog`, file-mutation guard; fix inline rename and Magic Organize folder names | #42 | `hardening/p1-file-broker` | `hardening/p0-base` | in review |
| P2 | Route the remaining raw file mutations through the broker; shrink the allowlist; refuse ancestors of protected folders | #43 | `hardening/p2-route-raw-fs` | `hardening/p1-file-broker` | in review |
| P3 | `ShellOperationGateway` (shell notifications, desktop icons) | #44 | `hardening/p3-shell-gateway` | `hardening/p2-route-raw-fs` | in review |
| P4 | `AutoRouteService` + `RoutingPolicy`, testable `DesktopRouter`, watcher hardening | #45 | `hardening/p4-auto-route` | `hardening/p3-shell-gateway` | in review |
| P5 | Typed `AppSettings` and `MigrationMarkers` | #46 | `hardening/p5-app-settings` | `hardening/p4-auto-route` | in review |
| P6 | `RackSettingsSchema` + validator + repository (golden fixtures first) | #47 | `hardening/p6-rack-settings` | `hardening/p5-app-settings` | in review |
| P7 | Decompose `MainWindow` (Magic Organize, hot corner, double-click hide, hotkeys, tray switches, update glue, display debounce) | #48 | `hardening/p7-mainwindow` | `hardening/p6-rack-settings` | in review |
| P8 | Decompose `App` (bootstrap, migrations, crash reporting, uninstall mode) | #49 | `hardening/p8-app-bootstrap` | `hardening/p7-mainwindow` | in review |
| P9 | Layout import/export: parser, validator, policy, executor | #50 | `hardening/p9-layout-import` | `hardening/p8-app-bootstrap` | in review |
| P10 | `UpdateService` split (behavior tests first; stays fail-closed) | #51 | `hardening/p10-update-service` | `hardening/p9-layout-import` | in review |
| P11 | Adversarial filesystem tests and the fixes they justified (P11a tests, P11b follow-ups, P11c F2/F3/F7) | #52 | `hardening/p11-adversarial-tests` | `hardening/p10-update-service` | in review |
| P12 | Dependency notes, patch bumps (Microsoft.ML 5.0.1, Svg 3.4.8), dead code, zero warnings, two flaky tests fixed | #53 | `hardening/p12-deps-deadcode` | `hardening/p11-adversarial-tests` | in review |
| P13 | Final documentation sync (this change): invariants index and risks, architecture, README, CONTRIBUTING, SECURITY, notices, changelog, this roadmap | #54 | `hardening/p13-final-docs` | `hardening/p12-deps-deadcode` | in review |

Test count per phase (passed / skipped): P0 493 / 1; P7 1320 / 4; P8 1376 / 4; P9 1536 / 4; P10 1651 / 4; P11 1868 / 6 (the
4 old skips plus 2 symlink tests that need the right to create symbolic links); P12 and P13 1874 tests (the skipped count varies by machine).

## Final numbers

- **Tests:** 1874 tests, 0 failed; the skipped count varies by machine (1 to 14: symlink rights, second drive, short names, SendTo, ReleaseGate) (`dotnet test Racks.sln -c Release`). The adversarial filesystem set
  (`dotnet test --filter "Category=Adversarial"`) takes about 3 s alone.
- **Build:** 0 warnings (was 2 at P0, 8 before P12).
- **File-mutation allowlist** (`Guards/fs-allowlist.json`): **11 files, 36 calls**, all Racks-owned state or a non-destructive
  copy (it went 44 -> 45 during the P1 review, when the guard became stricter, and 32 -> 36 in P9; was 45 calls in 12 files before P2; 32 in 9 after P2; 36 in 10 after P9, the one increase, for the atomic export and the
  recovery folder; 36 in 11 after P10, when `Util/Updater.cs` 3 became `InstallerExecutor` 1 and `UpdateCleanupService` 2).
- **Presentation boundary:** 0 direct shell references; registry allowlist in presentation: 0 (empty).
- **Size ratchets** (`Guards/baseline.json`): `rackWindowLines` 561, `rackWindowPartMaxLines` 911, `rackWindowTotalLines` 5925,
  `mainWindowLines` 404, `mainWindowMethodMaxLines` 59, `appLines` 38, `controllerUsages` 20, `xamlLiterals` 119.
- **Dependencies:** no vulnerable package (`dotnet list package --vulnerable --include-transitive` on Racks, Racks.Tests and
  RacksSigner); only test tooling is outdated on purpose (`Microsoft.NET.Test.Sdk` 17.14.1, `xunit.runner.visualstudio` 3.1.4).
  `THIRD-PARTY-NOTICES.md` lists every package that ships (checked against the Release build's `Racks.deps.json`; closed in P13).

## What remains open

Not repeated here. Everything open or accepted, with severity and recommendation, is in
[`SECURITY-INVARIANTS.md`](SECURITY-INVARIANTS.md), section **"Riscos aceites e abertos"** (R-01 to R-28): the open P11
findings (F1, F4, F5, F6, F8), drive-root racks, the virtual-rack cross-volume duplicate, link-aware Move/Delete,
the `WorkspaceReturn` identity check, the audit log (synchronous I/O, retention), handle-based `Unlink`, the shell
verb gate (M3), the windowless process after a startup failure, the numeric windows in `RackSettingsSchema`, the downgrade
caveat, what the guards cannot see, and the manual verification that no phase could do.

Not in that list because it is not a risk: `gitleaks`/CodeQL in Actions, `Directory.Packages.props`, converting every
`catch {}` and `Debug.WriteLine` (out of scope on purpose), and a `Microsoft.NET.Test.Sdk` 18 / `xunit.runner.visualstudio`
4 upgrade.

## Manual verification checklist (for the owner)

No phase from P5 to P12 started the real app, `test/Test-Racks.ps1` or `tools/dev-tools.ps1 e2e` (they kill the running
Racks and drive the real Desktop), so the real startup, tray, hotkeys, mouse hook, dialogs and installer are covered only
by the logic behind them. Do this once on the tip of the chain (P13 or the merged `main`), on a machine where it is fine
to move a few files, ideally a throwaway Windows profile for the last step. Stop and report at the first thing that
differs.

1. **Start and tray.** Build and run (`dotnet run --project Racks/Racks.csproj`) or install the build. Racks starts with
   no crash toast and no `crash.log`; the tray icon appears; right-click it and the menu opens; start a second copy: it
   exits at once (single instance).
2. **Hotkeys.** `Ctrl+Shift+N` makes a new rack; `Ctrl+Shift+Space` opens Quick Finder, typing narrows the list, `Enter` opens
   an item; if another app owns a hotkey you see a notification, not a crash.
3. **Rack lifecycle.** Add a rack (tray menu), drag a file from a folder into it (the file moves in), rename the item with
   `F2` (try a plain name; try `..\x` and `a:b`: both must be refused with a message and nothing moves), drag the file
   back out to the Desktop (one file, no duplicate), then remove the rack (the files are on the Desktop).
4. **Auto-route.** Give a rack the name pattern `\.pdf$`. Save a PDF on the Desktop: it appears in the rack. Download
   something in the browser: while it is `.crdownload`/`.part` it stays on the Desktop and moves when the browser
   renames it. A bad pattern in the rule dialog is refused with a message. `%AppData%\Racks\audit.log` has a line
   per decision.
5. **Magic Organize and undo.** Run it on a Desktop with a few files in "Racks" mode, confirm, check the racks; undo;
   the files are back and the racks are gone. Repeat once in "Folders" mode.
6. **Layout export and import.** Tray menu, "Export racks...": save to a file (the file appears). Change a rack. "Import racks..."
   (it replaces all current racks) with that file: the old state returns and any value left out is listed in the message.
   Then import a text file that is not a layout: a message, and nothing changes.
7. **Settings toggles.** Flip each tray/Settings switch and check the effect: ice physics, hide desktop icons (and that the
   icons come back on exit), double-click wallpaper to hide/show, hot corner, lock all racks, theme submenu, language
   preview (`RACKS_LANG`).
8. **Start on login.** Turn it on: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has a `Racks` value; turn it off: the
   value is gone. Sign out and in once with it on.
9. **Update check without installing.** Tray menu, "Check for updates" (and, in Settings, "Auto update" on then restart). Expected
   on a build with the real key: "up to date" or an offer; do not press Install. Anything else is a bug report. Nothing may
   run from `%TEMP%\Racks-update-*`.
10. **Uninstall (throwaway profile only).** Install, put a file in a rack, uninstall from Apps and Features. The file is on
    the Desktop (never overwritten; name clashes get "(from Racks)"), `audit.log` and `crash.log` are gone,
    `%AppData%\Racks\recovery` stays if it existed, and the farewell animation plays.

11. **Upgrade in place.** Over a real 1.4.0 install with existing racks, on a pt-PT machine: install this build; the racks load
    unchanged and `audit.log` has no fallback noise (P6 changed the loader and the save format, see R-15).
12. **Drag-out variants.** From a Desktop rack; from a virtual rack to another drive (expect the R-07 duplicate); a folder; and
    dropping a folder from another drive into a rack (expect the R-01 refusal, nothing moved).
13. **Remove-rack variants.** A virtual rack with a file open (the rack is kept, with a message); a folder rack on the Desktop.
14. **Ctrl+drop links.** Hardlink, junction and shortcut; then remove the rack and uninstall: the links are removed and the
    targets are intact.
15. **One real update install** from the previous signed build to a signed staging build: launch, shutdown, the single-instance
    lock is released, relaunch, and `%TEMP%\Racks-update-*` is cleaned. Do it before the next release at the latest.

Then run the automated pass if the machine can be driven: `.\test\Test-Racks.ps1` and `.\tools\dev-tools.ps1 e2e`.

## Merge order

The PRs are stacked: **#41 -> #42 -> ... -> #54**, each PR's base being the previous branch, and #41's
base being `feature/scandi-branding-and-stability`, which has no PR of its own and is 4 commits ahead of `main`.

1. Open (or merge directly) the feature branch into `main` first, because `main` is an ancestor of it. Until then every
   PR targets another branch, not `main`.
2. Merge **in order**, #41 first. When a merged branch is deleted, GitHub retargets the PR that was based on it; if it
   does not, use `gh pr edit <n> --base <branch>` (or retarget in the UI) before merging the next. Do not squash out of
   order: a squash rewrites the base and the next PR will show the previous phase's commits again.
3. Checks: the required ones on `main` are `pr-title` and `gitleaks`; the full local list is under "Commands" below, and each
   PR body lists the commands it ran.
4. After the last merge, delete the merged `hardening/*` branches, tag nothing (a release is a separate process:
   `docs/RELEASING.md`), and move the "Where we are" section of this file to "done".

## How to resume

1. `git fetch --all`, then `git switch hardening/p13-final-docs` (the tip of the chain; or `main` once everything is merged).
2. Read, in this order: `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY-INVARIANTS.md` (index, guards, risks), this file.
3. Run the commands under "Commands" (all must be green: 1874 tests, 0 failed; the skipped count varies by machine (1 to 14: symlink rights, second drive, short names, SendTo, ReleaseGate); build 0 warnings).
4. Pick work from the risks table in `SECURITY-INVARIANTS.md`; the suggested next phases are R-01, R-06, R-13. One risk = one
   PR on top of the chain's tip, with the test that flips (a `Known_gap_...` test is meant to be changed on purpose when its
   gap is fixed).
5. Keep the rules: broker for files, `AppSettings`/`RackSettingsRepository` for the registry, `ShellOperationGateway` for the
   shell, behavior tests, ratchets only go down, the update path stays fail-closed. `CONTRIBUTING.md` has the PR sections.

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
- Allowlist at the end of P2, historical (today's is under "Final numbers"; was 45 calls in 12 files, then 32 calls in 9 files; every entry is Racks-owned state or a
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

### P5 in short (what changed)

- New `Racks/Infrastructure/Registry/`: `IRegistryRoot` (read/write/delete values under one key; `HkcuRegistryRoot` is the
  real one and still follows `InstanceController.appName`), `AppSettings` (`Default`) and `MigrationMarkers`
  (`Default`). Registry-using call sites moved off `RegistryHelper.*Root`: `MainWindow` (startup reads, five toggle
  handlers, `MainHwnd`, first-run welcome), `SettingsWindow` (auto update, double-click to hide), `App` (two markers;
  its unused `reg` field is gone), `FirstRunWelcome` (takes `MigrationMarkers`), `InstanceController` (blur read, two
  markers, `_MigratedFromDeskFrame`). `RegistryHelper` lost `KeyExistsRoot`, `AddToAutoRun` and `RemoveFromAutoRun`;
  the per-rack methods, `ReadKeyValueRoot` and `WriteToRegistryRoot` stay for `Instance` (P6).
- Stored forms are byte-compatible with what the old code wrote and read (tests compare against the old helper): switches
  are REG_SZ `True`/`False`; **`MainHwnd` is REG_SZ with the decimal handle, not a QWORD** (`SetValue(name, (long)x)`
  stores text; the E2E runner parses it); `_MigratedFromDeskFrame` is REG_SZ `1`; the other markers REG_SZ `True`.
  A marker counts as set when any value of that name exists.
- Read rules: missing = default, silently; a DWORD/QWORD/binary/garbage/empty value or an unreadable registry = default plus
  one `SettingFallback` audit record per name (field, raw value, reason, fallback) in the existing `audit.log` line
  format (no new fields). Writes return `bool`; a failure is a `SettingWriteFailed` record each time. The UI still
  carries on as before (for example the start-on-login switch is saved even if the Run entry failed). IcePhysics defaults ON.
- Behavior kept on purpose: the constructor-time `IsChecked` assignments in `MainWindow` still fire their handlers and
  re-write values at startup; `HideDesktopIcons` and `blurBackground` are nullable (null = never saved, the caller skips,
  as `KeyExistsRoot` did).
- Guard: `RegistryAccessGuardTests` ratchets registry references in presentation files with exact per-file counts in
  `Guards/registry-presentation-allowlist.json` (4 files, 5 references; all per-rack or reg.exe, P6/P8). One unused
  `RegistryHelper` local in `RackWindow` was deleted (`rackWindowTotalLines` 5935 to 5934).
- Not done on purpose: `Instance`'s global style defaults (`ReadKeyValueRoot` list and the `WriteToRegistryRoot` for
  default-mode racks) and all `Instances\*` code are P6; `Test-Racks.ps1` and the dev-tools e2e were not run. The
  autorun test uses a throwaway Run key (the real one is never touched by tests).

### P6 in short (what changed)

- Two commits. **P6a** (tests only, plus one mechanical extract-method): golden fixtures with a non-default value for every one of
  the 67 persisted names, written by the bulk writer, by the incremental write-through and as hand-written raw values (REG_DWORD,
  REG_QWORD, REG_SZ, REG_MULTI_SZ; en-US, pt-PT, de-DE text), read by the real loader; the source-scraping guards
  (`InstanceKeyTests`, the allow-list switch scan, the `SafeWpfValues` text pin on `Instance.cs`) became behavior tests.
  The load half of `InitInstances` moved unchanged into `LoadInstancesFromRegistry` so a test can run it without windows.
  **P6b**: the schema, validator, mapper and repository below.
- New in `Racks/Infrastructure/Registry/`: `RackSettingsSchema` (the one table), `RackSettings` (typed DTO), `RackSettingsValidator`,
  `RackSettingsMapper`, `IRackRegistry`/`HkcuRackRegistry` (seam for `Instances\*`), `RackSettingsRepository` behind
  `IRackSettingsStore`. `InstanceController.InitInstances` delegates to `LoadAll()` and keeps the post pass; `WriteInstanceToKey`,
  rename, `Instance.OnPropertyChanged` and the 32-default read of the `Instance` constructor use `Instance.Store` (static, replaceable
  in tests). The three hand-maintained lists (the `switch`, the `SetValue` list, `RackLayoutIO._allowedValues`) are gone.
  The test assembly wraps the default store so a test that forgets to redirect `InstanceController.appName` throws instead of
  writing `HKCU\SOFTWARE\Racks`.
- Routed through the repository: rack removal (`RackWindow.Menu`), Magic Organize undo (`MainWindow`), "reset default style"
  (`SettingsWindow`) and the uninstall report's folder-to-title map (`App`). `registry-presentation-allowlist.json` is empty;
  `controllerUsages` went 30 to 20 (27 real uses before). `RackLayoutIO`'s own five registry accesses were removed in P9.
  `InstanceController.RefreshMirror` still reads `MainWindow._controller` (P7).
- **Deliberate behavior changes:** one rack that fails no longer stops the others (and no rack of the session is lost with it);
  loading never writes (before, every non-default value was written back while reading, flipping REG_DWORD to REG_SZ and rewriting
  doubles in the current culture); NaN/Infinity/overflow, an undefined `TitleTextAlignment`, a bool such as `1`, and numbers outside
  the documented window are refused, audited, and the default applies (`IconSize` and `GridSize` are clamped as their setters do);
  a bad item in `ShowOnVirtualDesktops`/`CustomOrderFiles` drops only that item (one bad item used to drop the whole virtual-desktop
  list); a rack with no (or an empty) `Name` takes its key name (it used to get an empty key location), a rack with an unsafe name is
  skipped with its data left untouched, a rack named `empty` is skipped without deleting its key (loading it used to delete the key);
  the global defaults are validated one by one (the first bad value used to skip all later ones) and `IconSize` is clamped;
  `Folder`/`BackgroundImagePath` containing control characters are refused (a NUL in `Folder` used to abort the whole load);
  `Enum.TryParse` combinations such as `Left, Right` are refused; `IsShortcutsOnly` is no longer written to the rack key (it was
  never read back); the junk text `System.Collections.Generic.List...` is no longer written to the root by a "Default Style" instance.
- **Culture and downgrade.** Doubles (`PosX`, `Width`, `IdleOpacity`, ...) are written as invariant text (`123.5`) whatever the
  culture. The reader tries invariant text first, then the current culture: invariant text is read correctly in every culture;
  legacy culture text (`123,5`) is read correctly only in the culture that wrote it. **A downgrade is unsafe** (an older build
  misreads or ignores the dot numbers): do not downgrade. Details and the affected cultures: R-15 in `docs/SECURITY-INVARIANTS.md`
  and the CHANGELOG. Integers stay REG_DWORD (the write-through used to store them as REG_SZ), switches stay `True`/`False`, lists
  as before; the global defaults stay REG_SZ text (an older build reads the root with a text reader).
- **Audit volume and normalisation (review fixes).** A load no longer writes normalised values back, so the same finding would repeat on every
  start: findings are logged once per process per (rack key, field, raw value). A rack whose ONLY findings are benign (a stored
  number clamped, a missing/empty `Name`) is saved once in full, explicitly, as `SettingNormalized`, so the registry is corrected
  once and later loads are silent; a rack with any refused value or dropped list item is never rewritten (the user's raw data stays
  until they change it). A `Name` that differs from its key only in case (a case-only rename) is not a finding. A hand-edited rack
  whose `Name` differs from its key in more than case gets its one-shot migrations in memory only (a write would create a
  skeleton key under that name that loads as a "folder missing" ghost).
- Behaviour note: `ShowOnVirtualDesktops` `1,abc` now becomes `[1]` (it used to be dropped whole, meaning "all desktops").
- Follow-up (pre-existing, not fixed): a rack whose folder is a drive root has the name `""` and is never persisted; investigated
  in P11b (`DriveRootRackTests`). Now R-06 in `docs/SECURITY-INVARIANTS.md`.
- Kept on purpose: the two one-shot migrations still write through the setters after a load; a full save leaves an empty
  `LastAccessedFiles`/`CustomOrderFiles` value alone while a single-value write stores the empty list (and `AssignedFiles` is removed
  when empty), exactly as before; `BackgroundImagePath` is stored as typed (the local-drive check stays where the image is loaded);
  a `Name` that differs from its key is used as is (audited), so a later save writes under that name.
- Not done on purpose: `Test-Racks.ps1` and the dev-tools e2e were not run; `RackLayoutIO.Import/Export` still touched the registry
  directly (closed in P9); `MoveFilesOnDrop` is still importable although it is discarded (P9: accepted and not stored).

### P7 in short (what changed)

- `MainWindow.xaml.cs` went from 885 to 420 lines (longest method about 150 to 59; ratchets `mainWindowLines` 420 and
  `mainWindowMethodMaxLines` 59 in `Guards/baseline.json`, plus "it is one file": no `MainWindow.*.cs` partial). It builds the
  services by hand (no container), forwards events and shows windows and dialogs. Pure refactor: same order, threads, timing,
  messages and registry values.
- New in `Racks/UseCases/` (namespace `Racks.UseCases.*`): `MagicOrganize/MagicOrganizeService` (analyze, preview, Racks mode,
  Folders mode, undo; talks to the UI through `IMagicOrganizeHost`), `HotCorner/HotCornerService` (the 100 ms poll as a two-state
  machine; `IRepeatingScheduler` + `IHotCornerProbe` injected), `DoubleClickHide/DoubleClickHideService` (hook life + the pure
  decision; `IGlobalMouseHook`, `IDesktopHitTest`), `Hotkeys/HotkeyController` (register, conflict report through a callback,
  dispatch table, unregister; `IHotkeyApi`), `Tray/TrayToggleService` (what each tray switch does once flipped, over
  `AppSettings` and the shell gateway; `IRackWindowControl`), `Updates/UpdateCheckService` (20 s background check, manual check,
  1 min clean-up of old download folders; `Util/Updater` was untouched in P7 and replaced in P10), `Display/DisplayChangeDebouncer` (the
  WM_DISPLAYCHANGE debounce).
- Infrastructure side: `Infrastructure/Shell/GlobalMouseHook` + `DesktopHitTest` (the only code that touches H.Hooks, `WindowFromPoint`
  and UI Automation), `Win32HotkeyApi`, `ScreenHotCornerProbe`. `Core/Abstractions` gained `IRepeatingScheduler` /
  `DispatcherRepeatingScheduler`. Presentation adapters in `Views/`: `MagicOrganizeHost` (preview dialog, message boxes, rack windows
  and the 100 ms stagger), `ControllerRackWindows`, `HotkeyConflictToast`, `SingleInstanceWindow<T>` (settings, help, dev
  diagnostics), `TrayIconRegistrar`. `MagicOrganizeUndo.RestoreFiles/RemoveCreatedFolders` take an optional broker/gateway for tests.
- **Kept on purpose, now documented and tested:** the constructor of `MainWindow` still assigns `IsChecked` on the tray switches
  from the saved values, which fires their handlers, so each handler runs once at startup and re-writes what it just read (a
  fresh install gets `IcePhysics=True` in the registry on the first start). The `WM_RACKS_TOGGLE_*` messages set `IsChecked` (the
  event runs the handler) AND call the handler by hand (`FlipAndRun`), so the handler runs twice per message; every effect is
  idempotent (the hot corner starts once), `TrayToggleServiceTests` pins both, including a real `Wpf.Ui.ToggleSwitch`.
  `OnSourceInitialized` runs inside the constructor (`EnsureHandle` raises it, measured), before `_controller` exists, so the services
  that WndProc and the hotkeys need are built in `ComposeHostServices()` BEFORE `EnsureHandle`, and the ones that hold the controller
  after it (`ComposeRackServices()`).
- Quirks found and kept (not fixed in a refactor): the second display message is tested as `0x001C`, which is `WM_ACTIVATEAPP` (the old comment
  called it WM_WININICHANGE, 0x001A); Magic Organize asks `Directory.Exists(source)` AFTER the move to tell the shell whether the item
  was a folder, so a moved folder is announced as a file; without Explorer the desktop hit test compares 0 with 0 and passes.
- Removed: the unused `MainWindow._mainWindow` static and the `startOnLogin` field. `controllerUsages` stays 20 (the uses in
  `MainWindow.xaml.cs` are the bare `_controller`, not counted by that regex); `MainWindow._controller` itself stays public.
- Tests: 1225 to 1320 passed (+95, 4 skipped): fakes for time (`ManualRepeatingScheduler`), the registry (`InMemoryRegistryRoot`), the
  shell and the UI; Magic Organize runs against temp folders and a throwaway registry key.
- Guards: `PresentationBoundaryTests.MainWindow_delegates_instead_of_doing_the_work` (no file/registry/updater/hook/hotkey/cursor
  code in `MainWindow.xaml.cs`, at most one `DispatcherTimer`).
- Not done on purpose: `Test-Racks.ps1` and the dev-tools e2e were not run (they kill the running Racks and drive the real desktop);
  no real tray, hotkey, mouse hook, display change or Explorer was exercised, only the logic behind them.

### P8 in short (what changed)

- `App.xaml.cs` went from 293 to 38 lines (ratchet `appLines` 38, and "it is one file"). It builds the services in its constructor
  (`Infrastructure/Startup/AppComposition`, no container) and delegates `OnStartup`/`OnExit`.
- New: `UseCases/Startup/` (`AppBootstrapService` = the single ordered step list per mode, `StartupMigrationService`, `UninstallMode`
  + `RackTitles`, `SingleInstanceGuard`, `ProcessConfiguration`), `UseCases/Crash/CrashReportingService`, and in
  `Infrastructure/Startup/` the WPF/Windows side (`AppStartupHost`, `ThreadWatchdog`, `MutexHandle`, `TaskDispatcherDelayScheduler`).
- The test that read `App.xaml.cs` as text (`SafeSvgTests.The_app_applies_the_svg_settings_at_startup`) was replaced by a behavior
  test over the step list BEFORE the code moved. Order is in the `AppBootstrapService` summary and in `ARCHITECTURE.md`.
- Deliberate differences (both only remove a defect): (1) `InitializeWorkspace` now runs before `StartupUri`/base `OnStartup`
  (it used to run right after them, still inside `OnStartup`, so before MainWindow existed either way); the first-run marker is
  read after base `OnStartup` as before. (2) A refused second instance no longer calls `ReleaseMutex` on a mutex it never owned
  in `OnExit` (that threw, and the dispatcher handler logged a bogus `crash.log` entry and showed the crash toast).
  The 500 ms animation delay is still a thread-pool wait followed by a UI-thread invoke.
- Crash log format and the registry marker names/values are unchanged (`CrashLogTests`, `MigrationMarkersTests` untouched).
  No structured fields were added to the log: the entry already names its source.
- Tests: 1321 to 1376 passed (4 skipped): ordering, short-circuit and idempotent `Exit`; crash service (handled/observed flags,
  uninstaller skip, one toast, entry text); migration service with a throwaway registry root and `ManualScheduler`; uninstall mode
  (argument parsing, injected watchdog, titles from a throwaway rack registry, report). No test starts a real mutex.
- Not done on purpose: the real app was not started (`Test-Racks.ps1` and the dev-tools e2e were not run), so the real startup path
  (mutex, toast activation, startup animation, uninstall runs) is covered by the unit tests of the order and the logic only.

### P9 in short (what changed)

- `Util/RackLayoutIO` (327 lines of parsing, validation, security policy, registry snapshot/delete/write/rollback, dialogs and file
  I/O) is split into `Racks/UseCases/Layout/`: `LayoutParser`, `LayoutValidator`, `LayoutSecurityPolicy`, `LayoutLimits`,
  `LayoutImportExecutor`, `LayoutExporter`, `LayoutService`, `LayoutMessages`, `LayoutAudit`; `Infrastructure/Layout/LayoutFileWriter`
  (atomic export); `Views/LayoutDialogs` (save/open dialogs and the messages). `RackLayoutIO` stays as a 30-line `internal` facade
  (`ParseAndValidate`, `IsSafeImportedPath`, `AllowedValueNames`) because `RackLayoutImportValidationTests` and
  `RackSettingsSchemaTests` pin the accepted and refused sets through it. Its five direct `Registry.CurrentUser` uses are gone: the
  executor works on `IRackRegistry` (new default method `ReadValuesExact`, REG_EXPAND_SZ unexpanded, used for snapshots).
  `MainWindow.xaml.cs` went from 420 to 404 lines (ratchet `mainWindowLines` 404).
- Risks that went down: a layout file can no longer be arbitrarily big, deep or long (INV-IMPORT-5); a numeric value out of range,
  NaN or Infinity is refused instead of being written; the restore after a failed replace is checked and a failed restore is no
  longer swallowed (result `RollbackFailed`, audit record `LayoutImportRollbackFailed`, a message to the user); every import and
  export failure is shown to the user and audited (before, only `InvalidDataException` was shown and the rest went to
  `Debug.WriteLine`); the export is written atomically, so a failure keeps the previous file.
- Deliberate differences from before: (1) `Version` newer than 1, or below 1, is refused; a missing one reads as 1. (2) Values go
  through `RackSettingsValidator`. After the review, old backups must stay importable, so a bad number, switch, enum or list
  (out of range, NaN/Infinity, not True/False, undefined, over the list caps) does NOT refuse the file: that value is dropped as
  the loader would (a number the loader clamps, `IconSize` 5, is clamped; a list keeps its good items), one `LayoutValueDropped`
  audit record per value and a message listing them. The security rules still refuse the whole file (names, paths, fonts, colors,
  NUL, size, depth, counts, version, duplicates, Name mismatch, regex over its cap). (3) Two racks whose names differ only in case
  are refused; a `Name` value must equal the rack name (case aside); a rack name with a control character is refused; a repeated
  JSON property in one object is refused. (4) Text with a NUL and over-long regexes are refused. (5) Stored kinds now follow the
  repository writer: integers REG_DWORD (were REG_QWORD), switches `True`/`False`, decimals invariant text; the legacy
  `MoveFilesOnDrop` is accepted but not stored. (6) Replace mode deletes each rack key (it used to delete the whole `Instances`
  tree, so a stray non-rack subkey is now left). (7) Merge mode is also snapshotted and rolled back. (8) The export is built from
  the typed racks (`RackSettingsRepository.LoadAll`): a value the loader refuses is not exported, the `Name` value always equals
  the rack name, and an export that Racks would not import back (too big, a list over its caps, duplicate names) is refused with
  the rack and the value named. (9) A file with no racks says so instead of doing nothing. (10) A failed import whose restore also
  fails writes a recovery layout (`%AppData%\Racks\recovery\layout-<date>.json`, newest 5 kept) and the message and audit record
  name it. New texts are in `Lang.resx` (English; other languages fall back).
- Review fixes (second commit): the rollback only touches the keys the run reached (a failure at the first `DeleteKey` touches no
  other rack); `File.Replace` is gone from the export (one `File.Move(overwrite)`, temp deleted only when the destination is
  confirmed intact, short temp name for 255-character file names); `AllowDuplicateProperties = false`; dialog exceptions are audited.
- Tests: 1381 to 1536 passed (4 skipped): parser (size, depth, count, BOM/encodings, garbage, repeated properties), validator
  (every new rule and message, dropped values), executor (in-memory registry that fails on command with every exception type,
  precise rollback, recovery file that re-imports), full `Import()`/export/round trip/old-format file/rollback of every REG_ kind
  over the throwaway registry root, atomic export, message texts. One existing test needed one argument
  (`Every_name_the_loader_applies_is_accepted_by_import_with_its_stored_form` now names its rack "Golden", because a `Name` value must match).
- Guards: `fs-allowlist.json` `Util/RackLayoutIO.cs` 1 became `Infrastructure/Layout/LayoutFileWriter.cs` 3 and
  `Infrastructure/Layout/RecoveryFolder.cs` 2 (the one allowlist increase of the whole plan, 32 to 36 calls, each call justified
  in the file); `baseline.json` `mainWindowLines` 420 to 404.
- Not done on purpose: the real app, dialogs, `Test-Racks.ps1` and the dev-tools e2e were not run (the dialogs are covered by the
  message tests only); a rack key's nested subkeys (the app writes none) are not copied by the snapshot and not compared by the
  restore check.

### Follow-ups recorded by the reviews: where they went

The P1, P2 and P8 reviews recorded follow-ups. The ones still open are risks in `docs/SECURITY-INVARIANTS.md` ("Riscos aceites
e abertos"); the ones that were done are listed here.

| Follow-up (review) | Outcome |
|---|---|
| P1: refuse an ancestor of a protected folder | Done in P2 |
| P1: `SafeMove.CopyThenDelete` cleanup should delete only what the call created | Done in P11b |
| P1: `MoveChecked` runs `SafeMove.Check` twice | Done in P11b (once, through `SafeMove.MoveAfterCheck`) |
| P1: the audit sink does synchronous I/O on the calling thread; reason texts are English only | Open: R-10 |
| P2: orphan shortcut no longer deleted in a virtual rack after a cross-volume Explorer Move | Open: R-07 |
| P2: `Move`/`DeleteTree`/`DeleteToRecycleBin` judge a junction by its target (Ctrl+dropped links to OneDrive, `C:\Users`, `AppData`) | Open: R-08 |
| P2: `RackWindow.Menu.cs` ignored the `DeleteEmptyDirectory` result when removing a Desktop rack | Done in P11b (`Rack/RackFolderRemoval`) |
| P2: "link count above 1" and no file-identity check in `UninstallCleanup`/`WorkspaceReturn` | Open: R-02 (confirmed as F4 in P11) |
| P2: `Unlink`/`DeleteEmptyDirectory` act by path after their checks | Open: R-11 (considered in P11, not done on purpose) |
| P8: startup failure after the single-instance name is taken leaves a windowless process | Open: R-13 |

### P10 in short (what changed)

- `Util/Updater.cs` (a static class with the toasts, the HTTP calls, the selection rules, the download and the process start in one
  file, with `InstallUpdate` untested) is gone. In its place: `UseCases/Updates/` (`UpdateService`, `ReleaseManifestValidator`,
  `UpdateMessages`, `UpdateAudit`, the four ports) and `Infrastructure/Update/` (`ReleaseCatalogClient`, `InstallerExecutor`,
  `UpdateCleanupService`, `UpdateHttpClientFactory`, `ToastUpdateNotifier`, `ShellInstallerLauncher`, `WpfAppShutdown`,
  `UpdateComposition`). `UpdateManifest.cs` and `UpdateTrust.cs` are untouched (RacksSigner links them by path); tools and
  workflows are untouched; the `ReleaseGate` test is untouched.
- Two commits: (a) seams (the notifier, launcher, shutdown, temp root, HTTP handler, key and version injected, production wiring
  the same) plus behavior tests for the whole check/download/verify/launch path, which replace the three tests that grepped
  `Updater.cs`; (b) the split. The behavior tests ran green on both sides of the split.
- Risk down: the release feed is now read with a 2 MiB cap, a 30 s timeout and a cancellation token, from this repository's URL only;
  every installer read has a 30 s timeout; the cleanup no longer deletes the empty folder of a download that is just starting (it skips
  folders younger than 10 minutes and ones with an open file); two installs cannot run at once; the pending offer is read and
  written under a lock and is used up when the installer starts; a toast that throws can no longer crash the dispatcher or stop
  an update; the running-version read is inside the try; the install download sends the running version as User-Agent (it sent the
  release tag); every check/install failure or refusal is an audit record (`UpdateCheckFailed`, `UpdateInstallRefused`,
  `UpdateInstallFailed`, `UpdateInstallStarted`) with URLs stripped of query strings.
- Unchanged on purpose: every user-visible text, the order of the checks, the redirect behavior (documented in INV-UPDATE-1),
  the hashing on the calling thread, and the toast arguments (`action=install_update`).
- Namespaces: `Racks.UseCases.Updates` (the existing folder name) and `Racks.Infrastructure.Update` (the name already in
  `ARCHITECTURE.md`).
- Tests: 1536 to 1651 passed (4 skipped): a fake HTTP handler, notifier, launcher and shutdown run the whole check/download/verify/launch path
  (`UpdateRig`); new files `UpdaterCheckBehaviorTests`, `UpdaterInstallBehaviorTests`, `ReleaseCatalogClientTests`, `InstallerExecutorTests`,
  `UpdateServiceTests`, plus cleanup tests in `UpdaterTempDirTests`. `Guards/fs-allowlist.json`: `Util/Updater.cs` 3 became
  `Infrastructure/Update/InstallerExecutor.cs` 1 and `UpdateCleanupService.cs` 2.
- Not done on purpose: no real download, no real installer and no real GitHub call were made (the tests use a fake handler, a temp
  folder and a fake launcher); `Test-Racks.ps1` and the dev-tools e2e were not run.

### P11 in short (what changed)

- Two commits. **P11a** is tests only (no production file touched): `test/Racks.Tests/Adversarial/`, 108 tests tagged
  `[Trait("Category","Adversarial")]` (2 of them need the right to create symbolic links; they run in the normal suite too;
  `dotnet test --filter "Category=Adversarial"` runs them alone, about 3 s, together with the 39 tagged ones of P11b). Everything is real NTFS in a temp tree with real junctions, hard links and a made-up non-link reparse point
  (stand-in for a OneDrive placeholder); the real Desktop/Documents/AppData and `HKCU\SOFTWARE\Racks` are never touched (a folder
  that must look protected is declared with `SafeMove.UseProtectedFoldersForTest`). **P11b** is the small production follow-ups
  below, each with its tests.
- **Covered by P11a:** `SafeDelete` (junction and junction chain inside a tree not followed, dangling junction, read-only/hidden/
  system files, deep >260 trees, unicode and symbol names, `x.` / `y ` entries created through `\\?\`, a locked file reported as
  `PartiallyCompleted` and finished on retry, Recycle Bin on a tree with a junction, on a junction and on symbol names, network paths
  refused); `SafeMove`/broker (protected folder by case, `\\?\`, `..`, 8.3 name, junction and 3-hop junction chain, ancestor through
  an alias, child of a protected folder allowed, into itself directly and through a junction alias, onto its own parent, missing
  parent, existing file/folder never overwritten in any kind or case, trailing dot/space destination, device-name destinations,
  symbols/emoji/unicode names, >260 paths, copy+delete fallback with a locked file, with a stuck original, with a link inside, deeper
  than the 64-level backstop, and a real move to the second drive when there is one); `DesktopRackClaims` (claims across 4 racks,
  ordinal case, removed rack, null list, `IsGone` with missing, file and dangling-junction parents); `WorkspaceReturn` (same name and
  same length with other content, hard links with equal and different length, the 1000-name cap both sides, hostile names, locked
  file, folder with a junction, link to a protected folder); `UninstallCleanup` (locked and nested hidden/system content, nested
  `desktop.ini`, clashes of file vs folder, the 1000-name cap, junctions to outside content in the workspace, in a sandbox, nested
  and dangling, junction to a protected folder, non-link reparse points returned and never unlinked, unicode paths, a second run);
  `RackMirror.RemoveJunctions` (only links go, a link inside a user folder is not reached, non-link reparse points stay, chains,
  dangling, crowds, unicode).
- **P11b, behavior:** (1) `SafeMove.CopyThenDelete` removes only what the call created: every file is claimed with `CreateNew`, every
  folder with `CreateDirectoryW` (fails if it exists, so a copy can no longer merge into somebody else's folder), the created paths are
  tracked and a failure removes them newest first with a non-recursive delete (a folder that gained somebody else's content stays with it).
  Seam: `SafeMove.UseBeforeCopyHookForTest` runs between the preflight and the copy. The old cleanup fails 3 of the 7 new tests (checked).
  The destination's parent must now exist (the broker already required it). (2) Removing a Desktop-folder rack whose emptied folder could not be
  deleted now tells the user once (`Lang.RemoveRack_FolderKept`, `Rack/RackFolderRemoval`) and still removes the rack; the attempt is in
  `audit.log` as the broker's `DeleteEmptyDirectory` record (Rejected "isn't empty" or Failed). (3) `FileOperationBroker.MoveChecked` runs
  `SafeMove.Check` once and calls the new `SafeMove.MoveAfterCheck`; every reason text and the order of the checks are unchanged (existing
  tests untouched). The one difference: a name that appears between the check and the move is no longer reported as `Skipped` by a second
  check but as `Failed` with the system's reason; it is still never overwritten. (4) The drive-root investigation is a test and a
  recommendation (see the P6 follow-up above). Two internal seams were added to make the rest testable and nothing else changed there:
  `DesktopIconManager.Setup(workspace, icon, libraries, desktop)` (what `Initialize()` does, with the four places passed in) and
  `RackMirror.Rebuild(racks, mirrorRoot)`.
- **Findings.** F2, F3 and F7 were fixed in a third commit (P11c, below). The others are not fixed in P11; each is pinned by a test
  (named `Known_gap_...` except F6 and F8), so a fix has to flip it on purpose, and each is now a risk in
  `docs/SECURITY-INVARIANTS.md`: **F1** a folder move to another volume is refused instead of copied (R-01, the one worth a phase of its
  own); **F4** a hard-linked workspace file with the same length as an unrelated Desktop file is dropped, not returned (R-02);
  **F5** a dangling junction counts as a reachable folder in `DesktopRackClaims.IsGone` (R-03); **F6** a second uninstall run returns
  the first run's note (R-04); **F8** the preflight does not resolve a missing destination's parent through a junction (R-05).
  - ~~**F2** moving a file to a destination called `NUL` poured the file into the NUL device.~~ **Fixed in P11c:**
    `FileOperationBroker.Move`/`MoveSameVolume` judge the destination's final segment with `PathPolicy.ValidateLeafName` right after
    the path check and before anything is touched ("Destination name: ..." in the reason): device names with or without an extension
    (NUL, CON, PRN, AUX, COM0-9, LPT0-9, CONIN$), a trailing dot or space and data streams are `Rejected`. Only the last segment is
    checked. `Rename` and `CreateDirectory` already did this.
  - ~~**F3** `AssignedFiles` containing `.` made `WorkspaceReturn` move the whole workspace onto the Desktop.~~ **Fixed in P11c:**
    `WorkspaceReturn.ToDesktop` and the remove-rack loop require `PathPolicy.ValidateLeafName(name).Ok`; the loader and the layout
    import (`RackSettingsValidator.ParsePipeList`) drop the items that fail it and keep the good ones, as a `SettingFallback` audit
    record on load and a dropped-value entry on import (the stored text is left as it is). One golden fixture value changed because it
    was not a leaf name: `sub dir\c.pdf` became `sub dir c.pdf`.
  - ~~**F7** the Desktop library file was built by string interpolation; a path with `&` or `<` made `DesktopWorkspace.library-ms`
    invalid XML.~~ **Fixed in P11c:** both paths go through `SecurityElement.Escape`.
- **P11c** (F2, F3, F7): the three fixes above, with their tests flipped from `Known_gap_...` to real expectations: 18 destination-name
  cases (device names, trailing dot/space, data streams) plus ordinary and `(from Racks N)` names, a trailing separator, odd parent
  names, `Rename`/`CreateDirectory`; 13 loader cases and the layout import for `AssignedFiles` items; the XML-escape case.
- Tests: 1669 to 1868 passed (6 skipped: the 4 old ones plus 2 new `[SymlinkFact]` tests that need the right to create symbolic links),
  0 failed. P11a added 108 (106 run here; SafeDelete 18, SafeMove 31, claims and return 36, uninstall and mirror 23), P11b 49 (copy
  cleanup 7, single preflight 3, rack folder removal 8, workspace set-up 10, mirror rebuild 11, drive-root rack 10). The Adversarial set
  was run 10 times in a row and the whole suite three times after P11b and again after P11c with no flake. P11c added 44 (1824 to 1868).
- Not done on purpose (all are risks in `docs/SECURITY-INVARIANTS.md`): the audit background queue (R-10), a handle-based `Unlink`
  (R-11), a link-aware `Move` for Ctrl+dropped links (R-08), the virtual-rack cross-volume copy-then-recycle (R-07) and the
  `WorkspaceReturn` file-identity check (R-02). The real app, `Test-Racks.ps1` and the dev-tools e2e were not run; the rack-removal
  dialog is covered through `RackFolderRemoval` (the decision and the text), not by showing the window. A second fixed drive existed on
  the machine used (D:), so the real cross-volume tests ran there; on a one-drive machine (and in CI) they show as skipped.

### P12 in short (what changed)

- One commit, no behavior change. Every `PackageReference` in `Racks.csproj` and the test project now has a comment saying why it
  exists and where it is used (all 7 app packages were checked with grep: all are used; none removed). Transitive on purpose, not
  referenced directly: Newtonsoft.Json (via Microsoft.ML), ExCSS (via Svg), Microsoft.CodeAnalysis (via Slions.VirtualDesktop).
- Bumps, all pinned exactly: `Microsoft.ML` 5.0.0 to 5.0.1 and `Svg` 3.4.7 to 3.4.8 (build and the whole suite green, SVG tests included).
  `THIRD-PARTY-NOTICES.md` was not touched in P12 (it did not list Microsoft.ML, which is MIT; closed in P13, which also lists the transitive packages that ship).
- `dotnet list package --vulnerable --include-transitive`: none in Racks, Racks.Tests and RacksSigner. `--outdated`: only test tooling,
  `Microsoft.NET.Test.Sdk` 17.14.1 (latest 18.11.0) and `xunit.runner.visualstudio` 3.1.4 (latest 4.0.1), both left on purpose
  (major versions, no need).
- Dead code removed (zero references in `Racks/` and `test/`, checked by name): `RegistryHelper.ReadKeyValueRoot` and
  `ReadKeyValueRootInt` (only mentioned in comments and in text samples of the registry guard test); in `Util/Interop.cs` the unused
  `InvalidateRect`, `UpdateWindow`, `IsWindow`, `DwmExtendFrameIntoClientArea`/`MARGINS`, `ShellExecuteEx`/`SHELLEXECUTEINFO`,
  `SHGetFileInfo` (and its `NotImplementedException` stub)/`SHFILEINFO`/`SHGFI*`, both `SHCreateItemFromParsingName` overloads with
  `IShellItem`/`IShellItemImageFactory`, the `EnumWindows(EnumedWindow)` overload with `EnumWindowCallback`, and unused constants
  (`MOD_*`, some `SHCNE_*`, `WM_*`, `SW_SHOWNA`, `HWND_NOTOPMOST`, `SEE_MASK_INVOKEIDLIST`). `RegistryHelper.KeyExists` was already gone (P6).
- Kept on purpose: `RegistryHelper.WriteToRegistryRoot` (tests use it as the old-behavior oracle), `AccentPolicy` fields (struct
  layout), `IContextMenu`/`CMINVOKECOMMANDINFO` (used), everything in DesktopIcons, DesktopIconPositioner, ShellLinkHelper,
  HardlinkHelper, JunctionHelper, FolderSize, WindowPlacement, FlickTracker and Particle (all members referenced).
- Old warnings gone: unused field `RackWindow._lastBorder`, the `string?` key in `ArchitectureRatchetTests`, four xUnit1031 in
  `UpdaterReleaseScanTests` (tests now `async Task`, same asserts) and one xUnit2029 (`Assert.DoesNotContain`, same meaning).
  Build warnings 8 to 0. Ratchet baselines lowered to the exact new values: `rackWindowLines` 577 to 561, `rackWindowPartMaxLines`
  919 to 911, `rackWindowTotalLines` 5934 to 5925.
- Hygiene: `tools/ComprehensiveE2ERunner.ps1` no longer defaults to a personal `.gemini` path (now `$env:TEMP\racks-e2e`);
  the PR template no longer says the test project does not exist and asks for what was refactored, risk reduced, tests added and local checks.
- Tests: 1868 passed, 6 skipped, 0 failed before and after (no test added or removed). Two flaky tests found in repeated runs were fixed in a follow-up commit: `SettingsAuditRecordTests.Without_an_injected_sink_...` (the process-wide audit sink also received events from parallel tests outside the AuditLog collection; the test now keeps only its own thread's events) and `RackDeletionTests.FileWatcherService_Dispose_...` (it watched the shared %TEMP% and used a blind sleep; real race too: an event already queued could reach a disposed `FileWatcherService`, now dropped by an `IsDisposed` guard; the test uses a private parent folder and a live control watcher instead of a sleep). Each class 30 runs and the whole suite 5 runs: all green.

## Findings that drove the order (all addressed)

1. Inline rename built the target with `Path.Combine(dir, typedText)` and no validation. Fixed in P1 (INV-FS-2).
2. Magic Organize "folders" mode created directories from an unsanitized cluster name. Fixed in P1.
3. Registry load used one `try` for all racks, accepted NaN/Infinity and undefined enum values, wrote doubles with the user's culture and
   wrote back while reading. Fixed in P6 (INV-REG-2); the downgrade caveat is R-15.
4. `DesktopRouter` had no watcher `Error` handling, used `Regex` directly, ignored partial downloads and reparse points. Fixed in P4 (INV-ROUTE-1).
5. `RackLayoutIO.Import()` was untested, failures only reached `Debug.WriteLine`, no size or range limits. Fixed in P9 (INV-IMPORT-2, -5).
6. `Updater` mixed toasts with logic, had no response size cap on the catalog and `InstallUpdate` was untested. Fixed in P10 (INV-UPDATE-1).
7. 185 `Debug.WriteLine` calls, none persisted. Not changed on purpose: only the broker, registry, import and update paths were audited.

## Out of scope (follow-ups, not risks)

`gitleaks`/CodeQL in Actions, `Directory.Packages.props`, converting every `catch {}`, the test tooling major upgrades. The shell verb
gate (M3) is a documented partial, R-12.

## Commands (run locally before every PR)

```powershell
dotnet restore Racks.sln
dotnet format whitespace --verify-no-changes
dotnet build Racks.sln -c Release
git diff --exit-code -- Racks/Properties/Lang.Designer.cs
dotnet test Racks.sln -c Release --no-build        # 1874 tests, 0 failed; the skipped count varies by machine (1 to 14: symlink rights, second drive, short names, SendTo, ReleaseGate)
dotnet publish Racks/Racks.csproj -c Release -r win-x64 --self-contained false
gitleaks detect --no-banner
```

UI-touching phases also run `test/Test-Racks.ps1` and `tools/dev-tools.ps1 e2e`, and the manual pass above (start the app, drag a
file into a rack, rename it, remove the rack, trigger an auto-route).
