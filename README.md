# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 app for converting supported game disc images to CHD without writing chdman commands by hand.

Pick a file or folder, choose where the output should go, and the app handles the checks, naming, progress, and final result.

## Download

https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest

Download:

- `HakamiqChdTool.App-v1.0.8-win-x64.zip`

## Requirements

- Windows x64
- .NET 8 Desktop Runtime x64
- Enough free space for temporary files and the final CHD

A short local path is best for the first run. Simple paths make failures easier to understand.

## Quick start

1. Download the ZIP from the latest release.
2. Extract it to a normal folder, not inside the ZIP viewer.
3. Run `HakamiqChdTool.exe`.
4. Add one file or folder.
5. Choose an output folder.
6. Start the queue.

Start with a single file — easier to verify before running a large batch.

## What you can use it for

Convert ISO to CHD. Add the ISO, choose an output folder, and start the queue.

Convert CUE/BIN discs. Add the `.cue` file, not just the `.bin`; the track files need to stay beside it.

Convert GDI-based discs where the current workflow supports them. Keep all track files together.

Prepare CSO input before CHD conversion. The app prepares the source first, then sends the prepared image through the normal CHD path.

Open ZIP, RAR, or 7Z archives that contain a supported disc image. Damaged or password-protected archives stop early.

Verify an existing CHD before moving it into long-term storage. A quick check is cheaper than finding a bad file later.

Extract supported CHD files. CD and GD-ROM output normally becomes CUE/BIN; DVD output becomes ISO; hard disk output becomes IMG.

Get clearer guidance for PS2 disc images. The app looks at disc structure where possible, not just the file name.

## Legal

The tool does not include games, ROMs, BIOS files, ISO files, CHD files, Redump data, keys, firmware, or user data.
Use it only with content you own or are legally allowed to process.

## Limitations

Hakamiq CHD Tool is not an emulator and does not download game files.
It is a Windows WPF app, not an Android app or C++ rewrite.
It uses chdman for CHD work; it does not implement a CHD encoder from scratch.

## More documentation

Detailed pages are in [`docs/`](docs/README.md): supported formats, conversion options, chdman integration, logs, and developer notes.
