# Changelog

All notable changes to Racks are listed here. Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed
- Removed about 1,150 lines of dead code (an unused second file loader and thumbnail service, unused menu and drag helpers, leftover commented-out blocks). No behaviour change; `RackWindow.xaml.cs` went from 5,870 to 5,603 lines.
- Repository maintenance: cleaned contributor metadata from the git history.

### Fixed
- Sorting: clicking Name, Date modified, Date created or Type in a rack's sort menu never flipped between ascending and descending, and Size flipped from the wrong starting points. Clicking the active key now flips its direction; another key starts ascending.
- Magic Organize placed racks using screen pixels as if they were WPF units, so on a scaled display (125%, 150%...) the grid was off-centre. It now converts first.
- New racks move dropped files by default (Ctrl while dropping still makes a shortcut), matching the README and Help. The default used to say "link" while the tray menu created racks that moved.
- Quick Access pinning no longer depends on English and Portuguese menu captions. It pins once with the language-independent command and checks first whether the folder is already pinned.
- Comments and the Magic Organize group-name code no longer claim auto-route makes shortcuts; it moves files.
- `crash.log` no longer grows forever: at 1 MB it moves to `crash.log.1` (replacing an older one), so at most about 2 MB is kept. The uninstaller removes both.
- When Racks swallows an unexpected error to keep running, it now says so once per session in a notification and points to `crash.log`. Before, you never knew anything had gone wrong.
- A rack showing a folder no longer reloads over and over while a file in it is being downloaded or copied. Content changes are now reported once the file has been quiet for a moment; new, deleted and renamed files still show up immediately.
- If the folder watcher loses events (a burst of changes overflowed its buffer), the rack now rebuilds the watcher and rescans instead of silently going stale.
- "Check for updates" now reports a failure the first time too. It used to say nothing when GitHub could not be reached on the first click.
- Installing an update now closes Racks normally instead of killing it, so the single-instance lock is released and the new version starts cleanly.
- Settings > "Auto update" now does what it says: with it on, Racks checks GitHub for a new release shortly after startup and shows a toast if there is one. It used to add or remove Racks from Windows startup (that is the tray's "Start on login") and never checked for updates. It is off by default.
- The tray's "Lock all racks" switch now shows the real state: on when every rack is locked, refreshed each time the menu opens.
- If another app already owns Ctrl+Shift+Space or Ctrl+Shift+N, Racks now says so in a notification instead of the shortcut silently doing nothing.
- "Reset default style" no longer wipes unrelated settings. It used to delete every saved global value, which turned physics, hide-icons and start-on-login back to defaults and replayed the first-run welcome and one-time migrations. It now removes only the default rack style, and asks for confirmation first.
- Hovering a rack in Settings > Manage racks no longer saves its green highlight. A crash while hovering could leave the rack's border green for good.
- Racks lost their "transparent background" setting on restart. It is now saved and loaded.
- Renaming a rack (or choosing a new folder for a missing-folder rack) could drop settings (drop shadow, gradient, disabled animations, and the files a desktop rack owns), and picking a folder with the same name deleted the rack's saved settings entirely, so it vanished on restart. Both save paths now share one list of settings.

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
