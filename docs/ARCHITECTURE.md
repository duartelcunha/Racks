# Architecture

Racks is a single WPF process (`Racks/Racks.csproj`, .NET 10, Windows only). It lives in the tray, owns a set of
always-on-desktop windows ("racks"), and moves, links and lists real files on the user's disk. The risk is not
the network: it is **data loss and surprises in the filesystem, the shell and the registry**. The layout below
exists to keep every dangerous operation in a few audited places.

## Layers (logical, not physical)

Folders were not moved to match these names. The table maps today's folders to layers; new code goes where
the last column says. A dependency may only point **down** the table (Presentation can use Application, never
the reverse).

| Layer | What it may do | Folders today | New code goes in |
|---|---|---|---|
| Presentation | WPF only: windows, bindings, dialogs, delegating to services | `MainWindow*`, `RackWindow*`, `*Window.xaml.cs`, `RackSettingsDialog*`, `Views/`, `ViewModels/`, `Converters/`, `ColorPicker/`, `Shaders/` | stays here, shrinks |
| Application | Use cases and orchestration (what happens when a file is dropped, a route fires, an update is found) | parts of `Core/` (`AutoOrganizer`, `MagicOrganize*`), `Rack/` (`WorkspaceReturn`, `DesktopRackClaims`) | `Racks/Application/` |
| Domain | Entities and rules, no I/O | `Core/Instance.cs`, `Core/FileItem.cs`, `Rack/RackNames.cs`, `Rack/Sorting/` | `Racks/Core/` |
| Infrastructure | Everything that touches Windows: filesystem, shell, registry, HTTP, process, logging | most of `Util/` (`Safe*`, `Shell*`, `Interop`, `RegistryHelper`, `Updater`, `JunctionHelper`, ...), `Services/` | `Racks/Infrastructure/{FileSystem,Shell,Registry,Update,Logging}/` |
| Shared | Small pure helpers used by every layer | `SafeRegex`, `SafeWpfValues`, path and name validation | `Racks/Infrastructure/` until a split is needed |

There is no DI container. `App.OnStartup` wires services by hand; the legacy static
`MainWindow._controller` is being retired step by step (see ratchets below).

## The rules that matter

1. **Destructive file operations go through one gateway** (`Infrastructure/FileSystem/FileOperationBroker`, a
   facade over the internal `SafeMove` and `SafeDelete`). It validates names and paths (`PathPolicy`),
   canonicalizes, rejects protected roots, never follows reparse points blindly and writes one audit record per
   call (`%AppData%\Racks\audit.log`). `Guards/FileMutationGuardTests` fails on raw `File`/`Directory` mutations
   outside it, except those listed in `Guards/fs-allowlist.json` (the list only shrinks; after P2 it holds only Racks-owned state).
2. **The registry is read and written through a typed schema + repository** (phase P6). A bad value is rejected,
   logged with field, raw value, reason and fallback, and never applied.
3. **Presentation does not touch the registry, mutate files, call shell notifications or the updater.**
4. **The update path is fail-closed.** Nothing downloaded runs unless the signed manifest, the host/path
   allowlist, the size and the SHA-256 all check out. See `docs/SECURITY-INVARIANTS.md` (INV-UPDATE-1).
5. **Imports are validated before any state is written** and a bad file leaves the current layout untouched.

Rules 1, 3 and parts of 5 are enforced by tests in `test/Racks.Tests/Guards/`, which work as ratchets: the
numbers in `Guards/baseline.json` may only go down.

## Where things live today

- **Window hosting.** `MainWindow` is a hidden host: tray icon, global hotkeys, IPC (`WM_RACKS_*`), update
  checks and Magic Organize. `RackWindow*` (partial class) is one rack. Both are larger than they should be and
  are being split (phases P7, P8).
- **Persistence.** `Core/InstanceController` loads/saves racks under `HKCU\SOFTWARE\Racks\Instances\<name>`;
  `Core/Instance` writes through on every property change; global switches and migration markers live under
  `HKCU\SOFTWARE\Racks`. Layout export/import is `Util/RackLayoutIO`.
- **Moving files.** `Infrastructure/FileSystem/FileOperationBroker` over the internal primitives `Util/SafeMove`
  (canonicalization, protected roots, cross-volume copy-then-delete) and `Util/SafeDelete` (reparse-point-aware
  tree delete, Recycle Bin), `Rack/WorkspaceReturn` (back to Desktop),
  `Util/UninstallCleanup` (returns user files on uninstall, rename-only).
- **Auto-route.** `Util/DesktopRouter` watches the Desktop and routes new files into racks by regex
  (`Util/SafeRegex` bounds every regex with a timeout).
- **Updates.** `Util/Updater` (flow), `Util/UpdateManifest` (pure signature/hash/size checks), `Util/UpdateTrust`
  (embedded public key). `tools/RacksSigner` links the last two by path.
- **Diagnostics.** `Util/CrashLog` (`%AppData%\Racks\crash.log`, rotated), `Infrastructure/Logging` (`audit.log`: one
  JSON line per file operation, same rotation, local only), `Services/DiagnosticCounters`.

## Paths that must not move

- `Racks/Util/UpdateManifest.cs`, `Racks/Util/UpdateTrust.cs`: `tools/RacksSigner/RacksSigner.csproj` and
  `tools/install-signer.ps1` / `tools/release-sign.ps1` reference them by path.
- `HKCU\SOFTWARE\Racks\MainHwnd` and the `WM_RACKS_*` window messages: used by `tools/*E2ERunner.ps1`.
- `Guards/baseline.json` and `Guards/fs-allowlist.json` are copied to the test output; keep their keys stable (add, don't rename).
- The `OnStartup` order in `App.xaml.cs`: `SafeSvg.Apply()` before `StartupUri`; `DesktopIconManager.Initialize()`
  before any `RackWindow` exists.

## Tests

`dotnet test Racks.sln -c Release`. Tests run on real Windows (`windows-latest` in CI) against temp directories,
real junctions and hardlinks. Registry tests redirect the root with `InstanceController.appName`. Time is faked
with `Fakes/ManualScheduler` (`IDelayScheduler`). Ratchets live in `Guards/`.
