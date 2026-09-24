# Contributing

CHD Tool is a Windows x64 WPF application built with C# and .NET 10.

Keep changes focused and small. Changes to paths, queue behavior, conversion, extraction, verification, or cleanup can affect multiple workflows.

## Requirements

- Windows
- PowerShell 5.1 or later
- .NET SDK version defined by `global.json`
- Git
- GitHub CLI when working with CI or release workflows

## Build

Use Debug while developing:

```powershell
dotnet restore .\HakamiqChdTool.App.csproj
dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore
```

Do not treat Debug output as an end-user release.

## Local verification

Before submitting a change, run:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1
```

This performs the normal local repository, build, and validation checks.

For the validation test set directly:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Ps2AdvTests.ps1
```

## Release-facing changes

Changes that affect publishing, packaging, bundled tools, or release output should also pass:

```powershell
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\RelOutGate.ps1
```

Do not manually assemble public releases from `bin`, `obj`, or arbitrary project folders.

## Before commit

Check the working tree and patch quality:

```powershell
git status --short
git diff --check
PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1
```

Keep commits focused. Avoid mixing unrelated documentation, behavior, architecture, and release changes.

## Architecture rules

Do not change queue, cancel, retry, or conversion-default behavior as part of unrelated cleanup.

`Core/Workflow` is currently an application workflow layer, not a pure domain core.

Do not add top-level media types for internal disc assets such as:

- `SYSTEM.CNF`
- TIM
- STR
- VAG
- MCR
- MCD
- GME
- PPF

Specialized scanners may inspect such files inside supported disc images, but they are not user-facing source formats.

See `docs/architecture/ARCH_BOUND.md` for current architecture boundaries.

## GitHub Actions

CI runs on changes to the main development path.

If CI fails, resolve the failure before preparing a release.

You can inspect recent runs with:

```powershell
gh run list --branch main --limit 5
```

## Screenshots and examples

Use neutral example paths such as:

- `D:\CHDWork\Sample.iso`
- `D:\CHDOut\Sample.chd`

Do not include private paths, personal information, copyrighted media names, or real game dumps.

## Files not allowed

Do not add:

- Games or ROMs
- BIOS files
- Disc images
- CHD files containing copyrighted content
- Redump data files
- Keys or firmware
- Private logs
- Copyrighted screenshots

Third-party tools included with the project must keep their required license and notice files.
