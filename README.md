<div align="center">

<img src="docs/icon.png" width="96" height="96" alt="Racks icon" />

# Racks

### A floating desktop organizer for Windows.

<sub>Tray-resident. Drop files into racks instead of onto your desktop. Doesn't touch the originals.</sub>

<p>
  <a href="https://github.com/duartelcunha/Racks/releases/latest"><img src="https://img.shields.io/github/v/release/duartelcunha/Racks?style=for-the-badge&label=Download&color=2ea043" alt="Download" /></a>
  &nbsp;
  <a href="https://github.com/duartelcunha/Racks/stargazers"><img src="https://img.shields.io/github/stars/duartelcunha/Racks?style=for-the-badge&color=f5a623" alt="Stars" /></a>
  &nbsp;
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10 / 11" />
  &nbsp;
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
</p>

<br />

<img src="docs/screenshots/demo-3.gif" alt="Dragging files from the desktop into a rack" width="100%" />

<sub><i>Drag files off the desktop, drop them onto a rack. They land in a clean sandbox. Your wallpaper stays empty, your files stay one click away.</i></sub>

</div>

---

## What it does

Racks lives in your system tray. Click **New rack** and a floating window appears on your wallpaper. Drag files into it and they're moved into a clean sandbox, one click away. Pin it, theme it, regex-route to it, search across every rack with a hotkey.

That's it. No cloud, no telemetry, no background indexer.

<br />

## Drag in. Done.

Anything you drop on a rack (file, folder, browser tab, app shortcut) lands in a clean sandbox in AppData. Your desktop stays empty without you ever opening a file manager.

Hold `Ctrl` to keep the original where it was. Hold `Shift` to drag a tile *out* of the rack into any other app. That's the entire mental model.

<br />

## Two racks, your way

<div align="center">
  <img src="docs/screenshots/demo-1.gif" alt="Personal and Work racks side by side with their own themes" width="100%" />
</div>

Spin up as many racks as you want. Each one is its own little world: title, color, opacity, icon size, font, even per-rack regex filters. Pick a theme preset or dial in every hex value yourself.

<br />

## Folds out of the way

<div align="center">
  <img src="docs/screenshots/demo-2.gif" alt="Racks collapse to their title bar to save desktop space" width="100%" />
</div>

Click the chevron and a rack collapses into its title bar, taking up almost no space but still one click away. Double-click empty wallpaper to hide every rack at once. A hot corner brings them back.

<br />

## Settings that follow the rack

The new settings panel docks beside the rack you're editing and **follows it** as you drag the rack around. Every change applies live: colors, fonts, opacity, animation speed, grayscale, icon size. No "Apply" button.

- Five tight pages: **Style**, **Title**, **Items**, **Window**, **Folder**.
- Font picker shows every installed font rendered in its own face (with scrollbar, virtualized, type-to-search).
- Color picker pops directly under the swatch you clicked. Hex inputs validate live; checkerboard preview shows transparency.
- One-click **Revert** if you want to discard every change in the session.
- **Change style** copies the look of any other rack onto this one.

<br />

## Also in the box

- 🔍 **Quick Finder.** `Ctrl+Shift+Space` opens a Spotlight-style search across every rack. Type, `Enter`, you're in the file.
- 🪄 **Magic Organizer 2.0.** Don't want to drag files manually? Click "Magic Organize" and Racks will scan your Desktop, cluster related files automatically, and create a perfectly centered, staggered grid of Racks instantly.
- 💥 **Pushable Physics.** Racks are solid objects! Drag one Rack into another and watch it smoothly push the other out of the way, keeping everything strictly within your screen bounds.
- 🎬 **Premium Animations.** From the 60fps signature startup animation to bouncy elastic pop-ins and springy file drops, everything feels alive and polished.
- 🤖 **Auto-route by regex.** Per-rack patterns (`\.pdf$`, `^Invoice-`, anything). Matching files dropped on the Desktop are moved in automatically. Screenshots into "Screenshots," invoices into "Finance."
- 📂 **Lives in every file picker.** Racks are pinned to Explorer Quick Access on first launch. Uploading from a browser? Click "Racks" in the sidebar, click the rack, done.
- 🛡️ **Safe by design.** Removing a rack only ever deletes its own sandbox. Point a rack at `Documents` and remove it, and `Documents` is exactly as you left it.
- 🖥️ **Multi-monitor aware.** Unplug a screen and racks snap back to the primary instead of being stranded off-screen.
- ✈️ **Round-trip your layout.** One JSON file exports every rack, every theme, every setting. Restore on a new machine in one click.
- 🪟 **Virtual-desktop aware.** Tell a rack which virtual desktops to appear on; it stays hidden everywhere else.

<br />

## Install

<div align="center">

### [⬇️ Download the latest release](https://github.com/duartelcunha/Racks/releases/latest)

</div>

Download `Racks-Installation.zip` (v1.1.0) and extract it. **No admin prompt, no choices, ~5 seconds.** Run `Racks.exe`. Racks starts in the system tray; right-click the tray icon to create your first rack.

<br />

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+N` | New empty rack |
| `Ctrl+Shift+Space` | Quick Finder (cross-rack search) |
| `Ctrl`-drop | Link on this drop (keep original on Desktop) |
| `Shift`-drop | Move on this drop (override Link-on-drop toggle) |
| `Shift`-drag from rack | Drag an item *out* of the rack into another app |
| `Alt`+drag rack | Bypass snap-to-grid |
| `Ctrl`+scroll inside rack | Resize icons |
| Double-click wallpaper | Hide / show all racks |

Right-click the tray icon for the global menu (new rack, hide desktop, import/export layout, settings). Right-click any rack's title bar for per-rack options.

<br />

## FAQ

**Does Racks move my files around behind my back?**
No. A default drop *moves* the file from the Desktop into the rack's sandbox (so the Desktop stays clean). Hold `Ctrl` to keep the original where it was. Removing a rack only ever deletes its own sandbox, never a real folder you pointed it at.

**Will it slow my PC down?**
Idle CPU is ~0%. Memory sits around 60 to 90 MB. No background indexer.

**Does it phone home?**
No telemetry, no analytics, no auto-update pings. A single `.exe` talking to the Windows shell and the registry.

**What if Explorer crashes or I unplug a monitor?**
Racks listens for `WM_DISPLAYCHANGE` and the Explorer `TaskbarCreated` message and recovers automatically. Racks on a disconnected monitor snap back to the primary instead of being stranded.

<br />

## Star the repo ⭐

If Racks earns a spot on your machine, **[give it a star](https://github.com/duartelcunha/Racks)**. Stars are how this project gets discovered by other people drowning in desktop clutter, and the only feedback signal that tells me the work is worth continuing.

<br />

## License

Racks is proprietary software. © 2026 Duarte L. Cunha. All rights reserved.
Free to install and use; redistribution, modification, and reverse engineering are not permitted. See [`LICENSE.txt`](LICENSE.txt).

Third-party components and required upstream attributions are listed in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
