# Hardening roadmap (resume point)

Goal: move Racks from "works" to "high confidence" for a Windows desktop app that moves real user files.
Order: **file safety first, then architecture, then tests.** No new features while this runs.
Each phase is one PR, stacked on the previous one, and says what was refactored, which risk went down and
which tests were added.

## Where we are

- **Current phase:** P8 (decompose `App`: bootstrap, migrations, crash reporting, uninstall mode), in review; P7 (`MainWindow`)
  is its base.
- **Next step:** P9, layout import/export (parser, validator, policy, executor).
  `MainWindow._controller` is still a public static (20 uses, of which `InstanceController.RefreshMirror` is the only
  one outside presentation). The file allowlist (`test/Racks.Tests/Guards/fs-allowlist.json`) is only Racks-owned
  state; its numbers only go down.
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
| P5 | Typed `AppSettings` and `MigrationMarkers` for root registry values | in review |
| P6 | `RackSettingsSchema` + validator + repository (golden fixtures first) | in review |
| P7 | Decompose `MainWindow` (Magic Organize, hot corner, double-click hide, hotkeys, tray switches, update glue, display debounce) | in review |
| P8 | Decompose `App` (bootstrap, migrations, crash reporting, uninstall mode) | in review |
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
  `controllerUsages` went 30 to 20 (27 real uses before). `RackLayoutIO`'s own five registry accesses stay for P9.
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
- **Culture and downgrade.** Doubles (`PosX`, `Width`, `IdleOpacity`, ...) are written as invariant text (`123.5`) whatever the culture.
  The reader tries invariant text first, then the current culture. The real rule: **invariant text is read correctly in every culture;
  legacy culture text (`123,5`) is read correctly only in the culture that wrote it** (an en-US machine reads `321,75` as 32175, as the
  old reader did). A **downgrade is unsafe**: an older build on a culture whose group separator is a space (pt-PT, fr-FR) does not
  parse `123.5` and uses the defaults; on cultures whose group separator is '.' (de-DE, es-ES, it-IT, pt-BR, nl-NL) the old parser
  (`AllowThousands`) reads `0.4` as 4 and `321.75` as 32175, which can corrupt geometry and opacity. Recommendation: do not downgrade
  (or re-save the racks there). Integers stay REG_DWORD (the write-through used to store them as REG_SZ), switches stay `True`/`False`,
  lists as before; the global defaults stay REG_SZ text (an older build reads the root with a text reader).
- **Audit volume and normalisation (review fixes).** A load no longer writes normalised values back, so the same finding would repeat on every
  start: findings are logged once per process per (rack key, field, raw value). A rack whose ONLY findings are benign (a stored
  number clamped, a missing/empty `Name`) is saved once in full, explicitly, as `SettingNormalized`, so the registry is corrected
  once and later loads are silent; a rack with any refused value or dropped list item is never rewritten (the user's raw data stays
  until they change it). A `Name` that differs from its key only in case (a case-only rename) is not a finding. A hand-edited rack
  whose `Name` differs from its key in more than case gets its one-shot migrations in memory only (a write would create a
  skeleton key under that name that loads as a "folder missing" ghost).
- Behaviour note: `ShowOnVirtualDesktops` `1,abc` now becomes `[1]` (it used to be dropped whole, meaning "all desktops").
- Follow-up (pre-existing, not fixed): a rack whose folder is a drive root has the name `""`; it has no registry key, so it is never
  persisted. Saving it is now a silent skip (it used to log a write failure on every change).
- Kept on purpose: the two one-shot migrations still write through the setters after a load; a full save leaves an empty
  `LastAccessedFiles`/`CustomOrderFiles` value alone while a single-value write stores the empty list (and `AssignedFiles` is removed
  when empty), exactly as before; `BackgroundImagePath` is stored as typed (the local-drive check stays where the image is loaded);
  a `Name` that differs from its key is used as is (audited), so a later save writes under that name.
- Not done on purpose: `Test-Racks.ps1` and the dev-tools e2e were not run; `RackLayoutIO.Import/Export` still touch the registry
  directly (P9); `MoveFilesOnDrop` is still importable although it is discarded.

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
  1 min clean-up of old download folders; `Util/Updater` untouched), `Display/DisplayChangeDebouncer` (the
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

- `App.xaml.cs` went from 293 to 39 lines (ratchet `appLines` 39, and "it is one file"). It builds the services in its constructor
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
- `MainHwnd` registry value (REG_SZ, decimal) and `WM_RACKS_*` messages are used by `tools/*E2ERunner.ps1`.
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
