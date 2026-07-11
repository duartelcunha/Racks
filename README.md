<div align="center">

<img src="docs/icon.png" width="96" height="96" alt="Racks icon" />

# Racks

**A floating desktop organizer for Windows.**

<sub>Lives in your tray. Drop files into racks on your wallpaper instead of onto your desktop. Zero clutter, zero cloud.</sub>

<p>
  <a href="https://github.com/duartelcunha/Racks/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/duartelcunha/Racks/build.yml?style=for-the-badge&label=Build" alt="Build" /></a>
  &nbsp;
  <a href="https://github.com/duartelcunha/Racks/releases/latest"><img src="https://img.shields.io/github/v/release/duartelcunha/Racks?style=for-the-badge&label=Download&color=2ea043" alt="Download" /></a>
  &nbsp;
  <a href="https://github.com/duartelcunha/Racks/stargazers"><img src="https://img.shields.io/github/stars/duartelcunha/Racks?style=for-the-badge&color=f5a623" alt="Stars" /></a>
  &nbsp;
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10 / 11" />
  &nbsp;
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  &nbsp;
  <img src="https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge" alt="License: MIT" />
</p>

<br />

<img src="docs/screenshots/demo-3.gif" alt="Dragging files into a rack" width="100%" />

<sub><i>Keep your wallpaper clean. Drag files in, and they live one click away in a safe space.</i></sub>

</div>

---

## Why Racks?

Your desktop shouldn't be a dumping ground. Racks lives quietly in your system tray and lets you create translucent floating panels on your wallpaper. Drop files in and they leave the mess behind for a tidy, safe home you can reach in one click.

- **Native and light.** A fast .NET 10 WPF app, not a browser in a trench coat.
- **Private.** No cloud, no background indexers, no telemetry. Your files never leave your PC.
- **Safe by design.** You can't accidentally delete a file from inside a rack, and removing a rack never touches a real folder.

<br />

## Drag in. Done.

Drop a file, folder, or shortcut onto a rack and it's tidied away instantly.

- **Default drop** — the item is **moved** into the rack (off your desktop, into a private sandbox).
- **`Ctrl` + drop** — a **shortcut** is created instead; the original stays where it is.
- **`Shift` + drop** — force a move even on a link-mode rack.
- **Drag an item out** — it comes back to your desktop as a single file, no duplicate.

Two kinds of rack:

- **Rack** — a private space. Items are moved into a hidden sandbox and shown in the rack.
- **Folder rack** — a live window onto a real folder you pick. Nothing is moved; deleting the rack leaves the folder untouched.

<br />

## Physics that feel real

<div align="center">
  <img src="docs/screenshots/demo-2.gif" alt="Racks sliding like pucks on ice" width="100%" />
</div>

Drag one rack into another and it **glides away like a puck on ice** — real momentum, friction, and a bounce off the screen edge. Pushes chain from rack to rack. Flick a rack and let go while it's moving and it keeps sliding. **Lock** a rack and it becomes a solid anchor nothing can shove. All at a smooth 60fps, and completely idle when nothing's moving.

<br />

## A rack for every mood

<div align="center">
  <img src="docs/screenshots/demo-1.gif" alt="Custom themes and live styling" width="100%" />
</div>

Every rack is independent. Dial in your own colors, fonts, opacity, and icon sizes. Add a drop shadow or a gradient, drop a background image, or strip everything away for pure glass that lets your wallpaper shine through. Six one-click themes to start from. Collapse any rack to just its title bar with the chevron.

The settings panel snaps next to the rack you're editing and updates **live** — no Apply buttons. Real-time hex color pickers, live font previews, and one-click style copying between racks.

<br />

## A safe space for your files

- **No accidental deletes.** The Delete option is blocked for files inside a rack, so you can't wipe a folder a rack points at. Want it gone? Drag it out first.
- **Open in File Explorer** — one click from any rack item to reveal the real file in its folder.
- **Removing a rack returns everything** to your desktop, laid out in a clean grid.

<br />

## Everything else

- 🪄 **Magic Organizer** — one click analyzes your desktop with on-device AI, groups files into smart categories, and (after you confirm) builds them into racks or folders.
- 🔍 **Quick Finder** — `Ctrl+Shift+Space` searches across every rack instantly.
- 🤖 **Auto-Routing** — set a regex per rack; new matching files landing on your desktop fly straight into it.
- 🔄 **Live refresh** — change a rack's folder in Explorer and the rack updates itself.
- 🖥️ **Multi-monitor smart** — windows open on the screen you're on; racks snap back to your primary display when a monitor is unplugged.
- 🎬 **Premium polish** — one coherent, animated interface across every window, from the startup sequence to every dialog.
- ✈️ **Portable layouts** — export every rack, theme, and setting to a single JSON file; restore on a new PC in one click.

<br />

## Get it running

<div align="center">

### [⬇️ Download the latest release](https://github.com/duartelcunha/Racks/releases/latest)

</div>

Grab `Racks-Setup-<version>.exe` from the latest release and double-click it. The installer takes a couple of seconds, asks for no admin prompts, and drops Racks straight into your system tray. Right-click the tray icon to make your first rack.

> [!NOTE]
> **Windows SmartScreen** may show a blue "Windows protected your PC" popup because Racks is an indie app without an expensive corporate signing certificate. Click **More info** → **Run anyway**. It's 100% open source and safe.

<br />

## Cheatsheet

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+N` | New rack |
| `Ctrl+Shift+Space` | Quick Finder |
| `Ctrl` + drop | Create a shortcut, keep the original |
| `Shift` + drop | Force move onto a link-mode rack |
| `Alt` + drag | Bypass grid snapping while moving a rack |
| `Ctrl` + scroll | Resize icons |
| `F2` | Rename the selected item |
| Scroll on title bar | Bring a rack forward / send it behind |
| Double-click wallpaper | Hide / show all racks |

<br />

## Build it yourself

Requires the **.NET 10 SDK** (and **Inno Setup 6** for the installer).

```powershell
# Run from source
dotnet build Racks/Racks.csproj -c Debug
dotnet run   --project Racks/Racks.csproj

# Build the distributable installer
.\build-installer.ps1   # -> installer\Output\Racks-Setup-<version>.exe
```

Curious how it works? [`ABOUT.md`](ABOUT.md) explains the design and gives a map of the codebase.

<br />

## Star the repo ⭐

If Racks cleans up your workflow, **[drop a star](https://github.com/duartelcunha/Racks)**. It's the best way to help others find it.

<br />

## License

Racks is free and open source under the **MIT License** — see [`LICENSE.txt`](LICENSE.txt).

It incorporates code originally distributed under the MIT License; required attribution and the full upstream license texts are in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
