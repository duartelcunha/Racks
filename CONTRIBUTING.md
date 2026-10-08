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
(it-IT, cs-CZ, pl-PL, ko-KR, es-ES, zh-CN). To improve a language, edit its `.resx` file and open a PR.
A missing key falls back to English.

Some interface text is still hardcoded in English (for example parts of the tray menu). A cleanup of
this is planned; see issue #3. If you want to help before that lands, a PR that moves strings into
`Lang.resx` is welcome. Until the resource class is generated at build time, new keys must also be
added by hand to `Racks/Properties/Lang.Designer.cs`.

## Security

See `SECURITY.md` and `docs/SECURITY-INVARIANTS.md` before touching file moves, deletes, imports or the updater.
