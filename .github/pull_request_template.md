## What and why

<!-- One or two sentences. Link the issue if there is one. -->

## Refactor notes (skip if not applicable)

- What was refactored:
- Risk reduced:
- Tests added:
- Local checks run (build, tests, format, publish):

## Checklist

- [ ] `dotnet build Racks.sln -c Release` passes
- [ ] `dotnet test Racks.sln -c Release` passes (add or update behavior tests for what you changed)
- [ ] `dotnet format whitespace --verify-no-changes` is clean and the build has 0 warnings
- [ ] A guard or ratchet number went down, or stayed the same (never up); if a rule or folder changed, `docs/ARCHITECTURE.md` is updated
- [ ] I ran the app: tray icon appears, a rack opens, drag and drop works, Exit is clean
- [ ] User-facing text goes through `Lang` resources, not hardcoded strings
- [ ] `CHANGELOG.md` has a line for this change
