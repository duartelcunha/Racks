# Changelog

All notable changes to Racks are listed here. Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- `test/Racks.Tests` (xUnit) with first tests for `SafeRegex`, the updater version compare and `RackMirror.Sanitize`.
- Ratchet tests that stop `RackWindow.xaml.cs`, `MainWindow._controller` usage and hardcoded XAML text from growing.

- CI: format check, tests, test-result and portable-build artifacts. New `release.yml` builds the installer, portable zip and `SHA256SUMS.txt` when a `v*` tag is pushed.

- `dotnet build` regenerates `Lang.Designer.cs` from `Lang.resx`, so new strings no longer need Visual Studio. Localization tests check translation keys and satellite loading.

### Changed
- Removed unused `Microsoft.CodeAnalysis` and `Microsoft.Windows.CsWinRT`; a single `<Version>` in the csproj.
- Repository hygiene: `.editorconfig`, `Directory.Build.props`, `global.json`, Dependabot, PR template.

## [1.1.4]

See the [GitHub releases page](https://github.com/duartelcunha/Racks/releases) for earlier versions.
