# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows desktop application for CHD workflows built around MAME `chdman`.

The project is designed for users who manage their own legally obtained disc images and want a queue-based desktop interface for conversion, verification, archive intake, and CHD extraction.

## Status

Current public release: **Windows x64 WPF desktop release**.

The latest release is available from GitHub Releases in two packages:

* `self-contained`: complete package for direct use.
* `framework-dependent`: smaller package, requires .NET 8 Desktop Runtime before use.

## Features

* ISO / CUE / GDI to CHD workflows
* CHD verification through `chdman verify`
* CHD extraction / back-conversion workflows where supported
* ZIP / RAR / 7Z intake where supported by the extraction layer
* Queue-based processing with cancellation and per-item status
* Optional locally configured Redump-assisted validation and naming flows
* Embedded `chdman` runtime with optional external `chdman.exe` selection
* Light, Dark, and HAKAMIQ themes
* Arabic and English interface text with RTL/LTR support
* Serilog file logging

## Platform support

Hakamiq CHD Tool is currently a WPF desktop application. WPF is Windows-only, so the official release artifacts target Windows x64.

Windows x86, native Windows ARM64, macOS, and Linux are not supported by this WPF release.

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

Before publishing a binary release, run the official publish gate on a real Windows machine:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

Default output:

```text
Release\
```

For development-only verification:

```powershell
.\scripts\Verify-Local.ps1
```

Manual runtime checks are listed in:

* `docs/SMOKE_TEST_CHECKLIST.md`

## Storage format policy

Hakamiq CHD Tool focuses on CHD workflows for supported disc-image conversion, verification, and extraction through `chdman`.

By default, the app does not delete source files automatically. Optional delete-source settings exist for verified conversion/extraction workflows and must remain disabled unless the user explicitly enables them. Test every output before removing originals manually.

Detailed release guidance is maintained in `docs/RELEASE-SAFETY.md`.

## Legal use

This project does **not** include games, ROMs, BIOS images, copyrighted disc images, user media, decryption keys, platform firmware, or Redump databases.

Users are responsible for ensuring they have the legal right to process their own files.

Do not use this project to distribute copyrighted games, BIOS files, ROM sets, unauthorized disc images, decryption keys, platform firmware, or copyrighted user media.

## chdman / MAME notice

Hakamiq CHD Tool uses `chdman` from the MAME project.

In the official release package, `chdman` is embedded in the application and prepared automatically at runtime. Users can also select an external `chdman.exe` manually from the app settings if needed.

See:

* `CHDMAN_NOTICE.md`
* `MAME_COPYING.txt`
* `MAME_GPL-2.0.txt`
* `THIRD_PARTY_NOTICES.txt`

Hakamiq CHD Tool is not affiliated with, sponsored by, or endorsed by MAMEdev. MAME is a trademark of its respective owner.

MAME/chdman are provided without warranty under their upstream license terms.

## License

Original Hakamiq CHD Tool source code in this repository is licensed under the MIT License. See [`LICENSE`](LICENSE).

Third-party components are distributed under their respective licenses. See [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt).
