# Contributing to Racks

Thanks for helping. Racks is a small Windows app (C#, WPF, .NET 10), so the bar is simple: keep it working, keep it light.

## Build and run

Requires the **.NET 10 SDK** on Windows.

```powershell
dotnet build Racks/Racks.csproj -c Debug
dotnet run --project Racks/Racks.csproj
```

## Before you open a pull request

The checks run locally, not on GitHub. The workflows in `.github/workflows/` are manual only. Run the same list as `.github/workflows/build.yml`:

```powershell
dotnet restore Racks.sln
dotnet format whitespace --verify-no-changes
dotnet build Racks.sln -c Release                  # 0 warnings
git diff --exit-code -- Racks/Properties/Lang.Designer.cs
dotnet test Racks.sln -c Release --no-build        # all green; a few tests skip when the machine lacks a 2nd drive or symlink rights
dotnet publish Racks/Racks.csproj -c Release -r win-x64 --self-contained false
gitleaks detect --no-banner
```

If you touched windows, the tray or startup, also run the app once: tray icon appears, a rack opens, drag and drop works, Exit is clean
(`test/Test-Racks.ps1` and `tools/dev-tools.ps1 e2e` automate this, but they close any running Racks and use the real Desktop).

## Pull requests

- Keep each PR small and about one thing.
- Add a line to `CHANGELOG.md` under `Unreleased`, in plain language, under Security, Changed or Fixed.
- Do not add user-facing text as a hardcoded string. Put it in `Racks/Properties/Lang.resx` and reference it as `Lang.Key`.
- The PR description (`.github/pull_request_template.md`) has these sections: **What and why**; **Refactor notes**
  (what was refactored, risk reduced, tests added, local checks run with their result); the checklist. Fill them in; "n/a" is fine
  for a section that does not apply.

## Rules for code that touches files, the registry, the shell or updates

The layer map and the guard tests are described in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). Short version:

- **Files.** Moving, renaming, deleting, recycling or creating a folder for something the user can see goes through
  `FileOperationBroker.Default` (with a reason string). No raw `File.Move`, `Directory.Delete` and friends, and no `SafeMove`/`SafeDelete`
  outside `Racks/Infrastructure/FileSystem/`. `FileMutationGuardTests` fails if you do.
- **Registry.** App-wide switches and one-shot markers go through `AppSettings` / `MigrationMarkers`; per-rack values go through
  `RackSettingsRepository`, which is driven by one table (`RackSettingsSchema`). Presentation code never opens the registry.
- **Shell.** Change notifications, desktop icons and the workspace folder go through `ShellOperationGateway.Default`
  (`PresentationBoundaryTests`).
- **Untrusted text** (imported layouts, saved values, typed names, regex): validate before use: `RackSettingsValidator`,
  `SafeWpfValues`, `PathPolicy.ValidateLeafName`, `SafeRegex`. A value that fails is dropped to its default and logged, never applied.
- **Updates.** The path is fail-closed (signed manifest, URL allowlist, size, SHA-256). Do not weaken it; a change there needs the behavior tests in `test/Racks.Tests/Util/Updater*` and `UpdateServiceTests` green.
- **Guards and ratchets only go down.** `test/Racks.Tests/Guards/*.json` (file-mutation allowlist, registry allowlist, size baseline)
  record exact numbers. If your change lowers one, lower it in the same PR. Do not raise one to make a test pass: move the code instead.
  A new `[DllImport]`, COM call or `Process` is not seen by the guards and needs a careful review.
- **Tests are behavior tests.** Test what the code does (against temp folders, a throwaway registry root, fakes for time and the shell),
  not what the source text says. A test that greps `.cs` files is only acceptable as a guard. When you move code, first replace any
  source-reading test of it with a behavior test. Never write to the real `HKCU\SOFTWARE\Racks`, the real Desktop or the real Documents.
- **Docs follow the code.** If you change a rule or a folder, update `docs/ARCHITECTURE.md` in the same PR.

## Translations

Strings live in `Racks/Properties/Lang.resx` (English) and one `Lang.<culture>.resx` per language
(it-IT, cs-CZ, pl-PL, ko-KR, es-ES, zh-CN). A key missing from a language falls back to English.

**Improve a language:** edit its `Lang.<culture>.resx` and open a PR. No C# needed.

**Add a new string:**
1. Add a `<data name="Area.Thing">` entry to `Lang.resx` (English).
2. Run `dotnet build`. `Racks/Properties/Lang.Designer.cs` is regenerated and `Lang.Area_Thing` now exists
   (dots become underscores).
3. Use it from XAML as `{x:Static prop:Lang.Area_Thing}` or from C# as `Lang.Area_Thing`.
4. Commit `Lang.resx` and `Lang.Designer.cs` together. CI fails if the Designer file is out of date.

**Add a new language:** copy `Lang.resx` to `Lang.<culture>.resx` (for example `Lang.ja-JP.resx`),
translate the values, keep the `name` attributes unchanged, and build. `dotnet test` checks that every
key in a translation exists in `Lang.resx`, and lists the keys each language is still missing.

**Preview a language** without changing Windows: set `RACKS_LANG` before starting Racks, for example
`$env:RACKS_LANG = "zh-CN"; dotnet run --project Racks/Racks.csproj`.

Some interface text is still hardcoded in English. Moving it into `Lang.resx` is tracked in issue #3,
and PRs that do it are welcome.

## Security

Read `SECURITY.md` before touching file moves, deletes, imports, the registry or the updater.
Report vulnerabilities privately (see `SECURITY.md`), not in a public issue.
