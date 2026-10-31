# C_Optimizer

Disk cleanup tool for Windows 10 (1809+) and Windows 11. Finds caches, temporary files, update leftovers,
GPU shader caches and other regenerated data on the system drive, and removes them safely.

Design and roadmap: [docs/DESIGN.md](docs/DESIGN.md).

## Status

Stage 1 (core engine) is done: rule engine, scanner, safety policy, clean engine, built-in rules and a CLI.
The desktop UI comes in stage 2.

## Build and test

Requires the .NET 10 SDK.

```
dotnet build COptimizer.slnx
dotnet test
```

Set `COPTIMIZER_SHELL_TESTS=1` to also run the test that moves a temporary file into the real Recycle Bin.

## CLI

```
dotnet run --project src/COptimizer.Cli -- rules
dotnet run --project src/COptimizer.Cli -- scan --level moderate --verbose
dotnet run --project src/COptimizer.Cli -- clean --level safe --dry-run
dotnet run --project src/COptimizer.Cli -- clean --rule gpu.nvidia.shader-cache --yes
```

Run from an elevated terminal to include system folders and other users' profiles.

## Layout

| Project | Purpose |
|---|---|
| `src/COptimizer.Core` | Models, rule loader, safety policy, scanner, clean engine (no Windows-only APIs) |
| `src/COptimizer.Rules` | Built-in rule sets (`Definitions/*.json`, embedded) |
| `src/COptimizer.Windows` | Windows integration: system paths, user profiles from the registry, Recycle Bin |
| `src/COptimizer.Cli` | Command-line front end |
| `tests/COptimizer.Core.Tests` | Unit tests on a fake machine in a temp folder, plus Windows integration tests |
