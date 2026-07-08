# Racks — what this project is

Racks is a **floating desktop organizer for Windows**. It lives in the system tray and
lets you create translucent "racks" (small floating panels) that sit on your wallpaper.
You drop files, folders, or shortcuts into a rack and they leave your cluttered desktop
for a clean, safe home you can reach in one click. No cloud, no background indexers, no
telemetry.

- **Platform:** Windows 10 / 11, .NET 10 (WPF + WinForms interop).
- **License:** MIT (free and open source).
- **Distribution:** a single `Racks-Setup-x.y.z.exe` installer (Inno Setup), or a portable
  self-contained folder.

## The core idea

Your desktop should not be a dumping ground. A rack is a floating window pinned *behind*
your other windows (it reparents itself as a child of the Windows desktop, `SHELLDLL_DefView`),
so it feels like part of the wallpaper rather than a normal app window. Drag a file in and,
depending on the rack type, it is either **moved into a private sandbox** or the rack simply
**shows a folder you point it at**.

## Rack types

- **Virtual rack** ("New rack"): items dropped in are moved into a per-user sandbox under
  `%UserProfile%\RacksWorkspace` (a hidden folder). The rack "claims" them so they show in
  the rack and are hidden from the desktop. Removing the rack returns everything.
- **Folder rack** ("New folder rack"): bound to a real folder on disk. The rack is a live
  window onto that folder; nothing is moved. Deleting the rack never touches the folder.

## What it does

- **Drag in / drag out.** Default drop moves the item; `Ctrl`+drop creates a shortcut and
  leaves the original; `Shift`+drop forces a move. Dragging an item out onto the desktop
  returns it (no duplicate).
- **Safe space.** Files inside a rack can't be deleted from the rack's right-click menu —
  the shell's Delete verb is intercepted so you can't accidentally wipe a folder a rack
  points at. A custom "Open in File Explorer" entry reveals the real file.
- **Ice-rink physics.** Drag one rack into another and it hands the other velocity — racks
  glide, slow by friction, bounce off screen edges, and chain-push each other, all at 60fps.
  A locked rack is a solid anchor nothing passes through. Flick-to-throw: release a rack in
  motion and it keeps sliding.
- **Live per-rack styling.** Colors, fonts, opacity, icon size, borders, gradient/drop-shadow/
  transparent backgrounds, background images, themes — all applied instantly (no Apply button).
- **Magic Organizer.** One click analyzes the desktop with an on-device ML.NET clustering
  model and groups files into smart racks or folders (with a confirmation first).
- **Auto-routing.** A regex per rack; new matching files on the Desktop are auto-moved in.
- **Quick Finder.** `Ctrl+Shift+Space` searches across every rack instantly.
- **Live refresh.** External changes to a rack's folder (add / delete / rename in Explorer)
  refresh the rack automatically, debounced so bursts don't churn.
- **Multi-monitor aware.** Windows open on the monitor you're on; racks snap back to the
  primary display when a screen is unplugged.
- **Portable layouts.** Export every rack, theme, and setting to one JSON file; restore on
  another PC.

## Design language

Every Racks-owned window (settings, rack settings, about, help, quick finder, message boxes,
the organize preview) shares one look: a dark **gradient card** with a soft rounded border,
a custom title strip, pill buttons, and smooth fade + scale motion. The shared styles live in
`Racks/Resources/CardStyles.xaml`; the fade/close behavior in `Racks/Util/WindowFade.cs`.

## How it's built (map for developers)

- `App.xaml.cs` — startup, single-instance mutex, global crash handlers (log to
  `%AppData%\Racks\crash.log`), the signature startup animation.
- `MainWindow.xaml(.cs)` — the invisible host window that owns the tray icon and menu, global
  hotkeys, and the desktop auto-router.
- `RackWindow.xaml(.cs)` — a single rack: file loading, drag/drop, the shell context menu,
  desktop-child reparenting, per-rack settings, and the physics hooks. (This is the big one.)
- `Core/InstanceController.cs` — creates/loads/persists racks (each rack = an `Instance`,
  stored under `HKCU\SOFTWARE\Racks\Instances`).
- `Core/Instance.cs` — the model for one rack (position, style, folder, assigned files).
- `Core/AutoOrganizer.cs` — the ML.NET desktop-clustering for Magic Organizer.
- `Util/RackPhysics.cs` — the shared 60fps ice-rink physics loop (self-disables when idle).
- `Util/ShellContextMenu.cs` — hand-rolled COM wrapper around the native shell context menu,
  with the delete-protection and "Open in File Explorer" injection.
- `Util/WindowFade.cs`, `Util/WindowPlacement.cs`, `Resources/CardStyles.xaml` — the shared
  UI/UX system.
- `Services/FileWatcherService.cs` — file-system watchers that drive live refresh.
- `test/Test-Racks.ps1` — a UI-automation smoke test that drives the built app and asserts
  the regressions this project has hit (tray icon, no CPU spin, clean exit, no crash).

## Building

- Requires the **.NET 10 SDK**.
- Build/run: `dotnet build Racks/Racks.csproj -c Debug`
- Installer: `.\build-installer.ps1` (needs Inno Setup 6). Produces
  `installer\Output\Racks-Setup-<version>.exe`.
- Version lives in `Racks/Racks.csproj` (`<Version>`); the installer filename tracks it.
