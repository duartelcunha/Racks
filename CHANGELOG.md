# Changelog

All notable changes to Racks are listed here. Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

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
