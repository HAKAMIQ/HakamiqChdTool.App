# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows desktop application for CHD workflows built around MAME `chdman`.

The project is designed for users who manage their own legally obtained disc images and want a queue-based desktop interface for conversion, verification, archive intake, and CHD extraction.

## Status

Current public release: **Windows x64 WPF desktop release**.

The latest release is available from GitHub Releases in two packages:

* `self-contained`: complete package for direct use.
* `framework-dependent`: smaller package, requires .NET 8 Desktop Runtime before use.

Before publishing a binary release, maintainers must run the official publish gate and smoke checklist on a real Windows machine:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

## Features

* ISO / CUE / GDI to CHD workflows
* CHD verification through `chdman verify`
* CHD extraction / back-conversion workflows where supported by the application operation catalog
* ZIP / RAR / 7Z intake where supported by the extraction layer
* Queue-based processing with cancellation and per-item status
* Optional locally configured Redump-assisted validation and naming flows
* Embedded `chdman.exe` with optional external `chdman.exe` selection
* Light, Dark, and HAKAMIQ themes
* Arabic and English interface text with RTL/LTR support
* Serilog file logging

## Platform support

Hakamiq CHD Tool is currently a WPF desktop application. WPF is Windows-only, so the official release artifacts from this repository target Windows x64.

Windows x86 and native Windows ARM64 artifacts are not claimed for this snapshot because the bundled `chdman.exe` is a Windows x64 executable. macOS and Linux are not supported by this WPF codebase. Cross-platform migration work is intentionally not included in this WPF repository snapshot.

## Storage format policy

Hakamiq CHD Tool focuses on CHD workflows for supported disc-image conversion, verification, and extraction through `chdman`.

By default, the app does not delete source files automatically. Optional delete-source settings exist for verified conversion/extraction workflows and must remain disabled unless the user explicitly enables them after understanding the risk. Test every output before removing originals manually.

Detailed release guidance is maintained in `docs/RELEASE-SAFETY.md`.

## Requirements

For release downloads:

* Windows 10 build 17763 (1809) or later; Windows 11 recommended.
* x64 Windows.
* `self-contained` package: includes the required runtime components.
* `framework-dependent` package: requires .NET 8 Desktop Runtime before running the application.

For development builds:

* .NET 8 SDK
* Windows desktop workload / WPF support
* Visual Studio 2022 or command-line `dotnet`

## Build

```powershell
dotnet restore .\HakamiqChdTool.App.csproj -r win-x64
dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore
dotnet build .\HakamiqChdTool.App.csproj -c Release -r win-x64 --no-restore
```

## Publish

The project file owns the Release publish policy. Scripts must not pass options that contradict the `.csproj`.

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

Default output:

```text
Release\
```

## Verification gate

Run before every public release:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

For development-only verification without producing the final `Release` folder:

```powershell
.\scripts\Verify-Local.ps1
```

Manual runtime checks are listed in:

* `docs/SMOKE_TEST_CHECKLIST.md`

## Legal use

This project does **not** include games, ROMs, BIOS images, copyrighted disc images, user media, decryption keys, platform firmware, or Redump databases.

Users are responsible for ensuring they have the legal right to process their own files.

Do not use this project to distribute copyrighted games, BIOS files, ROM sets, unauthorized disc images, decryption keys, platform firmware, or copyrighted user media.

## Legal notice

A project legal release notice is provided in [`LEGAL.md`](LEGAL.md). It covers the no-bundled-content policy, third-party tool notices, Redump integration scope, no-affiliation statements, and the safe-release rules used to gate this repository.

All third-party components, their authors, and their licenses are listed in [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt).

## chdman / MAME notice

This repository redistributes `chdman.exe` from the MAME project for user convenience.

See:

* `CHDMAN_NOTICE.md`
* `MAME_COPYING.txt`
* `MAME_GPL-2.0.txt`

Hakamiq CHD Tool is not affiliated with, sponsored by, or endorsed by MAMEdev. MAME is a trademark of its respective owner.

MAME/chdman are provided without warranty under their upstream license terms. Do not run Hakamiq CHD Tool as Administrator unless necessary; normal user execution is safer when processing files and archives from varied sources.

## License

Original Hakamiq CHD Tool source code in this repository is licensed under the MIT License. See [`LICENSE`](LICENSE).

Third-party components are distributed under their respective licenses. See [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt).
