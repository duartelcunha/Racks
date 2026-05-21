<div align="center">

<img src="Racks/ico.png" width="96" height="96" alt="Racks icon" />

# Racks

**A fast, lightweight desktop organizer for Windows.**

<sub>Always at hand. Doesn't touch your files. Built on .NET 10.</sub>

[Download](https://github.com/duartelcunha/Racks/releases/latest) · [Report a bug](https://github.com/duartelcunha/Racks/issues) · [Suggest a feature](https://github.com/duartelcunha/Racks/issues)

</div>

---

<div align="center">
  <img src="docs/screenshots/hero.png" alt="Racks on the desktop" width="900" />
</div>

## What it does

Racks lives in the system tray and gives you floating "racks" — small, transparent windows that group files, shortcuts, and folders right on top of your wallpaper. Drag something from the Desktop into a rack and it moves into a clean sandbox; the Desktop stays uncluttered. Open any file picker and your racks appear in the Quick Access sidebar, named exactly the way you named them.

## Features

- **Move on drop, no clutter.** Drag a file or folder onto a rack — it lands in a clean sandbox in AppData, and your Desktop stays visually empty. Hold `Ctrl` to keep the original instead (hardlink for files, junction for folders).
- **File pickers stay smart.** Every rack is mirrored to `%USERPROFILE%\Racks\<RackName>` and pinned to **Explorer Quick Access** on first launch. Uploading a file from your browser? Click "Racks" in the sidebar, click the rack, done.
- **Auto-route from Desktop.** Set a regex per rack and matching files dropped onto the Desktop are routed in automatically. First match wins.
- **Quick Finder.** `Ctrl+Shift+Space` opens a Spotlight-style search across every rack. `↑↓` to walk, `Enter` to open.
- **Snap, lock, pin, theme.** Snap-to-grid (hold `Alt` to bypass), lock a rack from moving, pin one above other windows, pick from seven coordinated theme presets.
- **Phone-style reorder.** Drag tiles inside a rack to reorder with a smooth animation. Shift-drag to drag items *out* of the rack into other apps.
- **Hide the desktop on demand.** Double-click the wallpaper to hide every rack; a top-right peek hotzone brings them back.
- **Safe by design.** Removing a rack only ever deletes its sandbox in AppData — pointing a rack at `Documents` and removing it leaves `Documents` exactly as it was.
- **Multi-monitor aware.** Disconnect a screen and racks snap back to the primary instead of being stranded.
- **Round-trip your layout.** Export every rack to a single JSON file; import it on another machine and the layout is rebuilt.

<div align="center">
  <img src="docs/screenshots/quick-access.png" alt="Racks mirror in Quick Access" width="46%" />
  &nbsp;
  <img src="docs/screenshots/quick-finder.png" alt="Quick Finder overlay" width="46%" />
</div>

## Install

### Installer (recommended)

Grab the latest `Racks-Setup-x.y.z.exe` from [Releases](https://github.com/duartelcunha/Racks/releases/latest) and double-click it. Installs per-user under `%LocalAppData%\Programs\Racks` — no admin prompt, no choices, ~5 seconds.

### Build from source

You only need the **.NET 10 SDK** — no Visual Studio.

```powershell
git clone https://github.com/duartelcunha/Racks.git
cd Racks
dotnet build Racks.sln -c Debug
.\Racks\bin\x64\Debug\net10.0-windows10.0.26100.0\Racks.exe
```

Portable release builds:

```powershell
.\publish.ps1                 # framework-dependent (small, needs .NET 10 runtime)
.\publish.ps1 -SelfContained  # bundles the runtime (~70 MB, no install needed)
.\publish.ps1 -SingleFile     # everything in one .exe
```

## Usage

Right-click an empty area of the tray icon for the global menu (new rack, quick finder, hide desktop, export/import, etc.). Right-click any rack's title bar for per-rack options.

### Shortcuts

| Shortcut              | Action                                            |
| --------------------- | ------------------------------------------------- |
| `Ctrl+Shift+N`        | New empty rack                                    |
| `Ctrl+Shift+Space`    | Quick Finder (cross-rack search)                  |
| `Ctrl`-drop           | Link on this drop (keep original on Desktop)      |
| `Shift`-drop          | Move on this drop (override Link-on-drop toggle)  |
| `Shift`-drag from rack| Drag an item *out* of the rack to another app    |
| `Alt`+drag rack       | Bypass snap-to-grid                               |
| `Ctrl`+scroll         | Resize icons inside a rack                        |
| Double-click wallpaper| Hide / show all racks                             |

## Screenshots

<div align="center">
  <table>
    <tr>
      <td><img src="docs/screenshots/themes.png" alt="Theme presets" /></td>
      <td><img src="docs/screenshots/settings.png" alt="Per-rack menu" /></td>
    </tr>
    <tr>
      <td align="center"><sub>Seven coordinated theme presets</sub></td>
      <td align="center"><sub>Slim per-rack context menu</sub></td>
    </tr>
  </table>
</div>

## License

MIT. See [`LICENSE.txt`](LICENSE.txt).

## Credits

Built on the shoulders of:

- [WPF UI](https://github.com/lepoco/wpfui)
- [WindowsCommunityToolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [SVG.NET](https://github.com/svg-net/SVG)
- [VirtualDesktop](https://github.com/Slion/VirtualDesktop)
- [H.Hooks](https://github.com/HavenDV/H.Hooks)
- [Microsoft.WindowsAPICodePack.Shell](https://github.com/contre/Windows-API-Code-Pack-1.1)
