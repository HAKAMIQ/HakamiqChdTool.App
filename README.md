# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 desktop app for converting supported game disc images to CHD through a guided WPF interface.

It checks the source, prepares the output path, runs `chdman`, tracks progress, and keeps failures readable. No manual command-line work needed for normal use.

## Download

Latest release:

<https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest>

Choose one package:

| Package | Best for |
| --- | --- |
| `HakamiqChdTool-*-win-x64-self-contained.zip` | Most users. No separate .NET install. |
| `HakamiqChdTool-*-win-x64-runtime-required.zip` | Smaller download. Requires .NET 8 Desktop Runtime x64. |

## Requirements

- Windows x64
- .NET 8 Desktop Runtime x64 only if you choose the runtime-required package
- Enough free disk space for the source, temporary work, and final CHD

## Quick start

1. Download a ZIP from the latest release.
2. Extract it to a normal folder, not `Program Files`.
3. Run `HakamiqChdTool.exe`.
4. Add a file or folder.
5. Pick an output folder and start the queue.

Start with a single file — easier to verify the output first.

## Common uses

### Convert ISO to CHD

Add an `.iso`, choose the output folder, then start the queue. The original file stays untouched.

### Convert BIN/CUE or GDI

Add the descriptor file: `.cue` or `.gdi`. Keep the referenced track files beside it.

### Process archives

Add a supported archive and the app stages the image inside before conversion. If the archive is damaged or unclear, it stops early.

### Prepare CSO input

CSO is handled as an input step. The app prepares a temporary ISO, then sends that through the normal CHD workflow.

### Verify a CHD

Use verification before archiving a CHD or moving it to long-term storage. Quick confidence check.

### Extract a CHD

Extraction is available where the CHD type and selected profile are supported.

### Run a queue

Add multiple items, review the list, then start. Failed and cancelled items stay separate, which makes cleanup less annoying.

### Check bundled tools

Open Options, then External Tools. If something is missing, the app should tell you what it needs.

## Legal

The tool does not include games, ROMs, BIOS files, disc images, or Redump databases. Use it only with content you own or are legally allowed to process.

## Limitations

PS3-related handling is experimental and conservative. The app is not an emulator and not a ROM downloader. Android, web, cloud conversion, and C++ rewrites are outside the current direction.

## More documentation

Detailed format notes, conversion behavior, logs, helper tools, and developer documentation live in [`docs/`](docs/README.md).
