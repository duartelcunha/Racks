# Racks: notes for coding agents

Windows desktop app (WPF, .NET 10). The risk is data loss in the filesystem, shell and registry, not the network.

**Resume here:** [`docs/HARDENING-ROADMAP.md`](docs/HARDENING-ROADMAP.md) has the current phase, the next step,
the commands and the acceptance criteria. Architecture: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
Security rules: [`docs/SECURITY-INVARIANTS.md`](docs/SECURITY-INVARIANTS.md) (keep it in sync with any change that
touches moves, deletes, imports, the registry or the updater).

## Rules

- Do not add features while hardening. One phase = one PR; say what was refactored, which risk went down and which tests were added.
- Destructive file operations go through the file gateway (`FileOperationBroker`, a facade over the internal `SafeMove`/`SafeDelete`; enforced by `FileMutationGuardTests`). No raw `File.Move`/`Directory.Delete` elsewhere.
- Presentation reaches the shell (change notifications, desktop icons, workspace folder) only through `ShellOperationGateway.Default` (enforced by `PresentationBoundaryTests`).
- Presentation reads and writes app-wide registry values (switches, one-shot markers) only through `AppSettings` / `MigrationMarkers` (`Racks.Infrastructure.Registry`; enforced by `RegistryAccessGuardTests`). Per-rack values and the global style defaults go through `RackSettingsRepository` (one `RackSettingsSchema` table; `Instance.Store` is the replaceable default, tests redirect `InstanceController.appName` with `RackRegistryFixture`).
- Never relax the update path: it is fail-closed (signed manifest, host/path allowlist, size, SHA-256).
- Persisted or imported values are validated before use. Regexes built from them go through `SafeRegex`.
- Every refactor ships regression tests. Tests that read source text are replaced by behavior tests *before* the code they watch moves.
- `Guards/baseline.json` ratchets only go down; update the baseline in the same PR that lowers a number.
- No secrets in the repo. Personal repo: GitHub via `gh.exe` as `duartelcunha`.

## Check before every PR (same as `.github/workflows/build.yml`, run locally)

```powershell
dotnet restore Racks.sln
dotnet format whitespace --verify-no-changes
dotnet build Racks.sln -c Release
git diff --exit-code -- Racks/Properties/Lang.Designer.cs
dotnet test Racks.sln -c Release --no-build
dotnet publish Racks/Racks.csproj -c Release -r win-x64 --self-contained false
gitleaks detect --no-banner
```

Phases that touch UI also run `test/Test-Racks.ps1` and `tools/dev-tools.ps1 e2e`.
