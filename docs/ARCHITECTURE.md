# Architecture

Racks is a single WPF process (`Racks/Racks.csproj`, .NET 10, Windows only). It lives in the tray, owns a set of
always-on-desktop windows ("racks"), and moves, links and lists real files on the user's disk. The risk is not
the network: it is **data loss and surprises in the filesystem, the shell and the registry**. The layout below
exists to keep every dangerous operation in a few audited places.

## Layers (logical, not physical)

Folders were not moved to match these names. The table maps today's folders to layers; new code goes where
the last column says. A dependency may only point **down** the table (Presentation can use Application, never
the reverse). One deliberate exception: interfaces that a use case needs (hook, hotkey, pointer probe) live in
`Racks/UseCases/` and their Windows implementations in `Racks/Infrastructure/` implement them, so Infrastructure
may reference those interfaces (dependency inversion), never the use-case logic.

| Layer | What it may do | Folders today | New code goes in |
|---|---|---|---|
| Presentation | WPF only: windows, bindings, dialogs, delegating to services | `MainWindow*`, `RackWindow*`, `*Window.xaml.cs`, `RackSettingsDialog*`, `Views/`, `ViewModels/`, `Converters/`, `ColorPicker/`, `Shaders/` | stays here, shrinks |
| Application (folder `UseCases/`) | Use cases and orchestration (what happens when a file is dropped, a route fires, an update is found) | `UseCases/AutoRoute/` (P4), `UseCases/{MagicOrganize,HotCorner,DoubleClickHide,Hotkeys,Tray,Updates,Display}/` (P7), `UseCases/{Startup,Crash}/` (P8), parts of `Core/` (`AutoOrganizer`, `MagicOrganize*`), `Rack/` (`WorkspaceReturn`, `DesktopRackClaims`) | `Racks/UseCases/` (namespace `Racks.UseCases.*`; not `Racks.Application`, which would shadow `System.Windows.Application`) |
| Domain | Entities and rules, no I/O | `Core/Instance.cs`, `Core/FileItem.cs`, `Rack/RackNames.cs`, `Rack/Sorting/` | `Racks/Core/` |
| Infrastructure | Everything that touches Windows: filesystem, shell, registry, HTTP, process, logging | most of `Util/` (`Safe*`, `Shell*`, `Interop`, `RegistryHelper`, `Updater`, `JunctionHelper`, ...), `Services/` | `Racks/Infrastructure/{FileSystem,Shell,Registry,Update,Logging}/` |
| Shared | Small pure helpers used by every layer | `SafeRegex`, `SafeWpfValues`, path and name validation | `Racks/Infrastructure/` until a split is needed |

There is no DI container. `Infrastructure/Startup/AppComposition` (called from the `App` constructor) wires the startup services by hand. It is the composition root, the one deliberate exception to "Infrastructure does not know use cases" (kept there rather than next to `App`, so `App.xaml.cs` stays a thin lifecycle shell); the legacy static
`MainWindow._controller` is being retired step by step (see ratchets below).

## The rules that matter

1. **Destructive file operations go through one gateway** (`Infrastructure/FileSystem/FileOperationBroker`, a
   facade over the internal `SafeMove` and `SafeDelete`). It validates names and paths (`PathPolicy`),
   canonicalizes, rejects protected roots, never follows reparse points blindly and writes one audit record per
   call (`%AppData%\Racks\audit.log`). `Guards/FileMutationGuardTests` fails on raw `File`/`Directory` mutations
   outside it, except those listed in `Guards/fs-allowlist.json` (the list only shrinks; after P2 it holds only Racks-owned state).
2. **The registry is read and written through typed access.** App-wide switches and one-shot markers (loose values under
   `HKCU\SOFTWARE\Racks`) go through `Infrastructure/Registry/AppSettings` and `MigrationMarkers` (P5); the per-rack
   values and the global style defaults go through `RackSettingsRepository`, driven by one `RackSettingsSchema` (P6). A
   bad value is never applied: the documented default is used and the field, raw value, reason and fallback are written
   to `audit.log` (once per name for app-wide values, once per value and load for racks).
3. **Presentation does not touch the registry, mutate files, call shell notifications or the updater.** Shell
   change notifications, desktop-icon hiding/arranging and the workspace folder go through
   `Infrastructure/Shell/ShellOperationGateway.Default`; `Guards/PresentationBoundaryTests` allows zero direct
   references to `Interop.NotifyShell*`, `DesktopIcons`, `DesktopIconPositioner` and `DesktopIconManager` in
   `MainWindow*`, `RackWindow*`, `App`, the dialogs, `Views/`, `ViewModels/` and `Converters/`. (The shell context
   menu, window geometry/style Win32 calls, `Process.Start` and the tray control stay in presentation on purpose; since P7 the hotkeys,
   the mouse hook, the hot-corner polling and the update glue are use cases, and `Guards/PresentationBoundaryTests` keeps
   `MainWindow.xaml.cs` free of file, registry, updater, hook and cursor code.)
4. **The update path is fail-closed.** Nothing downloaded runs unless the signed manifest, the host/path
   allowlist, the size and the SHA-256 all check out. See `docs/SECURITY-INVARIANTS.md` (INV-UPDATE-1).
5. **Imports are validated before any state is written** and a bad file leaves the current layout untouched (layout files: size, depth, counts, version, ranges and paths are checked first, `UseCases/Layout`; INV-IMPORT-5).

Rules 1, 2, 3 (shell and registry parts; updater comes later) and parts of 5 are enforced by tests in `test/Racks.Tests/Guards/`, which work as ratchets: the
numbers in `Guards/baseline.json` may only go down.

## Where things live today

- **Window hosting.** `MainWindow` is a hidden zero-size host: tray icon and its menu, the `WM_RACKS_*` IPC table, `WndProc`,
  dialogs. Since P7 it only builds the services below by hand, forwards events and shows windows (one file, 420 lines, ratcheted).
  `RackWindow*` (partial class) is one rack and is still larger than it should be. Order that matters: `OnSourceInitialized` runs
  inside the constructor (`EnsureHandle`), before the controller exists, so `ComposeHostServices()` runs before `EnsureHandle` and
  `ComposeRackServices()` after the controller.
- **Host use cases (P7).** `UseCases/MagicOrganize/MagicOrganizeService` (analyze, preview, Racks or Folders mode, undo; every move
  through the broker, windows and dialogs behind `IMagicOrganizeHost`, implemented by `Views/MagicOrganizeHost`),
  `UseCases/HotCorner/HotCornerService` (100 ms poll, 16 px zone, 64 px leave margin, primary monitor only; scheduler and probe
  injected), `UseCases/DoubleClickHide/DoubleClickHideService` (hook life and the pure decision; H.Hooks and UI Automation only in
  `Infrastructure/Shell/GlobalMouseHook` and `DesktopHitTest`), `UseCases/Hotkeys/HotkeyController` (ids 0xB0CC/CD/CE, conflict
  callback, dispatch), `UseCases/Tray/TrayToggleService` (what each tray switch does: `AppSettings`, shell gateway, hot corner, lock
  all), `UseCases/Updates/UpdateCheckService` (background and manual check, old-download clean-up; the updater itself is untouched)
  and `UseCases/Display/DisplayChangeDebouncer` (one reposition pass per burst). Composition is manual, no container; the static
  `MainWindow._controller` stays for the rack windows. Tests use `ManualRepeatingScheduler`, `InMemoryRegistryRoot` and fakes for the
  hook, hit test, hotkey API and host; none touches the real Desktop, shell, hotkeys or `HKCU\SOFTWARE\Racks`.
- **App and startup (P8).** `App.xaml.cs` (ratchet `appLines`) only builds the services in its constructor and delegates
  `OnStartup`/`OnExit`. `UseCases/Startup/AppBootstrapService` holds the ONE ordered list of startup steps per mode (a test reads and
  runs it with a recording host; no test reads source text any more). Normal: culture preview, WPF language, regex default timeout,
  `SafeSvg.Apply`, registry migration, dark mode, single-instance name (a second Racks shuts down here), binding trace level,
  workspace folder, `StartupUri`, base `OnStartup`, first-run marker, toast activation, Quick Access pin, startup animation (500 ms
  later, on the UI thread). `--uninstall-cleanup` stops after cleanup + `Shutdown(0)` (no migration, no mutex, no UI);
  `--uninstall-anim` skips the mutex and the workspace. Helpers: `StartupMigrationService` (markers via `MigrationMarkers`, pin,
  animation decision), `UninstallMode` (argument parsing, 2-minute `IWatchdog` that exits with code 2, rack-title map from the
  settings store, report), `SingleInstanceGuard` (`Local\Racks-SingleInstance-2C9D`, enabled only in release builds, handle factory
  injected), `ProcessConfiguration`, and `UseCases/Crash/CrashReportingService` (UI-thread exceptions always handled, unobserved
  task exceptions observed, `crash.log` entries in the format `CrashLogTests` pins, one toast per session, silent when running as the
  uninstaller). The crash events are attached by `Infrastructure/Startup/CrashHooks` first thing in the `App` constructor (before anything else there can throw). The WPF/Windows side is `Infrastructure/Startup` (`AppStartupHost`, `ThreadWatchdog`, `MutexHandle`,
  `TaskDispatcherDelayScheduler`, `AppComposition`).
- **Persistence.** `Core/InstanceController` asks the settings repository for the racks under
  `HKCU\SOFTWARE\Racks\Instances\<name>` and runs the post pass (Desktop-rack detection, the two one-shot migrations,
  the folder-missing flag, then the windows); `Core/Instance` writes through `Instance.Store` on every property change.
  Global switches and migration markers live under `HKCU\SOFTWARE\Racks` and are read and written only through
  `Infrastructure/Registry` (below). Layout export/import is `UseCases/Layout` (P9, below); `Util/RackLayoutIO` is only a thin facade for the tests.
- **App-wide registry values.** `Infrastructure/Registry/` (namespace `Racks.Infrastructure.Registry`): `IRegistryRoot` (read /
  write / delete values under one key; `HkcuRegistryRoot` is the real one and follows `InstanceController.appName`, tests
  use a throwaway `Software\RacksTests-<guid>` key or a failing root), `AppSettings` (`Default`: start on login incl.
  the Run entry and the legacy `DeskFrame` one, double-click to hide, ice physics (default ON), hide desktop icons, hot
  corner, auto update, read-only blur background, `MainHwnd`) and `MigrationMarkers` (`Default`: the six one-shot
  markers). Stored forms are unchanged (REG_SZ `True`/`False`; `MainHwnd` is REG_SZ decimal; marker `_MigratedFromDeskFrame`
  is REG_SZ `1`). `Guards/RegistryAccessGuardTests` ratchets direct registry access in presentation files
  (`Guards/registry-presentation-allowlist.json`, empty since P6). Do not name a test
  namespace `Racks.Tests.Registry`: it would shadow `Microsoft.Win32.Registry` in sibling test namespaces.
- **Per-rack registry values (P6).** Same namespace. `RackSettingsSchema` is the ONE table of the 67 persisted values (plus the
  discarded legacy `MoveFilesOnDrop`): name, kind, default, limits (reject or clamp), enum type, import-accepted, global-default.
  `RackLayoutIO`'s allow-list, the loader, the writers and the 32 global defaults all derive from it. `RackSettings` is the typed
  DTO (nullable: null = nothing usable stored, the rack keeps its default). `RackSettingsValidator` parses strictly (invariant
  then current-culture numbers, finite only, defined enums, `SafeWpfValues` for fonts and colors, list items kept one by one).
  `RackSettingsMapper` copies between the DTO and `Instance` through its safe setters under `SuspendPersistence`, so a load never
  writes. `RackSettingsRepository` (`Default`, behind `IRackSettingsStore`; `IRackRegistry`/`HkcuRackRegistry` is the seam for
  `Instances\*`, `IRegistryRoot` for the root) offers `LoadAll()` (a `try` per rack), `ReadGlobalStyleDefaults()` (each of the
  32 on its own), `Save`, `SaveField`, `SaveGlobalDefault`, `Delete` and `ResetGlobalStyleDefaults`; none throws. `Instance.Store`
  is the static default that tests replace. Every refused value is one `SettingFallback` audit record (rack, field, raw value to
  200 characters, reason, fallback); a failed write is a `SettingWriteFailed` record.
- **Moving files.** `Infrastructure/FileSystem/FileOperationBroker` over the internal primitives `Util/SafeMove`
  (canonicalization, protected roots, cross-volume copy-then-delete) and `Util/SafeDelete` (reparse-point-aware
  tree delete, Recycle Bin), `Rack/WorkspaceReturn` (back to Desktop),
  `Util/UninstallCleanup` (returns user files on uninstall, rename-only).
- **Shell gateway.** `Infrastructure/Shell/ShellOperationGateway` delegates (no logic) to `Util/Interop`
  (`SHChangeNotify`, one declaration), `Util/DesktopIcons`, `Util/DesktopIconPositioner` and
  `Core/DesktopIconManager`, which are `internal`. Tests replace the calls through an internal `ShellBackend`.
- **Auto-route.** `UseCases/AutoRoute/` (namespace `Racks.UseCases.AutoRoute`): `AutoRouteService` picks
  the first rack whose rule matches (regex through `Util/SafeRegex`), asks `RoutingPolicy` whether the entry may
  move (not `desktop.ini`, hidden/system, partial download, link, online-only cloud file, locked), moves it through
  the broker, notifies the shell gateway, claims it for a desktop rack and audits every decision. `Util/DesktopRouter`
  is only the watcher: 64 KB buffer, Created + Renamed, one debounce window per burst, recovery from watcher `Error`
  (rebuild + rescan for names that were not on the Desktop before, backoff), all on the UI thread through
  `IDelayScheduler`. The service has no WPF types: `MainWindow` supplies the rack list, a save callback and the
  workspace path (`AutoRouteHost`).
- **Layout import/export (P9).** `UseCases/Layout/` (namespace `Racks.UseCases.Layout`), one job per class, in the order a file travels:
  `LayoutParser` (bounded read of the file, size/depth/rack-count/entry-count limits from `LayoutLimits`, BOM-aware decoding, an untrusted
  `LayoutDocument`; never throws), `LayoutValidator` (version, names, duplicates, scalar kinds, every value through
  `RackSettingsValidator`, a typed `ValidatedRack` with the exact `RegistryWrite`s; whole-file refusal for the security rules with a message naming rack and
  value, while a bad number/switch/enum/list is dropped like the loader would and reported as `DroppedValue`) using `LayoutSecurityPolicy` (allow-list from `RackSettingsSchema`, local-drive paths, fonts/colors via `SafeWpfValues`, list
  and regex size caps), `LayoutImportExecutor` (snapshot, delete, write, verify, precise checked rollback over `IRackRegistry`, and a recovery layout in `%AppData%\Racks\recovery` (`LayoutRecovery`, `Infrastructure/Layout/RecoveryFolder`, newest 5) when the restore fails; the only
  place that writes layouts to the registry), `LayoutExporter` (typed racks from `IRackSettingsStore` to the same JSON shape as ever,
  written by `Infrastructure/Layout/LayoutFileWriter`: short random temp file next to the destination, then one `File.Move(overwrite)`; it first runs its own bytes through parser and validator and refuses a file Racks would not import back), `LayoutService` (composes
  them; results, never exceptions; every failure is an audit record: `LayoutImport`, `LayoutExport`, `LayoutImportRollbackFailed`)
  and `LayoutMessages` (the texts, `Lang.resx`). The dialogs are `Views/LayoutDialogs` (open/save dialog, confirm, shows every
  failure). `ImportResult` says Imported / Rejected / Failed, and for Failed whether the state changed, was rolled back or the
  rollback itself failed. `IRackRegistry.ReadValuesExact` (default = `ReadValues`) is the exact read used for snapshots
  (REG_EXPAND_SZ unexpanded). Limits and the other rules: `SECURITY-INVARIANTS.md`, INV-IMPORT-1, -2, -5.
- **Updates.** `Util/Updater` (flow), `Util/UpdateManifest` (pure signature/hash/size checks), `Util/UpdateTrust`
  (embedded public key). `tools/RacksSigner` links the last two by path.
- **Diagnostics.** `Util/CrashLog` (`%AppData%\Racks\crash.log`, rotated), `Infrastructure/Logging` (`audit.log`: one
  JSON line per file operation or rejected settings value, same rotation, local only), `Services/DiagnosticCounters`.

## Paths that must not move

- `Racks/Util/UpdateManifest.cs`, `Racks/Util/UpdateTrust.cs`: `tools/RacksSigner/RacksSigner.csproj` and
  `tools/install-signer.ps1` / `tools/release-sign.ps1` reference them by path.
- `HKCU\SOFTWARE\Racks\MainHwnd` (REG_SZ, decimal; written by `AppSettings.TryPublishMainHwnd`) and the `WM_RACKS_*` window messages: used by `tools/*E2ERunner.ps1`.
- `Guards/baseline.json`, `Guards/fs-allowlist.json` and `Guards/registry-presentation-allowlist.json` are copied to the test output; keep their keys stable (add, don't rename).
- The startup order in `UseCases/Startup/AppBootstrapService` (pinned by `AppBootstrapServiceTests` and `SafeSvgTests`):
  `SafeSvg.Apply()` before `StartupUri`; the workspace folder (`InitializeWorkspace`) before `StartupUri`, so before any `RackWindow`
  exists; uninstall modes end before any UI.

## Tests

`dotnet test Racks.sln -c Release`. Tests run on real Windows (`windows-latest` in CI) against temp directories,
real junctions and hardlinks. Registry tests redirect the root with `InstanceController.appName`. Time is faked
with `Fakes/ManualScheduler` (`IDelayScheduler`). Ratchets live in `Guards/`.
