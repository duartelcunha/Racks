# Changelog

All notable changes to Racks are listed here. Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Fixed
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
