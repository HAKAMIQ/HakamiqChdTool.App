# Contributing

Hakamiq CHD Tool is a Windows x64 WPF app built with C# and .NET 8.

Keep changes small. A one-line path change can affect conversion, extraction, queue state, release output, or cleanup.

## Requirements

- Windows
- PowerShell 5.1 or later
- .NET SDK version from `global.json`
- Git
- GitHub CLI if you work on releases or CI checks

## Build

Use Debug while editing:

```powershell
dotnet restore .\HakamiqChdTool.App.csproj
dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore
```

Do not publish from Debug output.

## Local verification

Run this before trusting a change:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1
```

This checks repository conventions, package cleanliness, Debug build, Release build, and the validation test set. It also prints the manual smoke checklist.

## Validation tests

The app has a lightweight validation test project:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-Ps2AdvisoryValidationTests.ps1
```

These tests cover PS2 advisory behavior, CUE/BIN safety, output path contracts, and workflow planning. If you add tests and the count does not change, clean the test `bin` and `obj` folders and rebuild.

## Release output gate

Before uploading a public ZIP, run:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReleaseOutputGate.ps1
```

This publishes a disposable release output, verifies the end-user package, checks package cleanliness, then removes the disposable folder. Good release scripts should leave the repo clean.

## Creating an end-user release folder

Use the release script, not `bin` or a hand-made folder:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-EndUserRelease.ps1 -Output .\Release\v1.0.8
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-PackageCleanlinessGate.ps1 -ReleaseOutput .\Release\v1.0.8
```

Then create the ZIP from that folder and verify SHA256. Do not include source, scripts, test output, or build folders in the release asset.

## Manual smoke test

After a Release build, launch the app and check:

- Main window opens.
- Options window opens.
- About window opens.
- Light, Dark, and Hakamiq themes load.
- Arabic/English switching works after restart.
- A small file can be added.
- Verify and Extract paths still show clear results.
- No XAML parse errors or resource lookup failures appear.

Use `docs/SMOKE_TEST_CHECKLIST.md` when preparing a public release.

## Before commit

Run:

```powershell
git status --short
git diff --check
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1
```

For release-facing changes, also run:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReleaseOutputGate.ps1
```

Keep commits focused. Do not mix documentation rewrite, behavior change, and release packaging in one commit.

## Architecture rules

Do not add features just because an external reference mentions them.

Do not add top-level media input kinds for internal PSX/PS2 asset files such as `SYSTEM.CNF`, TIM, STR, VAG, MCR, MCD, GME, or PPF. Specialized scanners may inspect those files inside supported disc images, but they are not user-facing source formats.

Do not change queue behavior, cancel behavior, retry behavior, or conversion defaults without a dedicated stage and tests.

`Core/Workflow` is currently treated as an application workflow layer, not pure domain core. See `docs/architecture/ARCHITECTURE_BOUNDARIES.md`.

## GitHub Actions

Main branch pushes run CI.

```powershell
gh run list --branch main --limit 5
```

If CI fails, fix it before making a release.

## Screenshots and examples

Use neutral examples:

- `D:\CHDWork\Sample.iso`
- `D:\CHDOut\Sample.chd`

Do not show private paths, desktop clutter, copyrighted media names, or real game dumps.

## Files not allowed

Do not add games, ROMs, BIOS files, ISO files, CHD files, Redump files, keys, firmware, private logs, or copyrighted screenshots.

If a release includes third-party tools, keep the matching license and notice files with the package.
