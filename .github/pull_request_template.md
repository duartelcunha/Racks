## What and why

<!-- One or two sentences. Link the issue if there is one. -->

## Refactor / hardening notes (skip if not applicable)

- What was refactored:
- Risk reduced:
- Tests added:
- Local checks run (build, tests, format, publish):

## Checklist

- [ ] `dotnet build Racks.sln -c Release` passes
- [ ] `dotnet test Racks.sln -c Release` passes (the test project exists: add or update tests for what you changed)
- [ ] I ran the app: tray icon appears, a rack opens, drag and drop works, Exit is clean
- [ ] User-facing text goes through `Lang` resources, not hardcoded strings
- [ ] `CHANGELOG.md` has a line for this change
