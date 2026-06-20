# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 app for CHD files.

It uses chdman to convert supported disc image files to CHD, check
existing CHD files, and extract supported CHD files.

No command line is needed for normal use.

## Download

Latest release:

https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest

Choose one package:

- `HakamiqChdTool-*-win-x64-self-contained.zip`
  Best for most users. No separate .NET install is needed.

- `HakamiqChdTool-*-win-x64-runtime-required.zip`
  Smaller download. Needs .NET 8 Desktop Runtime x64.

## Requirements

- Windows x64
- Enough free disk space for temporary files and the final CHD
- .NET 8 Desktop Runtime x64 if you use the smaller package

## Quick start

1. Download a ZIP from the latest release.
2. Extract it to a normal folder.
3. Run `HakamiqChdTool.exe`.
4. Add a file or folder.
5. Choose an output folder.
6. Start the queue.

Start with one small file first. Check the result before running a large
queue.

## Common tasks

### Convert ISO to CHD

Add an ISO file, choose the output folder, then start the queue.

The original file is not changed.

### Convert CUE or GDI

Add the `.cue` or `.gdi` file.

Keep the track files beside it.

### Open archives

The app can open supported ZIP, RAR, and 7Z files before conversion.

If the archive is damaged, password-protected, or unclear, the app
should stop before conversion.

### Prepare CSO files

CSO files are handled before CHD conversion.

The app prepares a temporary ISO, then chdman creates the CHD.

### Check a CHD

Use Verify before deleting the original source file or moving a CHD into
your library.

### Extract a CHD

Extraction is available only for supported CHD types.

## Legal

Hakamiq CHD Tool does not include games, ROMs, BIOS files, disc images,
CHD files, Redump files, keys, firmware, or private user files.

Use it only with files you own or are legally allowed to process.

## More docs

- [Documentation index](docs/README.md)
- [Supported formats](docs/SUPPORTED_FORMATS.md)
- [Conversion options](docs/CONVERSION_OPTIONS.md)
- [Errors and logs](docs/ERRORS_AND_LOGS.md)
- [Legal notice](docs/legal/LEGAL.md)
