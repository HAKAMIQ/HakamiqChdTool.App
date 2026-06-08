# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 WPF desktop app for CHD workflows built around MAME `chdman`.

It supports queue-based conversion, verification, extraction, archive intake, and optional local Redump-assisted validation for users managing their own legally obtained disc images.

## Release

The public release is one unified build with no activation, license keys, store gates, or paid feature restrictions.

Available packages:

* `self-contained`: ready to run.
* `runtime-required`: smaller package; requires .NET 8 Desktop Runtime.

## Features

* ISO / CUE / GDI to CHD workflows
* CHD verification with `chdman verify`
* CHD extraction where supported
* ZIP / RAR / 7Z intake where supported
* Queue processing with cancellation and per-item status
* Optional local Redump-assisted validation and naming
* Embedded `chdman` with optional external `chdman.exe`
* Arabic / English UI with RTL / LTR support
* Light, Dark, and HAKAMIQ themes
* Serilog file logging

## Requirements

* Windows 10 1809 or later; Windows 11 recommended
* Windows x64
* .NET 8 Desktop Runtime for the `runtime-required` package only

## Build

```powershell
dotnet restore .\HakamiqChdTool.App.csproj -r win-x64
dotnet build .\HakamiqChdTool.App.csproj -c Debug --no-restore
dotnet build .\HakamiqChdTool.App.csproj -c Release -r win-x64 --no-restore
```

## Publish

```powershell
.\scripts\Publish-EndUserRelease.ps1
.\scripts\Verify-Local.ps1
```

Smoke test checklist:

```text
docs/SMOKE_TEST_CHECKLIST.md
```

## Legal

This project does not include games, ROMs, BIOS files, copyrighted disc images, decryption keys, firmware, Disc Keys, or Redump databases.

Users are responsible for processing only files they are legally allowed to use.

Hakamiq CHD Tool uses `chdman` from the MAME project and is not affiliated with, sponsored by, or endorsed by MAMEdev.

See:

```text
docs/legal/CHDMAN_NOTICE.md
docs/legal/THIRD_PARTY_NOTICES.txt
```

## License

Original Hakamiq CHD Tool source code is licensed under the MIT License. See `LICENSE`.

Third-party components are distributed under their own licenses.

The HAKAMIQ name, logo, and official branding are not licensed under the MIT License. Official releases are distributed only through the HAKAMIQ GitHub repository.
