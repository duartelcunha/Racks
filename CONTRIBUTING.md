# Contributing to Racks

Thanks for helping. Racks is a small Windows app (C#, WPF, .NET 10), so the bar is simple: keep it working, keep it light.

## Build and run

Requires the **.NET 10 SDK** on Windows.

```powershell
dotnet build Racks/Racks.csproj -c Debug
dotnet run --project Racks/Racks.csproj
```

## Pull requests

- Keep each PR small and about one thing.
- Run the app once before opening the PR: tray icon appears, a rack opens, drag and drop works, Exit is clean.
- Add a line to `CHANGELOG.md` under `Unreleased`.
- Do not add user-facing text as a hardcoded string. Put it in `Racks/Properties/Lang.resx` and reference it as `Lang.Key`.

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

See `SECURITY.md` and `docs/SECURITY-INVARIANTS.md` before touching file moves, deletes, imports or the updater.
