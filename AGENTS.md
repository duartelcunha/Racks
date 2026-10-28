# Racks: notes for coding agents

Windows desktop app (WPF, .NET 10). The risk is data loss in the filesystem, shell and registry, not the network.

**Start here:** [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) (layers, where new code goes, the guard tests) and
[`CONTRIBUTING.md`](CONTRIBUTING.md) (rules for changing it). What the app guards against: [`SECURITY.md`](SECURITY.md).
Releases: [`docs/RELEASING.md`](docs/RELEASING.md).

## Rules

- Destructive file operations go through the file gateway (`FileOperationBroker.Default`, a facade over the internal `SafeMove`/`SafeDelete`; `FileMutationGuardTests` counts raw `File`/`Directory` mutations against `Guards/fs-allowlist.json`, Racks-owned state or the user-chosen export target). No raw `File.Move`/`Directory.Delete` elsewhere, and a new user-data operation is a new broker method with an audit record.
- Presentation reaches the shell (change notifications, desktop icons, workspace folder) only through `ShellOperationGateway.Default` (enforced by `PresentationBoundaryTests`).
- Presentation reads and writes app-wide registry values (switches, one-shot markers) only through `AppSettings` / `MigrationMarkers` (`Racks.Infrastructure.Registry`; enforced by `RegistryAccessGuardTests`). Per-rack values and the global style defaults go through `RackSettingsRepository` (one `RackSettingsSchema` table; `Instance.Store` is the replaceable default, tests redirect `InstanceController.appName` with `RackRegistryFixture`).
- Never relax the update path: it is fail-closed (signed manifest, host/path allowlist, size, SHA-256). A change there needs the behavior tests in `test/Racks.Tests/Util/Updater*` and `UpdateServiceTests` green.
- Persisted or imported values are validated before use. Regexes built from them go through `SafeRegex`.
- Every refactor ships regression tests. Tests are behavior tests (temp folders, a throwaway registry root, fakes); a test that reads source text is only a guard. Source-reading tests are replaced by behavior tests *before* the code they watch moves.
- Ratchets only go down: `Guards/baseline.json`, `fs-allowlist.json` and `registry-presentation-allowlist.json`; update the number in the same PR that lowers it, never raise one. The guards cannot see Win32/COM, calls through variables or `tools/`: review those by hand.
- No new feature or risk fix without a test that fails first.
- No secrets in the repo. Personal repo: GitHub via `gh.exe` as `duartelcunha`.

## Check before every PR (run locally; the GitHub workflows are manual only)

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
