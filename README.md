# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 desktop application for converting supported
disc images to CHD, verifying existing CHD files, and extracting supported CHD
media without requiring manual `chdman` commands.

Add a file or folder, choose an output location, and start the queue. The
application handles input validation, naming, progress tracking, and output
verification.

## Download

Latest release:

https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest

Windows x64 runtime-required package:

- `HakamiqChdTool-vX.Y.Z-win-x64-runtime-required.zip`
- Requires .NET 10 Desktop Runtime x64.

## Quick start

1. Download the latest Windows x64 release.
2. Extract the ZIP to a normal folder.
3. Run `HakamiqChdTool.exe`.
4. Add a supported file or folder.
5. Choose the output folder.
6. Start the queue.

For the first run, processing a single disc image makes the resulting output
easier to verify before starting a larger batch.

## Supported workflows

Hakamiq CHD Tool supports common disc-image workflows including:

- ISO
- CUE/BIN
- GDI
- CSO
- CHD
- supported archive inputs

Support depends on the media type and selected operation. Input detection does
not imply that every conversion or extraction path is valid.

The application validates the selected workflow before external processing
begins.

## Safety

Input is checked before processing for conditions such as:

- unreadable or incomplete source files
- invalid descriptor references
- unsafe CUE paths
- invalid archive input
- unsupported disc layouts
- invalid output paths

Invalid or unsupported input is rejected before conversion whenever possible.

## CHD extraction

Supported extraction depends on the CHD media type:

- CD and GD-ROM CHD can extract to CUE/BIN.
- DVD CHD can extract to ISO.
- Hard disk CHD can extract to IMG.

## Limitations

- Windows x64 only.
- The bundled MAME 0.289 tool requires an x86-64-v2 capable processor.
- Not every disc layout is supported.
- CHD operations depend on `chdman` capabilities.

## Security

Dependency versions are locked and bundled external-tool hashes are pinned.
Release output includes a CycloneDX 1.7 SBOM.

See `SECURITY.md` for vulnerability reporting.

## Legal

Hakamiq CHD Tool does not include games, ROMs, BIOS files, disc images,
Redump databases, keys, firmware, or copyrighted user content.

Use the application only with files you own or are legally authorized to
process.

## Documentation

See `docs/` for supported formats, conversion options, logs, and
troubleshooting.

## License

Licensed under the MIT License. See `LICENSE`.