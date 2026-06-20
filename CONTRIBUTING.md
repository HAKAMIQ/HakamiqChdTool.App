# Contributing

Hakamiq CHD Tool is a Windows x64 WPF app built with .NET 8.

Keep changes small and easy to review. A small UI change can still affect
conversion, cancel, package output, or app startup.

## Requirements

- Windows
- PowerShell 5.1 or later
- .NET SDK version from global.json
- GitHub CLI for CI and release checks

## Build

Use Debug while editing:

- `dotnet restore .\HakamiqChdTool.App.csproj`
- `dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore`

Do not publish from Debug output.

## Local check

Run this before trusting a change:

- `PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1`

This restores packages, builds Debug, builds Release, and prints the
manual smoke checklist.

There is no test project right now. Do not add fake test commands.

## Release check

Before uploading a public ZIP, run:

- `PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-EndUserRelease.ps1`
- `PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-EndUserRelease.ps1`

Use the release scripts only. Do not publish from `bin`, `obj`, or a
hand-made folder.

## Manual smoke test

After a Release build, launch the app and check the main screens.

Use:

- `docs/SMOKE_TEST_CHECKLIST.md`

Check at least:

- Main window opens.
- Options window opens.
- About window opens.
- A small ISO can be added.
- A CHD can be checked.
- A running item can be cancelled.
- Failed and cancelled items look different.
- No chdman or 7-Zip process is left open after closing the app.

## Before commit

Run:

- `git status --short`
- `git --no-pager diff --check`
- `PowerShell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Local.ps1`

Also check for old unwanted text:

- `git --no-pager grep -n -i "maxcso\|NuGet.temp.config" -- . 2>$null`

No output is expected.

## GitHub Actions

Main branch pushes run CI.

Check recent runs with:

- `gh run list --branch main --limit 5`

If CI fails, fix it before making a release.

## Screenshots

Use clean screenshots only.

Do not show private paths, desktop clutter, copyrighted media names, or
real game dumps.

Neutral examples are better:

- `D:\CHDWork\Sample.iso`
- `D:\CHDOut\Sample.chd`

## Files not allowed

Do not add:

- games
- ROMs
- BIOS files
- ISO files
- CHD files
- Redump files
- keys
- firmware
- private logs
- copyrighted screenshots

If a release includes tools from other projects, keep their license
files with the package.
