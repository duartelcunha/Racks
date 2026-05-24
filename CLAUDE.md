# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Racks is a WPF desktop organizer for Windows 10/11 — a heavily reworked fork of MIT-licensed DeskFrame. It runs as a tray-resident app and shows one floating "rack" window per folder the user has set up. The project is **.NET 10**, single C# project (`Racks/Racks.csproj`), WinExe. AssemblyVersion is `0.8.0` in `Racks/Racks.csproj`.

## Build / run / publish

No Visual Studio required — the original `<COMReference>` items have been replaced with hand-rolled `[ComImport]` wrappers (see `Util/ShellLinkHelper.cs`).

```powershell
# Debug build & run
dotnet build Racks.sln -c Debug
.\Racks\bin\x64\Debug\net10.0-windows10.0.26100.0\Racks.exe

# Portable release into .\publish\
.\publish.ps1                  # framework-dependent (needs .NET 10 runtime)
.\publish.ps1 -SelfContained   # bundles the runtime (~70 MB)
.\publish.ps1 -SingleFile      # one .exe
```

The solution forces `Debug` → `x64` (see `Racks.sln`). The csproj `TargetFramework` includes the Windows SDK version (`net10.0-windows10.0.26100.0`), so the bin output path includes that suffix.

There is **no test project** in the solution and no lint/format script. `dotnet build` is the only check.

`make-icon.ps1` regenerates `Racks/Icon/ico.ico` and `Racks/ico.png`. Note: the script still writes to a hard-coded `DeskFrame\` subpath — it predates the rename and will need a path fix before it works again.

## Architecture

### Entry point and process model

`App.OnStartup` (`Racks/App.xaml.cs`):
1. Calls `InstanceController.MigrateLegacyRegistry()` — one-shot copy of `HKCU\SOFTWARE\DeskFrame` → `HKCU\SOFTWARE\Racks` so users upgrading from DeskFrame keep their racks. **Anything that reads/writes the registry root must go through `InstanceController.appName` ("Racks") — never hard-code "DeskFrame".**
2. Acquires a named `Mutex` (`Global\Racks-SingleInstance-2C9D`) in Release builds. Second launch silently exits. Disabled in Debug.

`MainWindow` is **deliberately invisible** (Width=0, Height=0, off-screen, no taskbar, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`). It exists only to host:
- the tray icon (WPF-UI Tray) defined in `MainWindow.xaml`,
- the global hotkeys (Ctrl+Shift+Space = quick finder, Ctrl+Shift+N = new rack — see `WndProc`),
- the `H.Hooks.LowLevelMouseHook` for double-click-desktop-to-hide,
- the hot-corner DispatcherTimer (`StartHotCornerWatch`),
- and the `DesktopRouter` (Desktop FileSystemWatcher → auto-route by regex).

It also listens for `WM_DISPLAYCHANGE` and the `TaskbarCreated` registered message to recover after monitor changes / Explorer crashes via `InstanceController.CheckFrameWindowsLive()`.

### Core MVC trio

- `Core/Instance.cs` — the model. `INotifyPropertyChanged` with **~60 persisted fields**. Setters call `OnPropertyChanged` which, depending on context, writes to either the per-instance registry subkey (`SOFTWARE\Racks\Instances\<name>`) or the root (when `_settingDefault == true`). The "default" code path is how global defaults get edited in `SettingsWindow`. The list `notGlobalProperties` controls which fields are per-rack-only and never become global defaults.
- `Core/InstanceController.cs` — holds `Instances` (model list) and `_subWindows` (view list), in lockstep. Read path is `InitInstances()` — a giant switch over registry value names that populates an `Instance` per subkey. Write paths are `WriteInstanceToKey` (insert/upsert) and `WriteOverInstanceToKey` (rename, deletes the old subkey at the end). `DuplicateInstance` and `AddVirtualInstance` create a new sandbox folder under `VirtualFramesRoot` and wire up the rack.
- `RackWindow.xaml.cs` — the view (5,000+ LOC). One window per rack. Hosts a `ListView` of `FileItem`s loaded from `Instance.Folder`, plus all UI behavior: drag/drop, snap-to-grid, inline rename, particle effects, grayscale shader, context menus, search overlay, etc.

**Adding a new persisted field to `Instance` requires three coordinated edits**: the property in `Instance.cs`, the write in `InstanceController.WriteInstanceToKey` *and* `WriteOverInstanceToKey`, and a `case` in `InstanceController.InitInstances`'s switch. Forgetting any one of them silently drops the value across launches.

### Virtual vs. folder-backed racks

- **Virtual rack** (`IsShortcutsOnly = true`, `AddVirtualInstance`): folder is a UUID directory under `%AppData%\Racks\VirtualFrames\` (a.k.a. `InstanceController.VirtualFramesRoot`). New default for `New rack` and first launch.
- **Folder-backed rack** (`AddInstance` → `addFolderFrame_Click`): folder picked by the user; rack mirrors that folder's contents.

**Safety invariant**: rack removal is allowed to recursively delete its backing folder **only if** `InstanceController.IsInsideVirtualFramesRoot(path)` returns true. This is the safeguard that prevents the original DeskFrame's "Remove rack on Documents" bug from nuking real folders. Any code that deletes a rack's folder must go through this check.

### Drop semantics

`Instance.LinkOnDrop` — when **false** (default), a drop **moves** the file into the rack's folder; when **true**, it creates a `.lnk` shortcut and leaves the source alone. `Ctrl` while dropping flips the per-drop behavior. `Shift` (in folder-backed contexts) forces a move. The whole drop-handling lives in `RackWindow.xaml.cs` (search for `Drop`, `DragEnter`, etc.).

Shortcut creation goes through `Util/ShellLinkHelper.Create(...)`, a pure-C# `IShellLink`/`IPersistFile` wrapper. **Don't reintroduce `IWshRuntimeLibrary` or Shell32 `COMReference`** — they require full MSBuild (Visual Studio) and break `dotnet build`.

### Auto-routing from Desktop

`Util/DesktopRouter.cs` is a `FileSystemWatcher` on the user's Desktop with a 600ms `DispatcherTimer` debounce. On flush, it walks `Instances` and, for each one with a non-empty `AutoRouteRegex`, runs `Regex.IsMatch` against new filenames. **First match wins** (the foreach `break`s). The route action passed in from `MainWindow` MOVES the matching file into the rack's folder (distinct from the drop default, which is also move) and calls `Util.Interop.NotifyShellMove` + `NotifyShellUpdateDir` so the Desktop view refreshes without F5.

### Layout export/import

`Util/RackLayoutIO.cs` serializes the entire `SOFTWARE\Racks\Instances` subtree to JSON (including `MultiString` values separately) and restores it. Import with `replaceExisting: true` calls `DeleteSubKeyTree` first. Triggered from the tray menu (`ExportLayout_Click`, `ImportLayout_Click` in `MainWindow.xaml.cs`).

### Other notable modules

- `Util/Interop.cs` — Win32 P/Invoke surface (window manipulation, hotkeys, shell-change notifications, taskbar-restart message).
- `Util/ShellContextMenu.cs` — invokes the real Explorer context menu on a file.
- `Util/ThemePresets.cs` — the seven one-click color schemes referenced from `FrameSettingsDialog`.
- `Util/Updater.cs` — present but the auto-update timer in `App.xaml.cs` is a deliberate no-op (no release pipeline in this fork).
- `Shaders/GrayscaleEffect.{cs,ps}` — pixel shader for the "grayscale when inactive" option.
- `QuickFinderWindow` — Spotlight-style cross-rack search, opened by Ctrl+Shift+Space.
- `Properties/Lang*.resx` — localization (cs-CZ, es-ES, it-IT, ko-KR, pl-PL, zh-CN, default en).

### What lives in the registry

Everything user-visible. Global defaults at `HKCU\SOFTWARE\Racks\<ValueName>`. Per-rack settings at `HKCU\SOFTWARE\Racks\Instances\<RackName>\<ValueName>`. Boolean fields are stored as the string `"True"`/`"False"` and parsed back with `bool.Parse` — keep that exact case if you write new ones directly. `LastAccessedFiles` and `CustomOrderFiles` are `MultiString`; everything else is `String` or `DWORD`.

### Conventions

- A property named `MoveFilesOnDrop` exists only in the legacy DeskFrame registry. `InstanceController.InitInstances` reads it and discards it — don't reintroduce it. The current field is the inverted `LinkOnDrop`.
- Use `InstanceController.appName` (currently `"Racks"`) anywhere you'd otherwise hard-code the product name in registry paths or autorun entries. `LegacyAppName` (`"DeskFrame"`) is also defined for one-off legacy-cleanup work like `RemoveFromAutoRun`.
- New global hotkeys: register in `MainWindow.Window_Loaded` with a unique ID constant, handle in `WndProc`, unregister in `OnClosed`.
