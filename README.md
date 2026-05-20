<div align="center">

# Racks

</div>

<p align="center">
   <i align="center">A fast, lightweight desktop organizer that beats Stardock Fences at its own game.<br><b>Always at hand. Doesn't touch your files.</b></i>
</p>

Racks is a heavily rewritten desktop organizer for Windows 10 / 11. It started from an MIT-licensed codebase (see `LICENSE.txt`) but has been rebuilt: data-loss bugs fixed at the root, build pipeline modernized to .NET 10 with no Visual Studio requirement, ~20 new features stacked on top.

---

## Build & run

You only need the **.NET 10 SDK** — no Visual Studio.

```powershell
# clone, then from the repo root:
dotnet build DeskFrame.sln -c Debug
.\DeskFrame\bin\x64\Debug\net10.0-windows10.0.26100.0\Racks.exe
```

Or for a portable release build:

```powershell
.\publish.ps1                 # framework-dependent (small, needs .NET 10 runtime)
.\publish.ps1 -SelfContained  # bundles the runtime (~70 MB, no install needed)
.\publish.ps1 -SingleFile     # everything in one .exe
.\publish\Racks.exe
```

---

## What's different from upstream DeskFrame

### Safety (this is why the fork exists)

- **Drops never silently move your files.** The old default was `File.Move` from source → rack folder, which is how files seemed to "vanish" after a drop. New default: a `.lnk` is created in the rack and the source is untouched. Hold `Shift` while dropping if you actually want to move.
- **"Remove rack" can never delete a real folder.** The recursive `Directory.Delete` is now sandboxed to `%AppData%\Racks\VirtualFrames\…`. Pointing a rack at `Documents` and removing it leaves `Documents` exactly as it was.
- **Fixed a broken drop-path guard** that always evaluated false (impossible AND-chain) and let drops fall through to unsafe code paths on un-initialized frames.
- **Empty-rack drops handle bootstrapping properly** instead of producing self-pointing shortcuts or shortcuts in the working directory.

### Build / tech

- **.NET 8 → .NET 10** with bumped NuGet packages.
- **No more `<COMReference>` items.** The original needed Visual Studio for Shell32 + IWshRuntimeLibrary COM imports; both are gone. Shell32 wasn't used at all; the `.lnk` writer is now a pure-C# `[ComImport]` wrapper around `IShellLink`. `dotnet build` works on a fresh box.
- **Registry migration.** First launch copies `HKCU\SOFTWARE\DeskFrame` → `HKCU\SOFTWARE\Racks` so existing setups carry over.
- **Single-instance Mutex** instead of the racy `Process.GetProcessesByName` check (which popped a stuck modal dialog).

### New per-rack features (right-click the rack title)

- **Move files on drop** toggle — flip the safe default if you want move semantics on this rack.
- **Snap to grid** — 16px grid; hold `Alt` while dragging the rack to bypass.
- **Pin to top** — rack stays above all other windows.
- **Auto-route from Desktop** — regex matched against new files on the Desktop; matches auto-create a `.lnk` in this rack. First-match-wins, 600ms debounce.
- **Set background image** — PNG/JPG/etc. fills the rack (Stretch=UniformToFill).
- **Theme presets** — Dark, Light, Glass, Neon, Solarized Dark, Solarized Light. One click sets 7 coordinated color fields.
- **Refresh thumbnails** — re-fetches every icon (fix stale/blank icons).
- **Show in Explorer** — opens the rack's backing folder.
- **Reset position** — recenter the rack on the primary monitor.
- **Duplicate rack** — copies colors/fonts/regex/snap into a new empty rack.
- **Inline rename** — double-click the rack title, Enter saves, Esc cancels.

### New global / tray features

- **New rack** (Ctrl+Shift+N) — empty virtual rack, drag any shortcut/file/folder in.
- **New folder rack** — bind to a folder you pick by dragging it in.
- **Cross-rack quick finder** (Ctrl+Shift+Space) — Spotlight-style search across every rack's items. ↑↓ navigate, Enter opens, Esc dismisses.
- **Export racks / Import racks** — round-trip your entire layout to/from a single JSON file.
- **Hide desktop icons** — toggle the Windows desktop icons for a clean racks-only desktop.
- **Hide racks on top-right corner** — Fences-style peek hotzone.
- **Lock all racks** — freeze every rack in one click.
- **Help / cheatsheet** — searchable summary of every binding and feature.

### Smarter UX

- **Search-as-you-type** with `↑↓` to walk filtered items and `Enter` to open the highlighted one.
- **Offscreen-rack rescue** — when a monitor disappears, racks snap back to the primary instead of being stranded.
- **First-launch experience** — opens a virtual rack ready to receive a drop instead of an unbound empty folder picker.

---

## Original features kept

- Instant search, fully customizable colors, transparent backgrounds, hidden files / file extensions toggles, Alt-drag to rearrange items, sticky+lockable racks, `Ctrl`+scroll icon resize, scroll-on-titlebar to bring forward/back, sorting (name/date/type/size), first-row last-accessed, regex filter, per-virtual-desktop visibility, double-click-desktop hide, grayscale.

---

## License

MIT. See `LICENSE.txt`. The original DeskFrame copyright line is retained as required by the MIT terms; everywhere else (UI, README, assembly metadata) is rebranded.

## Credits

- [WPF UI](https://github.com/lepoco/wpfui) — MIT
- [WindowsCommunityToolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit) — MIT
- [SVG.NET](https://github.com/svg-net/SVG) — MS-PL
- [VirtualDesktop](https://github.com/Slion/VirtualDesktop) — MIT
- [H.Hooks](https://github.com/HavenDV/H.Hooks) — MIT
- [Microsoft.WindowsAPICodePack.Shell](https://github.com/contre/Windows-API-Code-Pack-1.1)
