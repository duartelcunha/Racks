# Architecture

Racks is a single WPF process (`Racks/Racks.csproj`, .NET 10, Windows only). It lives in the tray, owns a set of
always-on-desktop windows ("racks"), and moves, links and lists real files on the user's disk. The risk is not
the network: it is **data loss and surprises in the filesystem, the shell and the registry**. The layout below
exists to keep every dangerous operation in a few places that are reviewed and, where a test can see them, fenced.

This file describes the code as it is at the end of the hardening plan (P13). The security rules are in
[`SECURITY-INVARIANTS.md`](SECURITY-INVARIANTS.md); the history, the open work and how to resume are in
[`HARDENING-ROADMAP.md`](HARDENING-ROADMAP.md).

## Layers

The folders are the layers. A dependency may only point **down** this table (Presentation uses Use cases, Use cases
use Domain, nothing in a lower row uses a higher one), with the exceptions listed after it.

| Layer | What it may do | Folders | New code goes in |
|---|---|---|---|
| Presentation | WPF only: windows, bindings, dialogs, converters; delegate to use cases | `MainWindow*`, `RackWindow*` (partial class), `*Window.xaml(.cs)`, `RackSettingsDialog*`, `App.xaml(.cs)`, `Views/`, `ViewModels/`, `Converters/`, `ColorPicker/`, `Shaders/`, `Resources/` | stays here, and shrinks (ratchets) |
| Use cases | What happens when a file is dropped, a route fires, an update is found, the app starts. No WPF types in the logic; the outside world is behind interfaces | `UseCases/{AutoRoute,Layout,Startup,Crash,Updates,MagicOrganize,HotCorner,DoubleClickHide,Hotkeys,Tray,Display}/` (namespace `Racks.UseCases.*`; never `Racks.Application`, it shadows `System.Windows.Application`), plus `Core/AutoOrganizer`, `Core/MagicOrganize*`, `Rack/WorkspaceReturn`, `Rack/DesktopRackClaims`, `Rack/OrphanShortcutCleanup`, `Rack/RackFolderRemoval` | `Racks/UseCases/<Feature>/` |
| Domain / model | Entities and rules; no I/O except persisting through `Instance.Store` | `Core/Instance`, `Core/FileItem`, `Core/InstanceController` (loads and holds the racks), `Rack/RackNames`, `Rack/Sorting/`, `Core/Abstractions/` (`IDelayScheduler`, `IRepeatingScheduler`) | `Racks/Core/` or `Racks/Rack/` |
| Infrastructure | Everything that touches Windows: filesystem, shell, registry, HTTP, process, logging | `Infrastructure/FileSystem`, `Logging`, `Registry`, `Shell`, `Layout`, `Startup`, `Update`; and the legacy helpers in `Util/` and `Services/` (see below) | `Racks/Infrastructure/<Area>/` |
| Shared helpers | Small helpers used by every layer | `Util/` (`SafeRegex`, `SafeWpfValues`, `SafeSvg`, `SafeMove`, `SafeDelete`, `UpdateManifest`, `UpdateTrust`, `CrashLog`, Win32 and shell wrappers) and `Services/` (`FileWatcherService`, `ThumbnailCache`, `DiagnosticCounters`) | `Racks/Util/` only for something that really is a tiny helper; otherwise a layer above |

`Util/` is the one place where the layers are not separated yet: it holds pure helpers (`SafeRegex`, `RackPhysics`,
`FlickTracker`), the two internal file primitives (`SafeMove`, `SafeDelete`), Win32 wrappers (`Interop`,
`ShellContextMenu`, `ShellLinkHelper`, `HardlinkHelper`, `JunctionHelper`) and the `DesktopRouter` watcher. Moving them
was not worth the churn; the guards (below) fence the ones that matter. Do not add new infrastructure there.

**The exceptions to "dependencies point down" (all of them, exactly):**

1. **Interfaces owned by a use case are implemented in Infrastructure.** For example `IGlobalMouseHook`,
   `IHotkeyApi`, `IHotCornerProbe`, `IMagicOrganizeHost`, `IUpdateNotifier`, `IInstallerLauncher`, `IAppShutdown` and
   `IStartupHost` live in `UseCases/` and `Infrastructure/` (or the Views adapters) implements them, so Infrastructure
   references those interfaces (dependency inversion), never the use-case logic. (The registry and audit seams,
   `IRegistryRoot`, `IRackRegistry`, `IRackSettingsStore` and `IAuditSink`, are the opposite case: they are owned by
   Infrastructure and used by the use cases, which is downward and needs no exception.)
2. **`Infrastructure/Startup/AppComposition` and `Infrastructure/Update/UpdateComposition` are composition roots.** There
   is no DI container: they build the services by hand (the one deliberate place where Infrastructure knows the use
   cases). `App` calls `AppComposition` from its constructor, which keeps `App.xaml.cs` a thin lifecycle shell;
   `MainWindow` builds its own host services by hand (`ComposeHostServices`, `ComposeRackServices`).
3. **`Core/Instance` and `Core/InstanceController` <-> `Infrastructure/Registry` reference each other.** `Instance` persists through
   `Instance.Store` (an `IRackSettingsStore`) and `RackSettingsMapper` copies the typed DTO into `Instance`. It is the one
   mutual reference left; it is deliberate (the model is the thing being persisted) and not to be copied.

## The rules that matter

1. **Destructive file operations go through one gateway**, `Infrastructure/FileSystem/FileOperationBroker`: a facade over
   the `internal` primitives `Util/SafeMove` and `Util/SafeDelete`. It validates names and paths (`PathPolicy`),
   canonicalizes, refuses protected folders and their ancestors, never follows reparse points blindly, confirms the
   result on disk and writes one audit record per call (`%AppData%\Racks\audit.log`). It does not throw for an
   expected failure: the result is `Succeeded`/`Rejected`/`Skipped`/`Failed`. Operations: `Move`, `MoveSameVolume`,
   `Rename`, `DeleteToRecycleBin`, `DeleteTree`, `CreateDirectory`, `DeleteEmptyDirectory`, `Unlink`,
   `DeleteRacksShortcut`. `FileMutationGuardTests` fails on raw `File`/`Directory` mutations outside it, except those in
   `Guards/fs-allowlist.json`.
2. **The registry is read and written through typed access.** App-wide switches and one-shot markers (loose values
   under `HKCU\SOFTWARE\Racks`) go through `Infrastructure/Registry/AppSettings` and `MigrationMarkers`. Per-rack values and
   the 32 global style defaults go through `RackSettingsRepository`, driven by one table, `RackSettingsSchema`. A bad value
   is never applied: the documented default is used and the field, raw value, reason and fallback are written to
   `audit.log`.
3. **Presentation does not touch the registry, mutate files, call shell notifications or the updater.** Shell change
   notifications, desktop-icon hiding/arranging and the workspace folder go through
   `Infrastructure/Shell/ShellOperationGateway.Default`. The shell context menu, window geometry/style Win32 calls,
   `Process.Start` and the tray control stay in presentation on purpose.
4. **The update path is fail-closed.** Nothing downloaded runs unless the signed manifest, the host/path allowlist, the
   size and the SHA-256 all check out (INV-UPDATE-1).
5. **Imports are validated before any state is written** and a bad file leaves the current layout untouched
   (`UseCases/Layout`; INV-IMPORT-1, -2, -5).
6. **Tests are behavior tests.** A test that reads source text is a guard (a fence), never the way to pin a feature
   (see "Tests").

## Guards and ratchets (the numbers today)

All in `test/Racks.Tests/Guards/`. The three JSON files are copied to the test output; keep their keys stable
(add, do not rename). **Numbers only go down**: lower the number in the same PR that removes the code.

| Guard | What it enforces | Number today | What it cannot see |
|---|---|---|---|
| `FileMutationGuardTests` + `fs-allowlist.json` | exact count of raw file mutations per file (more or fewer fails); `SafeMove.`/`SafeDelete.` only inside `Infrastructure/FileSystem/` | **11 files, 36 calls** (`DesktopIconManager` 7, `UninstallCleanup` 8, `JunctionHelper` 5, `CrashLog` 3, `LayoutFileWriter` 3, `RackWindow.DragDrop` 2, `RackMirror` 2, `RecoveryFolder` 2, `UpdateCleanupService` 2, `ShortcutFactory` 1, `InstallerExecutor` 1); all Racks-owned state or a non-destructive copy | calls through a variable, Win32/COM, processes, shell verbs, reflection, `tools/` |
| `PresentationBoundaryTests` | zero references in presentation files to `NotifyShell*`, `SHChangeNotify`, `DesktopIcons.`, `DesktopIconPositioner`, `DesktopIconManager`; `MainWindow.xaml.cs` has no file, registry, updater, hook, hotkey or cursor code and at most one `DispatcherTimer` | **0** (no baseline file) | new `[DllImport]`/COM in presentation, `Process.Start`, files outside the presentation list |
| `RegistryAccessGuardTests` + `registry-presentation-allowlist.json` | exact count of registry references per presentation file | **0** (empty list since P6) | `reg.exe`/PowerShell processes, reflection, helpers outside presentation |
| `ArchitectureRatchetTests` + `baseline.json` | size ratchets | `rackWindowLines` **561**, `rackWindowPartMaxLines` **911**, `rackWindowTotalLines` **5925**, `mainWindowLines` **404** (one file), `mainWindowMethodMaxLines` **59**, `appLines` **38** (one file), `controllerUsages` **20**, `xamlLiterals` **119** | quality or coupling; only lines and counts |

Other tests that are not size ratchets but fence the shape: `ReleaseScriptTests` (release scripts and the snippets of
`docs/RELEASING.md`), `LocalizationTests` (every translation key exists in `Lang.resx`), `CollapsedHeightTests`,
`QuickFinderStyleTests`. `SECURITY-INVARIANTS.md` has the full list of what each guard sees and misses.

## Startup sequence

`App` (`Racks/App.xaml.cs`, 38 lines) does three things: attach the crash hooks first (`Infrastructure/Startup/CrashHooks`,
before anything else can throw), build the services (`AppComposition.Create`), and delegate `OnStartup`/`OnExit` to
`UseCases/Startup/AppBootstrapService`, which holds the ONE ordered list of steps per mode (`StartupSteps`; a test reads
and runs it with a recording host; `AppBootstrapServiceTests`). A step returning false ends the run.

- **Normal:** `ConfigureCulturePreview`, `OverrideWpfLanguage`, `SetRegexDefaultTimeout`, `ApplySafeSvg`,
  `MigrateLegacyRegistry`, `EnableDarkMode`, `AcquireSingleInstance` (a second Racks shuts down here; mutex
  `Local\Racks-SingleInstance-2C9D`, release builds only), `SetBindingTraceLevel`, `InitializeWorkspace`, `SetStartupUri`,
  `RaiseBaseStartup`, `ReadFirstRunMarker`, `RegisterToastActivation`, `PinQuickAccess`, `ScheduleStartupAnimation`
  (500 ms later, on the UI thread).
- **`--uninstall-cleanup`:** the four common steps, `RunUninstallCleanup` (2-minute watchdog that exits with code 2),
  `ShutdownAfterCleanup`. No migration, no mutex, no UI.
- **`--uninstall-anim`:** the four common steps, `MigrateLegacyRegistry`, `EnableDarkMode`, `MarkUninstallAnimation`,
  `RunUninstallAnimation`. No mutex, no workspace.
- **Then WPF creates `MainWindow`** from `StartupUri`, after `OnStartup` returns. Its constructor: `ComposeHostServices()`
  (hotkeys, the `WM_RACKS_*` message table, tray toggles, hot corner, double-click hide, update glue, display debounce), then
  `EnsureHandle()` (this raises `OnSourceInitialized` inside the constructor, before the controller exists, which is why
  the host services are built first), `new InstanceController().InitInstances()` (loads every rack in its own `try`;
  never writes), `ComposeRackServices()` (the services that hold the controller), `StartAutoRouting()`, then the saved tray
  switches, the optional background update check and the clean-up of old download folders.
- **Order that is pinned by tests:** `ApplySafeSvg` and the regex timeout before anything can parse a file; the workspace
  folder before `StartupUri` (so before any `RackWindow` exists); uninstall modes end before any UI.
- **Exit:** `AppBootstrapService.Exit()` brings back the desktop icons Racks hid, then releases the single-instance name.
  Known gap: a startup failure after the name was taken leaves a windowless process that holds it (R-13 in
  `SECURITY-INVARIANTS.md`).

## Where things live

- **Window hosting.** `MainWindow` is a hidden zero-size host: tray icon and menu, the `WM_RACKS_*` IPC table, `WndProc`,
  dialogs. It builds its services by hand, forwards events and shows windows (one file, 404 lines, ratcheted).
  `RackWindow*` is one rack and is still the biggest presentation class (a partial class of about 5,900 lines in total,
  ratcheted). `MainWindow._controller` is still a public static (20 uses, ratcheted); only `InstanceController.RefreshMirror`
  reads it from outside presentation.
- **Host use cases.** `UseCases/MagicOrganize/MagicOrganizeService` (analyze, preview, Racks or Folders mode, undo; every
  move through the broker, windows and dialogs behind `IMagicOrganizeHost`, implemented by `Views/MagicOrganizeHost`),
  `HotCorner/HotCornerService` (100 ms poll, 16 px zone, 64 px leave margin, primary monitor only; scheduler and probe
  injected), `DoubleClickHide/DoubleClickHideService` (hook life and the pure decision; H.Hooks and UI Automation only in
  `Infrastructure/Shell/GlobalMouseHook`), `Hotkeys/HotkeyController` (ids 0xB0CC/CD/CE, conflict callback, dispatch),
  `Tray/TrayToggleService` (what each tray switch does: `AppSettings`, shell gateway, hot corner, lock all),
  `Updates/UpdateCheckService` (background and manual check, old-download clean-up) and `Display/DisplayChangeDebouncer`
  (one reposition pass per burst). Tests use `ManualRepeatingScheduler`, `InMemoryRegistryRoot` and fakes for the hook, hit test,
  hotkey API and host; none touches the real Desktop, shell, hotkeys or `HKCU\SOFTWARE\Racks`.
- **Startup helpers.** `UseCases/Startup`: `StartupMigrationService` (markers through `MigrationMarkers`, Quick Access pin,
  animation decision), `UninstallMode` (argument parsing, `IWatchdog`, rack-title map, report), `SingleInstanceGuard`,
  `ProcessConfiguration`. `UseCases/Crash/CrashReportingService` (UI-thread exceptions always handled, unobserved task exceptions
  observed, `crash.log` entries in the format `CrashLogTests` pins, one toast per session, silent when running as the
  uninstaller). The WPF/Windows side is `Infrastructure/Startup` (`AppStartupHost`, `ThreadWatchdog`, `MutexHandle`,
  `TaskDispatcherDelayScheduler`, `CrashHooks`, `AppComposition`).
- **Persistence.** `Core/InstanceController` asks the settings repository for the racks under
  `HKCU\SOFTWARE\Racks\Instances\<name>` and runs the post pass (Desktop-rack detection, the two one-shot migrations, the
  folder-missing flag, then the windows); `Core/Instance` writes through `Instance.Store` on every property change.
  `Instance.Store` is the static default that tests replace; the test assembly wraps it so a test that forgets to redirect
  `InstanceController.appName` throws instead of writing `HKCU\SOFTWARE\Racks`.
- **App-wide registry values.** `Infrastructure/Registry/` (namespace `Racks.Infrastructure.Registry`): `IRegistryRoot`
  (`HkcuRegistryRoot` is the real one and follows `InstanceController.appName`; tests use a throwaway
  `Software\RacksTests-<guid>` key or a failing root), `AppSettings` (`Default`: start on login incl. the Run entry and the
  legacy `DeskFrame` one, double-click to hide, ice physics (default ON), hide desktop icons, hot corner, auto update,
  read-only blur background, `MainHwnd` via `TryPublishMainHwnd`) and `MigrationMarkers` (`Default`: the six one-shot
  markers). Stored forms are unchanged (REG_SZ `True`/`False`; `MainHwnd` is REG_SZ decimal; marker `_MigratedFromDeskFrame` is
  REG_SZ `1`). Do not name a test namespace `Racks.Tests.Registry`: it would shadow `Microsoft.Win32.Registry`.
- **Per-rack registry values.** Same namespace. `RackSettingsSchema` is the ONE table of the 67 persisted values (plus the
  discarded legacy `MoveFilesOnDrop`): name, kind, default, limits (reject or clamp), enum type, import-accepted,
  global-default. The import allow-list, the loader, the writers and the 32 global defaults all derive from it.
  `RackSettings` is the typed DTO (null = nothing usable stored). `RackSettingsValidator` parses strictly (invariant then
  current-culture numbers, finite only, defined enums, `SafeWpfValues` for fonts and colors, list items kept one by one).
  `RackSettingsMapper` copies between the DTO and `Instance` through its safe setters under `SuspendPersistence`, so a load
  never writes. `RackSettingsRepository` (`Default`, behind `IRackSettingsStore`; `IRackRegistry`/`HkcuRackRegistry` is the seam
  for `Instances\*`) offers `LoadAll()` (a `try` per rack), `ReadGlobalStyleDefaults()`, `Save`, `SaveField`,
  `SaveGlobalDefault`, `Delete` and `ResetGlobalStyleDefaults`; none throws.
- **Moving files.** `Infrastructure/FileSystem/FileOperationBroker` over `Util/SafeMove` (canonicalization, protected roots,
  cross-volume copy-then-delete that removes only what it created) and `Util/SafeDelete` (reparse-point-aware tree delete,
  Recycle Bin). `Rack/WorkspaceReturn` (back to Desktop), `Rack/RackFolderRemoval`, `Rack/OrphanShortcutCleanup`,
  `Util/UninstallCleanup` (returns user files on uninstall, rename-only), `Util/RackMirror` (junction mirror in
  `%USERPROFILE%\Racks`, only links are touched).
- **Shell gateway.** `Infrastructure/Shell/ShellOperationGateway` delegates (no logic) to `Util/Interop` (`SHChangeNotify`, one
  declaration), `Util/DesktopIcons`, `Util/DesktopIconPositioner` and `Core/DesktopIconManager`, which are `internal`. Tests
  replace the calls through an internal `ShellBackend`. Also in `Infrastructure/Shell`: `GlobalMouseHook` (with `DesktopHitTest`),
  `Win32HotkeyApi`, `ScreenHotCornerProbe`.
- **Auto-route.** `UseCases/AutoRoute/`: `AutoRouteService` picks the first rack whose rule matches (regex through
  `Util/SafeRegex`), asks `RoutingPolicy` whether the entry may move (not `desktop.ini`, hidden/system, partial download,
  link, online-only cloud file, locked), moves it through the broker, notifies the shell gateway, claims it for a desktop rack
  and audits every decision. `Util/DesktopRouter` is only the watcher: 64 KB buffer, Created + Renamed, one debounce window per
  burst, recovery from watcher `Error` (rebuild + rescan for names that were not on the Desktop before, backoff), all on the UI
  thread through `IDelayScheduler`. The service has no WPF types: `MainWindow` supplies the rack list, a save callback and the
  workspace path (`AutoRouteHost`).
- **Layout import/export.** `UseCases/Layout/`, one job per class, in the order a file travels: `LayoutParser` (bounded read,
  limits from `LayoutLimits`, BOM-aware decoding, an untrusted `LayoutDocument`; never throws), `LayoutValidator` (version,
  names, duplicates, scalar kinds, every value through `RackSettingsValidator`, a typed `ValidatedRack` with the exact
  `RegistryWrite`s; whole-file refusal for the security rules, while a bad number/switch/enum/list is dropped like the loader would
  and reported as `DroppedValue`) using `LayoutSecurityPolicy`, `LayoutImportExecutor` (snapshot, delete, write, verify, precise
  checked rollback over `IRackRegistry`, and a recovery layout in `%AppData%\Racks\recovery` when the restore fails; the only
  place that writes layouts to the registry), `LayoutExporter` (typed racks to the same JSON shape as ever, written by
  `Infrastructure/Layout/LayoutFileWriter`: temp file next to the destination, then one `File.Move(overwrite)`; it first runs its own
  bytes through parser and validator), `LayoutService` (composes them; results, never exceptions; every failure is an audit
  record) and `LayoutMessages` (the texts, `Lang.resx`). The dialogs are `Views/LayoutDialogs`. `Util/RackLayoutIO` is only a
  30-line `internal` facade that `RackLayoutImportValidationTests` and `RackSettingsSchemaTests` use.
- **Updates.** Logic in `UseCases/Updates/`: `UpdateService` (check and install orchestration; one install at a time; the
  verified offer under a lock; never throws), `ReleaseManifestValidator` (pure: which release, which assets, which URLs, which
  candidate verified), `UpdateMessages`, `UpdateAudit` (failure records in `audit.log`, no query strings) and the ports
  `IUpdateNotifier`, `IUpdateProgress`, `IInstallerLauncher`, `IAppShutdown`. Windows side in `Infrastructure/Update/`:
  `ReleaseCatalogClient` (the only place with the feed URL; response caps and timeouts), `InstallerExecutor` (private folder,
  bounded download, size + SHA-256 through the held-open handle, launch, shutdown), `UpdateCleanupService`,
  `UpdateHttpClientFactory`, `ToastUpdateNotifier`, `ShellInstallerLauncher`, `WpfAppShutdown` (in `UpdateProcessAdapters`) and
  `UpdateComposition` (the production wiring). `Util/UpdateManifest` (pure signature/hash/size checks, `UpdateVerifier`) and
  `Util/UpdateTrust` (embedded public key) stay in `Util`; `tools/RacksSigner` links them by path. Tests build the service with a
  fake HTTP handler, notifier, launcher and shutdown (`test/Racks.Tests/Util/UpdateRig`).
- **Diagnostics.** `Util/CrashLog` (`%AppData%\Racks\crash.log`, rotated), `Infrastructure/Logging` (`audit.log`: one JSON line per
  file operation, rejected settings value, layout or update failure; same rotation; local only), `Services/DiagnosticCounters`.

## Where does new code go

1. **It changes, deletes, renames or creates a file or folder the user can see:** call `FileOperationBroker.Default` from a use
   case. A new kind of operation is a new broker method with validation, a confirm step, an audit record and tests; never a
   `File.`/`Directory.` call elsewhere. If the guard asks for an allowlist entry, the answer is almost always "wrong place".
2. **It reads or writes a registry value:** an app-wide switch or marker is a property on `AppSettings`/`MigrationMarkers`; a
   per-rack value is one row in `RackSettingsSchema` (plus its golden fixture). Presentation never opens the registry.
3. **It talks to the shell (change notifications, desktop icons, workspace folder):** `ShellOperationGateway.Default`.
4. **It decides something (a policy, a sequence, an orchestration):** a class under `Racks/UseCases/<Feature>/`, with its
   dependencies behind interfaces and fakes in `test/Racks.Tests/Fakes/`. No `Dispatcher`, no `MessageBox`, no `Registry`.
5. **It touches Windows or the network (Win32, COM, HTTP, a process, a hook):** an adapter in `Racks/Infrastructure/<Area>/` that
   implements the use case's interface, wired in `AppComposition` / `UpdateComposition`.
6. **It is a window, a binding or a dialog:** presentation; keep the code-behind thin and move the logic to a use case. The
   ratchets will fail if `RackWindow`, `MainWindow` or `App` grow.
7. **It parses a layout file, a regex or a value a user typed or imported:** through the validator (`RackSettingsValidator`,
   `SafeWpfValues`, `PathPolicy`, `SafeRegex`); never trust the raw text.
8. **It touches the updater:** read INV-UPDATE-1 first; the path stays fail-closed.
9. **It is user-facing text:** `Racks/Properties/Lang.resx` (`Lang.Key`), never a hard-coded string.

## Paths that must not move

- `Racks/Util/UpdateManifest.cs`, `Racks/Util/UpdateTrust.cs`: `tools/RacksSigner/RacksSigner.csproj` and
  `tools/install-signer.ps1` / `tools/release-sign.ps1` reference them by path.
- `HKCU\SOFTWARE\Racks\MainHwnd` (REG_SZ, decimal; written by `AppSettings.TryPublishMainHwnd`) and the `WM_RACKS_*` window messages:
  used by `tools/*E2ERunner.ps1`.
- `Guards/baseline.json`, `Guards/fs-allowlist.json` and `Guards/registry-presentation-allowlist.json`: keys stay stable.
- The startup order in `UseCases/Startup/AppBootstrapService` (pinned by `AppBootstrapServiceTests` and `SafeSvgTests`).
- Exactly one test is tagged `Category=ReleaseGate` (`release.yml` expects it).

## Tests

`dotnet test Racks.sln -c Release`. Tests run on real Windows (`windows-latest` in CI) against temp directories, real junctions and
hardlinks. Registry tests redirect the root with `InstanceController.appName` (`RackRegistryFixture`) or use
`InMemoryRegistryRoot`. Time is faked with `Fakes/ManualScheduler` (`IDelayScheduler`) and `ManualRepeatingScheduler`.
The filesystem regression pass is `test/Racks.Tests/Adversarial/` (`dotnet test --filter "Category=Adversarial"`; it also runs in
the normal suite): hostile paths, links, locked files and name clashes on real NTFS in temp folders, with the internal seams
`SafeMove.UseProtectedFoldersForTest` and `SafeMove.UseBeforeCopyHookForTest`, `DesktopIconManager.Setup` and
`RackMirror.Rebuild(racks, mirrorRoot)`. Tests that need a second drive, symlink rights, 8.3 names or a SendTo folder are skipped
with a message (6 skipped today). Classes that call `AuditLog.Use` share the xunit collection `AuditLog`.
