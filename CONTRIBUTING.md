# Contributing

Hakamiq CHD Tool is a Windows x64 WPF app built on .NET 8.
Keep changes boring unless the feature truly needs more.

The app wraps local CHD workflows. Small UI changes can still break
conversion flow, release packaging, or cancellation behavior, so run
the gates before shipping.

## Requirements

- Windows
- PowerShell 5.1 or later
- .NET SDK compatible with `global.json`
- GitHub CLI if you need Actions or Release checks

## Build

Use Debug while editing:

```powershell
dotnet restore .\HakamiqChdTool.App.csproj
dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore
```

Public packages come from the release scripts, not from Debug output.

## Local gate

Run this before you trust a change:

```powershell
.\scripts\Verify-Local.ps1
```

This is the quick confidence pass. It restores, builds, and prints the
manual smoke checklist. There is no real test project right now, so do
not add fake `dotnet test` requirements.

## Release gate

Before uploading a public ZIP:

```powershell
.\scripts\Publish-EndUserRelease.ps1
.\scripts\Verify-EndUserRelease.ps1
```

The scripts publish the app, copy approved bundled tools and legal
notices, generate the manifest, then verify the release output.

Do not publish from `bin`, `obj`, or a hand-made folder.

## Manual smoke

After a Release build, launch the app from `Release` and click through
the important paths. Build passing is not enough for a desktop tool.

Check at least:

- Options opens.
- About opens.
- a small ISO can be added.
- a CHD can be verified.
- a running item can be cancelled.
- failed and cancelled items are reported differently.
- no orphan `chdman`, `7z`, or helper process remains.

Simple, but it catches real problems.

## Release assets

Use stable asset names:

```text
HakamiqChdTool-vX.Y.Z-win-x64-runtime-required.zip
HakamiqChdTool-vX.Y.Z-win-x64-self-contained.zip
```

The runtime-required package is smaller and needs .NET 8 Desktop
Runtime x64. The self-contained package is larger, but easier for most
users.

Never commit generated ZIP files, release folders, logs, source
packages, or local artifacts.

## Repository hygiene

Before commit:

```powershell
git status --short
git --no-pager diff --check
```

Check for old forbidden leftovers:

```powershell
git --no-pager grep -n -i "maxcso\|NuGet.temp.config" -- . 2>$null
```

No output is expected.

## GitHub Actions

Main pushes run CI:

```powershell
gh run list --branch main --limit 5
```

If CI fails, fix the workflow or the code before publishing a release.

Release notification runs only when a GitHub Release is published:

```powershell
gh run list --limit 10
```

## Screenshots

Use clean screenshots only. No private paths, copyrighted media names,
desktop clutter, or real game dumps.

Neutral examples are better:

```text
D:\CHDWork\Sample.iso
D:\CHDOut\Sample.chd
```

## Legal boundary

Do not add games, ROMs, BIOS files, ISO or CHD images, Redump
databases, keys, firmware, private logs, or copyrighted screenshots.

If a release bundles third-party tools, keep the matching legal notices
with the package.
