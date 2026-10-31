# C_Optimizer

Disk cleanup tool for Windows 10 (1809+) and Windows 11. Finds caches, temporary files, update leftovers,
GPU shader caches and other regenerated data on the system drive, and removes them safely.

Design and roadmap: [docs/DESIGN.md](docs/DESIGN.md).

## Status

- Stage 1 (core engine): rule engine, scanner, safety policy, clean engine, built-in rules, CLI.
- Stage 2 (desktop UI): WPF app with Home (disk usage, one-click safe cleanup) and Cleanup (review by category) pages.

- Stage 3 (system cleanup): official Windows cleanup handlers from an allow-list (Windows.old, component store,
  Delivery Optimization, upgrade leftovers, memory dumps, old drivers, ...), Recycle Bin, Windows Update download
  cache with services stopped during cleaning.

## Build and test

Requires the .NET 10 SDK.

```
dotnet build COptimizer.slnx
dotnet test
```

Set `COPTIMIZER_SHELL_TESTS=1` to also run the test that moves a temporary file into the real Recycle Bin.

## Desktop app

```
dotnet run --project src/COptimizer.App
```

Debug builds run with the caller's rights; use "Run as administrator" in the app to include system folders.

Portable single-file build (requests administrator rights on start):

```
dotnet publish src/COptimizer.App -c Release -r win-x64 -o publish/win-x64
```

Every cleanup writes a log of removed and skipped files to `%LOCALAPPDATA%\COptimizer\Logs`.

## CLI

```
dotnet run --project src/COptimizer.Cli -- rules
dotnet run --project src/COptimizer.Cli -- scan --level moderate --verbose
dotnet run --project src/COptimizer.Cli -- clean --level safe --dry-run
dotnet run --project src/COptimizer.Cli -- clean --rule gpu.nvidia.shader-cache --yes
dotnet run --project src/COptimizer.Cli -- system                      # elevated: scan Windows cleanup handlers
dotnet run --project src/COptimizer.Cli -- system --rule windows.delivery-optimization --yes
```

Run from an elevated terminal to include system folders and other users' profiles.

## Layout

| Project | Purpose |
|---|---|
| `src/COptimizer.Core` | Models, rule loader, safety policy, scanner, clean engine (no Windows-only APIs) |
| `src/COptimizer.Rules` | Built-in rule sets (`Definitions/*.json`, embedded) |
| `src/COptimizer.Windows` | Windows integration: system paths, user profiles from the registry, Recycle Bin |
| `src/COptimizer.App` | WPF desktop app (WPF-UI, CommunityToolkit.Mvvm) |
| `src/COptimizer.Cli` | Command-line front end |
| `tests/COptimizer.Core.Tests` | Unit tests on a fake machine in a temp folder, plus Windows integration tests |
| `tests/COptimizer.App.Tests` | View model tests with fake services |
