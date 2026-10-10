<div align="center">

<img src="docs/icon.png" width="96" height="96" alt="Racks icon" />

# Racks

**A floating desktop organizer for Windows.**

<sub>Lives in your tray. Drop files into racks on your wallpaper instead of onto your desktop. No cloud, no account.</sub>

<p>
  <a href="https://github.com/duartelcunha/Racks/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/duartelcunha/Racks/build.yml?style=for-the-badge&label=Build" alt="Build" /></a>
  &nbsp;
  <a href="https://github.com/duartelcunha/Racks/releases/latest"><img src="https://img.shields.io/github/v/release/duartelcunha/Racks?style=for-the-badge&label=Download&color=1AAE9F" alt="Download" /></a>
  &nbsp;
  <a href="https://github.com/duartelcunha/Racks/stargazers"><img src="https://img.shields.io/github/stars/duartelcunha/Racks?style=for-the-badge&color=f5a623" alt="Stars" /></a>
  &nbsp;
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10 / 11" />
  &nbsp;
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  &nbsp;
  <img src="https://img.shields.io/badge/License-MIT-5A6672.svg?style=for-the-badge" alt="License: MIT" />
</p>

<br />

<img src="docs/screenshots/hero.gif" alt="Files dragged from a folder into two racks, leaving a clean desktop" width="100%" />

<sub>Two drags and the clutter is gone. <a href="docs/screenshots/hero.mp4">Watch it as an MP4</a>.</sub>

<br />
<br />

<a href="https://github.com/duartelcunha/Racks/releases/latest"><b>⬇️ Download for Windows</b></a>

</div>

---

## Why Racks

Floating panels on your wallpaper that hold your files, so the desktop stays clear.

- **Native and light.** A .NET 10 app in the tray that stops animating when nothing moves.
- **Private.** No account, no cloud, no telemetry.
- **Safe.** Items in a plain rack can't be deleted by accident; removing a rack gives the files back.

<br />

## Drag in. Done.

<div align="center">
  <img src="docs/screenshots/drag-in.gif" alt="A file dropped into a rack, another Ctrl+dropped as a link while the original stays in the folder, and one dragged back out" width="100%" />
</div>

Drop files, folders or shortcuts onto a rack. They move in, out of your way.

| Gesture | Result |
| --- | --- |
| Drop | Moves the item into the rack |
| `Ctrl` + drop | Adds a link, the original stays |
| `Shift` + drop | Forces a move on a link-mode rack |
| Drag out to the desktop | One file back, no duplicate |

A **folder rack** shows a real folder you pick instead of a private one.

<br />

## Physics that feel real

<div align="center">
  <img src="docs/screenshots/physics.gif" alt="A rack pushed into another, which slides into a locked rack and stops; then thrown back to the left" width="100%" />
</div>

Push or flick a rack and it glides like a puck on ice. A **locked** rack (orange) never moves.

<br />

## A rack for every mood

<div align="center">
  <img src="docs/screenshots/styling.gif" alt="The rack settings panel open next to a rack: turning on the gradient and drop shadow, then changing the background colour to violet, emerald and rose" width="100%" />
</div>

Colors, fonts, opacity, shadow, gradient or pure glass, per rack. Right-click › **Settings**; changes apply live.

<br />

## Find a file across your racks

<div align="center">
  <img src="docs/screenshots/finder.gif" alt="Quick Finder narrowing the list as the letters 'hol' and then 'mo' are typed" width="100%" />
</div>

`Ctrl+Shift+Space`, type, `Enter` to open.

<br />

## Everything else

<details>
<summary><b>More features</b></summary>

<br />

- 🪄 **Magic Organizer.** Groups your desktop files into suggested racks, on your PC. Nothing moves until you confirm; undo right after.
- 🤖 **Auto-routing.** A rack with a name pattern (regex) catches new matching files from the desktop.
- 🔄 **Live refresh.** Racks follow changes made in Explorer.
- 🖥️ **Multi-monitor.** Racks come back to the main screen when a monitor is unplugged.
- ✈️ **Portable layouts.** Export and import your racks as one JSON file (files not included).
- 🔍 **Open in File Explorer** from any item's right-click menu.

</details>

<details>
<summary><b>Keyboard shortcuts</b></summary>

<br />

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+N` | New rack |
| `Ctrl+Shift+Space` | Quick Finder |
| `Ctrl` / `Shift` + drop | Link / force move |
| `Alt` + drag | Ignore Snap to grid |
| `Ctrl` + scroll | Icon size |
| `F2` | Rename the item under the pointer |
| Scroll on title bar | Bring forward / send behind |
| Double-click wallpaper | Hide or show all racks (enable in Settings) |

</details>

<details>
<summary><b>FAQ</b></summary>

<br />

**Where do my files live?** In a hidden `RacksWorkspace` folder in your user profile (folder racks use your folder).

**What happens when I remove a rack?** Its files go back to the desktop. A folder rack on a folder directly on the Desktop asks, then empties and deletes that folder.

**Does it send anything?** Only update checks to GitHub, when you ask or turn on auto-update.

**"Windows protected your PC"?** Indie app, no paid certificate. Click **More info › Run anyway**.

**A hotkey does nothing?** Another app owns it; Racks shows a notification.

</details>

<br />

## Install

Get `Racks-Setup-<version>.exe` from the [latest release](https://github.com/duartelcunha/Racks/releases/latest). No admin rights needed. Right-click the tray icon to make your first rack.

<br />

## Build it yourself

Needs the **.NET 10 SDK** (and **Inno Setup 6** for the installer).

```powershell
dotnet run --project Racks/Racks.csproj   # run from source
.\build-installer.ps1                   # -> installer\Output\Racks-Setup-<version>.exe
```

Design notes: [`ABOUT.md`](ABOUT.md). Contributing: [`CONTRIBUTING.md`](CONTRIBUTING.md). README media: [`docs/media/studio`](docs/media/studio/README.md).

<br />

## License

MIT, see [`LICENSE.txt`](LICENSE.txt). Third-party notices: [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

<div align="center">
<br />

If Racks tidies up your workflow, a ⭐ helps other people find it.

</div>
