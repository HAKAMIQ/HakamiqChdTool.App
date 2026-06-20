# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 desktop app for working with CHD files.

It uses bundled chdman tooling to convert supported disc image files to CHD,
verify existing CHD files, and extract supported CHD files.

No command-line work is needed for normal use.

## Download

Latest release:

https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest

Choose one package:

- HakamiqChdTool-*-win-x64-self-contained.zip  
  Best for most users. No separate .NET install is needed.

- HakamiqChdTool-*-win-x64-runtime-required.zip  
  Smaller download. Requires .NET 8 Desktop Runtime x64.

## Requirements

- Windows x64
- Enough free disk space for temporary files and the final output
- .NET 8 Desktop Runtime x64 if you use the runtime-required package

## Quick start

1. Download a ZIP from the latest release.
2. Extract it to a normal folder.
3. Run HakamiqChdTool.exe.
4. Add a file or folder.
5. Choose an output folder.
6. Start the queue.

Start with one small file first. Check the result before running a large queue.

## Supported input

The app can convert supported files to CHD from:

- ISO
- CUE/BIN
- GDI
- TOC
- NRG
- CSO

The app can also open supported archives before processing:

- ZIP
- RAR
- 7Z

CSO files are prepared first, then converted through the normal CHD path.

Standalone BIN files are accepted only when the app can safely identify and
prepare them. If you have a CUE file, add the CUE file instead of the BIN file.

## CHD actions

Existing CHD files can be:

- verified
- extracted when the CHD type is supported
- processed through supported app actions

Extraction depends on the CHD type.

Common outputs are:

- CD or GD-ROM CHD to CUE/BIN
- DVD CHD to ISO
- hard disk CHD to IMG

## Common tasks

### Convert ISO to CHD

Add an ISO file, choose the output folder, then start the queue.

The original file is not changed.

### Convert CUE/BIN or GDI

Add the CUE or GDI file.

Keep the referenced track files beside it.

### Convert CSO to CHD

Add the CSO file.

The app prepares a temporary ISO, then creates the CHD from that prepared file.

### Open archives

Add a ZIP, RAR, or 7Z file.

The app opens the archive, looks for a supported file, and processes it if the
contents are valid.

### Verify a CHD

Use Verify before deleting the original source file or moving a CHD into
long-term storage.

### Extract a CHD

Extraction is available only for supported CHD types.

## Legal

Hakamiq CHD Tool does not include games, ROMs, BIOS files, ISO files, CHD
files, Redump files, keys, firmware, or private user files.

Use it only with files you own or are legally allowed to process.

## More docs

- [Documentation index](docs/README.md)
- [Supported formats](docs/SUPPORTED_FORMATS.md)
- [Conversion options](docs/CONVERSION_OPTIONS.md)
- [Errors and logs](docs/ERRORS_AND_LOGS.md)
- [Legal notice](docs/legal/LEGAL.md)
