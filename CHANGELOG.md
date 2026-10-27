# Changelog

All notable changes to Racks are listed here. Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Security
- **Updates must be signed.** The updater now installs a release only if its `Racks-Setup-<v>.manifest.json` is signed (ECDSA P-256) by the key built into Racks, names exactly that release, is newer than what you run, and matches the downloaded installer's size and SHA-256. The installer is downloaded to a private folder and kept locked from the check until it starts. Anything missing or wrong: nothing runs and you see why. The signing key never touches GitHub, so from this version on a compromised GitHub account cannot push an installer to your install without the owner's review and signature. The updater looks through recent releases for the newest correctly signed one, so a broken newer release cannot hide it, and its notifications show only the verified version number. A release that cannot be verified is never named or linked; you are told only that Racks can't verify it. See `docs/RELEASING.md`. Racks 1.4.0 and earlier cannot check signatures. They take this update the old way. Update now, or download this version from the Releases page and check it against SHA256SUMS.txt. Signatures are checked only from this version on.
- **Layout import only accepts local folders.** An imported layout could point a rack at a network share (`\\host\share`), so new Desktop files could be routed there and Windows would send your sign-in hash to that host. Imported `Folder` and background image paths must now be local drive paths (`C:\...`), value names are checked without regard to case, and only settings Racks itself saves are imported. Folders you choose yourself in the app can still be network folders. Font names and colors could do the same (`\\host\share\#Font`, `ContextColor file://host/...`): they are now accepted only as plain font names and colors WPF can read, on import and when Racks loads its settings (global defaults included), and a background image is only loaded from a local drive. SVG thumbnails no longer load external images or elements, so an `.svg` that points at a web address or a network share cannot make Racks contact it.
- **Layout import refuses oversized and malformed files.** A layout file is now checked before anything is imported: it must be at most 2 MB, hold at most 500 racks and be an ordinary JSON layout (not deeply nested, not from a newer Racks version), and every number must be in range (an opacity of 5 or an icon size of 4 is refused instead of being cut), with a message that names the rack and the value. Two racks that differ only in letter case, or a rack whose `Name` value does not match, are refused too. The whole file is refused at the first problem and nothing is changed. If a replace fails halfway, your previous racks are put back and the result is checked; if they cannot be fully put back you are told, and the details are written to `audit.log`.
- **Release pipeline.** CI now only builds a draft release (pinned actions, least-privilege jobs, pinned and checksummed Inno Setup); signing and publishing happen on the owner's machine, only for a release tag signed with the owner's tag key, with a signer built once from a reviewed commit, and only after the owner confirms the list of changes. The signer installer runs only as the copy taken from the commit it installs, shows changes to every build input (including `Directory.Build.*`, `global.json`, `NuGet.config` and `.gitattributes`), and pins hidden files too. A signing run that fails or is stopped removes the signed files from the draft.

- **Inline rename can no longer move an item.** Renaming a file or folder in a rack used the typed text as a path, so a name like `..\x`, `sub\x` or `C:\x` moved the item to another folder. A typed name must now be a plain name: slashes, drive letters, `..`, `:`, reserved device names (`CON`, `NUL`, `COM1`...), a trailing dot or space and names over 255 characters are refused, with the reason shown and the item left where it was. Renaming only the case of a name (`photo.jpg` to `Photo.jpg`) works.
- **Every destructive file operation is now audited.** Moves, renames, deletes and folder creation by Racks go through one gateway that checks the paths, refuses protected folders and writes one line per operation to `%AppData%\Racks\audit.log` (kept on your PC, rotated, never uploaded). The log contains file and folder names; only your profile folder path is shortened to `%USERPROFILE%`, so read it before sharing it. Sending an item to the Recycle Bin is refused on network and removable drives instead of deleting it permanently.

- **Folders that contain a protected folder can no longer be moved or deleted by Racks.** Until now only the protected folder itself (Desktop, Documents, ...) was refused. A drop, move, rename or delete of a folder that holds one of them, such as `AppData`, `C:\Users` or your OneDrive folder when Desktop and Documents live in it, is now refused with the reason shown.

### Fixed
- **Auto-route no longer moves half-written files.** A file still being downloaded (`.crdownload`, `.part`, `.tmp`..., or a `name.part` next to it), an Office `~$` lock file, `desktop.ini`, hidden or system items and shortcuts to folders (junctions, symbolic links) are left on the Desktop. Cloud files that are only online (OneDrive "available when online") are left too, because moving them out of the sync folder could download them or delete them on your other devices. A download is routed when the browser renames it to its final name. A file that another program still has open is checked again for about 30 seconds and then left where it is, with a line in `audit.log`. Each routing decision (moved or skipped, and why) is now written to `audit.log`.
- **Auto-route recovers after the Desktop watcher fails.** When Windows drops file events (a burst of hundreds of new files) or the watcher stops, Racks rebuilds it and picks up the files that arrived meanwhile instead of silently stopping. Files that were already on your Desktop are not touched.
- **A bad auto-route rule can no longer stall Racks.** In the rule dialog, a pattern that is not valid, or that takes too long on a test name, is refused with a message and the previous rule stays. A rule that was already saved is never changed or deleted; if it turns out to be unusable, that rack simply does not auto-route and the reason is written to `audit.log`.
- **Magic Organize in "Folders" mode** created desktop folders from the raw group name. Names with characters Windows does not allow (or `..`, or a reserved name) are now turned into a plain folder name, like in "Racks" mode.

- **One damaged rack no longer stops the others.** If the saved settings of one rack contained something Racks could not read, the racks after it were not loaded at all and no rack was shown. Now each rack is read on its own: the damaged value is ignored (that setting keeps its normal default), the other racks and the other settings of that rack load as usual, and a line in `audit.log` says which value was ignored and why. A rack whose saved name is unsafe is skipped (its saved data stays untouched), and a rack with no saved name uses its registry key name.

### Changed
- Exporting a layout writes the file in one step (a temporary file next to it, then a swap), so a failed export leaves the previous file as it was. Export and import failures are now shown in a message (before, only a bad layout file was) and every import and export leaves a line in `audit.log`. Importing a file with no racks says so.
- Imported numbers are saved the way Racks saves them itself (whole numbers as numbers, decimals as text with a dot).
- In a virtual rack, an item dragged out with a Move that Explorer completes as a copy (another drive) is no longer deleted from the rack's folder when it is the only copy of that file; the original stays in the rack until you remove it, so you may see it twice. Racks now only removes its own shortcuts and extra names of a file.
- The uninstaller also removes `audit.log`, like `crash.log`.
- A setting in the registry that Racks cannot use (wrong type or unreadable) now falls back to its default and leaves one line in `audit.log`; before, it was ignored silently. Nothing you saved is changed.
- Rack settings are checked when they load: numbers that are not numbers (`NaN`, infinity) or are far outside what the settings window allows, unknown alignment values and similar are ignored and logged instead of being applied. Numbers are now saved with a dot whatever your Windows language (older versions saved a comma on some systems); both are read, but only text saved in the language it was saved in reads back correctly in the old comma form. Loading no longer rewrites your saved settings, except that a rack whose only problem is a number outside its limits (for example an icon size of 4) or a missing name is saved once in corrected form. **Do not go back to an older Racks version after this one** (or open and re-save your racks there): an older version on a Windows language that uses a space between thousands (Portuguese - Portugal, French) ignores the dot numbers and uses defaults, and on languages that use a dot between thousands (German, Spanish, Italian, Portuguese - Brazil, Dutch) it reads `0.4` as 4 and `321.75` as 32175, which can put racks far off screen.
- The installer is per-user only. The unsupported "install for all users" choice is gone. If you installed that way, uninstall that copy from Apps & Features after updating.

## [1.4.0] - 2026-10-10

### Added
- **Theme menu.** Right-click a rack > Theme: Dark, Light, Glass, Neon, Solarized Dark, Solarized Light, applied live and saved. The two light themes no longer draw a white glow around dark text.

### Fixed
- **Physics.** A rack you pushed into another froze for up to a third of a second and then jumped; glides now follow the cursor and stay smooth. A hit rack slides away from the rack that hit it instead of jittering back, slow glides no longer stall, and throws use the speed of the last 80 ms of the drag. Position is saved once per drag instead of on every mouse move.
- **Collapsed racks lost their name.** With the drop shadow on, collapsing left a 10 px sliver; it now shows the title bar and name.
- **Ctrl+drop onto a rack** created the link but the item never showed in the rack. Dragging an item out of a rack now removes it from the rack.
- **Quick Finder:** the selected result was black text on a dark accent colour; it is now white on a neutral bar and the path line is readable.
- **No file in two places.** A move between drives whose delete fails no longer leaves the original next to the copy. Removing a rack when the Desktop already has a file with that name returns it as "name (from Racks).ext" instead of leaving it hidden. A file belongs to one rack only.
- **Magic Organize** reused the rack names of the previous run, overwriting its racks (undo could close the wrong one). New racks get unique names.
- **Auto-routing** (a rack's name pattern) did nothing on normal racks; it now works on every rack.

### Changed
- New README with a hero demo and a short clip per feature; the Remotion project that renders them is in `docs/media/studio`.

## [1.3.1] - 2026-10-08

### Fixed
- **Removing a rack could permanently delete the files inside it.** A rack created with "New folder rack" by dropping a file keeps the dropped files in its own folder under `%AppData%\Racks\VirtualFrames` (dropping moves them there). "Remove" deleted that folder without using the Recycle Bin, and the dialog wrongly said it only held shortcuts. Removing such a rack now moves its files back to the Desktop (never overwriting; name clashes get "(from Racks)") and only then deletes the empty folder. If something cannot be moved, for example an open file, the rack is kept and nothing is deleted.

### Changed
- Rack sorting (natural name order, dates, type, size, folders first or last, custom drag order, recently used first) lives in `Rack/Sorting/FileSorter.cs`, a plain class with tests, instead of inside the window.
- `RackWindow.xaml.cs` (5,600 lines) is split into focused partial files: sorting, Win32/desktop hosting, moving, hover, search, drag and drop, files and thumbnails, item interactions, the rack menu, and visuals. No behaviour change; the main file is now about 640 lines. Ratchet tests stop any part, and the parts together, from growing.
- Every window now uses one shared font list with Chinese, Japanese and Korean fallbacks, and the app tells WPF the interface language (not the regional format), so translated text picks the right glyph forms. `RACKS_LANG` previews a language, including its fonts.

## [1.3.0] - 2026-10-08

A stabilisation release: it fixes the bugs found in a full audit of the code.

### Fixed: Settings and your data
- Racks lost their "transparent background" setting on restart. It is now saved and loaded.
- Renaming a rack (or choosing a new folder for a missing-folder rack) could drop settings (drop shadow, gradient, disabled animations, and the files a desktop rack owns), and picking a folder with the same name deleted the rack's saved settings entirely, so it vanished on restart. Both save paths now share one list of settings.
- "Reset default style" no longer wipes unrelated settings. It used to delete every saved global value, which turned physics, hide-icons and start-on-login back to defaults and replayed the first-run welcome and one-time migrations. It now removes only the default rack style, and asks for confirmation first.
- Hovering a rack in Settings > Manage racks no longer saves its green highlight. A crash while hovering could leave the rack's border green for good.
- New racks move dropped files by default (Ctrl while dropping still makes a shortcut), matching the README and Help. The default used to say "link" while the tray menu created racks that moved.
- The tray's "Lock all racks" switch now shows the real state: on when every rack is locked, refreshed each time the menu opens.
- Settings > "Auto update" now does what it says: with it on, Racks checks GitHub for a new release shortly after startup and shows a toast if there is one. It used to add or remove Racks from Windows startup (that is the tray's "Start on login") and never checked for updates. It is off by default.

### Fixed: Desktop and Explorer
- **"Hide desktop icons" also hid every rack.** Racks live inside the desktop window, and the option hid that whole window. It now hides only the icon list, so racks stay visible.
- Desktop icons hidden by Racks are shown again when Racks exits normally (tray Exit, an update, Windows shutdown), when you uninstall, and a desktop left blank by an older version is repaired at startup. Icons you hid yourself with Windows' own option are never touched.
- Magic Organize placed racks using screen pixels as if they were WPF units, so on a scaled display (125%, 150%...) the grid was off-centre. It now converts first.
- Quick Access pinning no longer depends on English and Portuguese menu captions. It pins once with the language-independent command and checks first whether the folder is already pinned.
- Sorting: clicking Name, Date modified, Date created or Type in a rack's sort menu never flipped between ascending and descending, and Size flipped from the wrong starting points. Clicking the active key now flips its direction; another key starts ascending.

### Fixed: Stability and speed
- When Explorer restarts, racks no longer freeze Racks (and the tray icon) for up to 10 seconds waiting for the desktop window. They check once, keep checking in the background for up to 30 seconds, and attach as soon as it appears.
- A rack no longer piles up timers and event handlers over a long session: the mouse-leave timer is created once instead of on every mouse-leave, the shell right-click menu keeps at most one "closed" and one "rename" handler (a rename handler from a menu where Rename was never chosen used to fire on a later menu), and list items are wired once instead of every time the list refreshes.
- A rack showing a folder no longer reloads over and over while a file in it is being downloaded or copied. Content changes are now reported once the file has been quiet for a moment; new, deleted and renamed files still show up immediately.
- If the folder watcher loses events (a burst of changes overflowed its buffer), the rack now rebuilds the watcher and rescans instead of silently going stale.
- `crash.log` no longer grows forever: at 1 MB it moves to `crash.log.1` (replacing an older one), so at most about 2 MB is kept. The uninstaller removes both.
- When Racks swallows an unexpected error to keep running, it now says so once per session in a notification and points to `crash.log`. Before, you never knew anything had gone wrong.

### Fixed: Updates and shortcuts
- "Check for updates" now reports a failure the first time too. It used to say nothing when GitHub could not be reached on the first click.
- Installing an update now closes Racks normally instead of killing it, so the single-instance lock is released and the new version starts cleanly.
- If another app already owns Ctrl+Shift+Space or Ctrl+Shift+N, Racks now says so in a notification instead of the shortcut silently doing nothing.

### Changed
- Removed about 1,150 lines of dead code (an unused second file loader and thumbnail service, unused menu and drag helpers, leftover commented-out blocks). No behaviour change; `RackWindow.xaml.cs` went from 5,870 to 5,603 lines.
- Repository maintenance: cleaned contributor metadata from the git history.
- Comments and the Magic Organize group-name code no longer claim auto-route makes shortcuts; it moves files.

## [1.2.1] - 2026-10-08

### Fixed
- **Uninstalling Racks could permanently delete files you had moved into racks.** Racks created with "New folder rack" by dropping a file (and the "New rack" of older versions) keep moved files under `%AppData%\Racks\VirtualFrames`, and the uninstaller deleted that folder without using the Recycle Bin. The uninstaller now moves every file from your racks back to the Desktop first and only removes folders that are empty.
- The uninstaller no longer leaves the hidden `RacksWorkspace` folder, the `%UserProfile%\Racks` shortcuts folder, the Quick Access pin or the "Desktop (Workspace)" library behind, and it shows desktop icons again if Racks had hidden them.
- The uninstaller no longer briefly starts a full copy of Racks in the background (which could recreate the shortcuts folder and write a crash log after uninstalling).
- Name clashes on the Desktop are never overwritten (`name (from Racks).ext`). Anything that cannot be moved safely, such as an open file or a file on another drive, stays in a visible `RacksWorkspace` folder with a note, and the uninstaller tells you where.

## [1.2.0] - 2026-10-08

### Added
- Translations: `dotnet build` now regenerates `Lang.Designer.cs` from `Lang.resx`, so adding a string or a language no longer needs Visual Studio. See `CONTRIBUTING.md`.
- `RACKS_LANG` environment variable to preview a language without changing Windows.
- Unit tests (`test/Racks.Tests`, xUnit): `SafeRegex`, updater version compare, `RackMirror.Sanitize`, and translation checks. Ratchet tests stop `RackWindow.xaml.cs`, `MainWindow._controller` usage and hardcoded XAML text from growing.
- CI: format check, tests, test-result and portable-build artifacts. A `v*` tag now builds the installer, a portable zip and `SHA256SUMS.txt` and publishes the release.
- Repository files: `.editorconfig`, `Directory.Build.props`, `global.json`, Dependabot, PR template, `CONTRIBUTING.md`, this changelog.

### Changed
- Tray menu: every item and tooltip now comes from `Lang.resx`, so it can be translated. New strings fall back to English until a translator fills them in.
- Tray tooltips corrected: "New rack" moves dropped files (Ctrl+drop makes a shortcut), Import no longer claims a restart is needed, Magic Organize no longer says "AI".
- Docs corrected to match the code: THIRD-PARTY-NOTICES says MIT (not proprietary), SECURITY.md no longer claims a signed updater asset, the Help window and README describe the real behaviour (drop moves, sandbox is `%UserProfile%\RacksWorkspace`, double-click-to-hide is opt-in).
- Removed the unused `Microsoft.CodeAnalysis` and `Microsoft.Windows.CsWinRT` packages. One `<Version>` in the csproj.

## [1.1.4]

See the [GitHub releases page](https://github.com/duartelcunha/Racks/releases) for earlier versions.
