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

Your desktop is a good place to keep things in reach and a bad place to keep them in a pile. Racks gives you translucent panels that float on the wallpaper. Each one holds a group of files, and the desktop underneath stays clear.

- **Native and light.** A .NET 10 WPF app, not a web view in a window. It sits in the tray and does nothing when nothing is moving.
- **Private.** No cloud, no telemetry, no background indexer. Your files never leave your PC.
- **Hard to break.** You can't delete a file from inside a rack, and removing a rack never touches a real folder.

<br />

## Drag in. Done.

<div align="center">
  <img src="docs/screenshots/drag-in.gif" alt="Three files dropped into racks, one after the other" width="100%" />
</div>

Drop a file, folder or shortcut onto a rack. By default it is **moved** into the rack's private workspace, so it disappears from wherever it was.

| Gesture | What happens |
| --- | --- |
| Drop | The item moves into the rack |
| `Ctrl` + drop | A link is made and the original stays where it was |
| `Shift` + drop | Forces a move, even on a rack set to link on drop |
| Drag an item out | It comes back as a single file, with no duplicate |

There are two kinds of rack:

- **Rack**: a private space. Items live in a hidden workspace folder and show up in the rack.
- **Folder rack**: a live window onto a real folder you pick. Nothing is moved, and deleting the rack leaves the folder alone.

<br />

## Physics that feel real

<div align="center">
  <img src="docs/screenshots/physics.gif" alt="A rack flicked into another one, which slides into a locked rack and stops" width="100%" />
</div>

Push one rack into another and it glides away like a puck on ice, with momentum, friction and a bounce off the screen edge. Pushes chain from rack to rack. Flick a rack and let go while it is moving, and it keeps sliding. **Lock** a rack and it becomes a solid anchor that nothing can shove (the orange one above). Motion runs at 60 fps and stops costing anything when everything is still.

<br />

## A rack for every mood

<div align="center">
  <img src="docs/screenshots/styling.gif" alt="The rack settings panel open next to a rack: turning on the gradient and drop shadow, then changing the background colour to violet and emerald" width="100%" />
</div>

Every rack is styled on its own: colors, fonts, opacity, icon size, drop shadow, gradient, a background image, or nothing at all for pure glass. Right-click a rack, choose **Settings**, and the panel opens next to it. Type or pick a color and the rack follows as you go, with no Apply button. Collapse any rack down to its title bar with the chevron.

<br />

## Find anything instantly

<div align="center">
  <img src="docs/screenshots/finder.gif" alt="Quick Finder narrowing the list as the letters 'hol' and then 'mo' are typed" width="100%" />
</div>

Press `Ctrl+Shift+Space` and type. Quick Finder searches every rack at once and tells you which rack each result lives in. `↑` `↓` to move, `Enter` to open, `Esc` to close.

<br />

## Everything else

<details>
<summary><b>More features</b></summary>

<br />

- 🪄 **Magic Organizer.** One click analyzes your desktop with on-device AI and groups the files into suggested categories. Nothing moves until you confirm, and the last run can be undone.
- 🤖 **Auto-routing.** Give a rack a regex, and new matching files that land on your desktop fly straight into it.
- 🔄 **Live refresh.** Change a rack's folder in Explorer and the rack updates itself.
- 🖥️ **Multi-monitor aware.** Windows open on the screen you are using, and racks return to your primary display when a monitor is unplugged.
- ✈️ **Portable layouts.** Export every rack, theme and setting to a single JSON file and restore it on a new PC in one click.
- 🔍 **Open in File Explorer.** One click from any rack item reveals the real file in its folder.
- 🧹 **Removing a rack returns your files** to the desktop, laid out in a clean grid.

</details>

<details>
<summary><b>Keyboard shortcuts</b></summary>

<br />

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+N` | New rack |
| `Ctrl+Shift+Space` | Quick Finder |
| `Ctrl` + drop | Create a link and keep the original |
| `Shift` + drop | Force a move onto a link-mode rack |
| `Alt` + drag | Bypass grid snapping while moving a rack |
| `Ctrl` + scroll | Resize icons |
| `F2` | Rename the selected item |
| Scroll on title bar | Bring a rack forward or send it behind |
| Double-click wallpaper | Hide or show all racks (off by default, turn it on in Settings) |

</details>

<details>
<summary><b>FAQ</b></summary>

<br />

**Where do my files actually live?**
Files dropped into a plain rack are moved to a hidden `RacksWorkspace` folder in your user profile. Folder racks show a folder of your choice and never move anything.

**Can I lose a file by deleting a rack?**
No. Removing a rack puts its files back on your desktop. Delete is also blocked for items inside a rack, so drag a file out first if you really want it gone.

**Does it send anything anywhere?**
No. There is no account, no cloud sync and no telemetry.

**Windows says "Windows protected your PC".**
Racks is an independent app without a paid signing certificate, so SmartScreen warns about it. Click **More info**, then **Run anyway**. The source is all in this repository.

**A hotkey does nothing.**
Another app may already own `Ctrl+Shift+N` or `Ctrl+Shift+Space`. Racks shows a notification when it can't register a shortcut. Close or rebind the other app and restart Racks.

</details>

<br />

## Install

Download `Racks-Setup-<version>.exe` from the [latest release](https://github.com/duartelcunha/Racks/releases/latest) and run it. The installer needs no admin rights and puts Racks in your system tray. Right-click the tray icon to make your first rack.

> [!NOTE]
> **Windows SmartScreen** may show a blue "Windows protected your PC" popup, because Racks is an indie app without a corporate signing certificate. Click **More info**, then **Run anyway**.

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

[`ABOUT.md`](ABOUT.md) explains the design and maps the codebase. [`CONTRIBUTING.md`](CONTRIBUTING.md) covers how to send changes.

The images and the hero video above are made with the Remotion project in [`docs/media/studio`](docs/media/studio/README.md), from real screen recordings of the app.

<br />

## License

Racks is free and open source under the **MIT License**. See [`LICENSE.txt`](LICENSE.txt).

It incorporates code originally distributed under the MIT License. The required attribution and the upstream license texts are in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

<div align="center">
<br />

If Racks tidies up your workflow, a ⭐ helps other people find it.

</div>
