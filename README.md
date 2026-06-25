# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 app for converting supported game disc images to CHD without writing chdman commands by hand.

Pick a file or folder, choose an output folder, and start the queue. The app handles the checks, naming, progress, and final output.

## Download

Latest release:
https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest

Choose one package:

- `HakamiqChdTool-v1.0.8-win-x64-runtime-required.zip`  
  Smaller download. Requires .NET 8 Desktop Runtime x64.

- `HakamiqChdTool-v1.0.8-win-x64-self-contained.zip`  
  Larger download. Runs without installing .NET separately.

## Quick start

1. Download one ZIP file from the latest release.
2. Extract it to a normal folder.
3. Run `HakamiqChdTool.exe`.
4. Add a supported disc image.
5. Choose the output folder.
6. Start the queue.

Start with one file first. Easier to check the output before running a full batch.

## What it is for

Use it to convert supported disc images such as ISO, CUE/BIN, and supported CHD extraction outputs into a cleaner CHD-based library.

It can also help with common problems before conversion starts — missing files, unsafe CUE paths, bad archive input, or unsupported layouts.

## Notes

The tool does not include games, ROMs, or BIOS files. Use it only with files you own or are legally allowed to process.

## Limitations

Windows x64 only.  
Not every disc layout is supported.  
CHD work still depends on chdman support.

## More documentation

See `docs/` for supported formats, conversion options, logs, and troubleshooting.