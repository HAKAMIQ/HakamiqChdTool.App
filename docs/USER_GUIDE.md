# User guide

This guide covers the normal local workflow for CHD Tool. For format-specific details, see [Supported formats](FORMATS.md).

## Before you start

Extract the release ZIP to a normal local folder before running the app. Do not run it from inside the ZIP, from a repository `bin` or `obj` folder, or from a protected Windows folder.

A short writable path such as `C:\Tools\HakamiqChdTool` is a good choice. Start `HakamiqChdTool.exe` from the extracted release folder.

The runtime-required package needs the Microsoft .NET 10 Desktop Runtime for Windows x64.

## Basic workflow

1. Open CHD Tool.
2. Add a supported source file or folder.
3. Review the detected input and any safety warning.
4. Choose an output folder with enough free space.
5. Select Convert, Verify, or Extract as appropriate.
6. Start the queue and review the final result for every item.

Start with one file before processing a large batch. It is easier to confirm the output location and behavior with a small first run.

## Explorer right-click menu

Turn on **Settings > Performance > Add CHD Tool to the Explorer right-click menu**
to get:

- **Convert to CHD** on ISO, CSO, ZSO, CUE, GDI, TOC, NRG, CCD, MDS, ECM, ZIP,
  7Z and RAR files.
- **Extract CHD** and **Verify CHD** on CHD files.
- A **CHD Tool** submenu with all three on folders.

The entries open CHD Tool, or the window that is already open, switch to that
mode and add the selected items to the queue. While the queue is running, the
items are added when it finishes. On Windows 11 the entries are under
**Show more options**. The menu is added for your Windows account only and
needs no administrator rights. Turn the option off before you delete or move
the app folder; if you move it, start the app once from the new place to update
the entries.

## Recompress CHD files

**Tools > Recompress CHD files** makes existing CHD files smaller, for example
files made with fast or no compression. Add files or a folder (or drop them on
the list), pick a compression level and either a folder for the new files or
**Replace the original files**. For each file, CHD Tool runs `chdman copy`,
then `chdman verify`, and compares the SHA-1 values with the original. A new
file is kept only if it matches and saves at least 1%; otherwise it is deleted
and the original stays as it was. Without Replace, nothing is overwritten.

## Command line

```
HakamiqChdTool.exe [--mode convert|extract|verify] [--start] [--exit-when-done] --add <file or folder>...
```

- `--mode` picks the operation; without it the mode selected in the window is used.
- `--add` lists files and folders; relative paths are resolved against the
  current folder.
- `--start` starts processing once the items are in the queue.
- `--exit-when-done` (with `--start`) closes the app after the run. The exit
  code is 0 when every item finished or was skipped, 1 when an item failed,
  2 when the run was stopped, and 3 when there was nothing to process.

If CHD Tool is already open, the items are added to that window, and
`--exit-when-done` is ignored so your open window is not closed. Output
folders and conversion options come from Settings. A batch file example:

```
start /wait "" "C:\Tools\HakamiqChdTool\HakamiqChdTool.exe" --mode convert --start --exit-when-done --add "D:\Disc images"
echo Exit code %ERRORLEVEL%
```

## Multi-file disc images

Keep descriptor files and their tracks together. For example, add the CUE file for a CUE/BIN image and keep every referenced BIN beside it. Do not rename or move only part of a disc set.

The app refuses missing or unsafe references instead of guessing the disc layout.

## Output and queue results

Use a stable local drive when possible. Avoid protected, cloud-synced, failing, or nearly full storage for large conversions.

Each queue item ends with a distinct result such as success, failure, skip, or cancellation. An existing destination may be skipped to protect it from accidental replacement.

Canceling a running job can leave a partial temporary or output file. Review the destination before retrying the same item.

## Verification and metadata

Redump or DAT information is optional and user-provided. It can help identify or compare a disc, but it does not prove that a damaged or incomplete source is safe to convert.

PS3-related detection remains experimental. Recognizing an input does not mean that conversion is supported.

## After a run

To leave a long batch running, pick **When done: sleep** or **When done: shut
down** next to the queue buttons before you start. When the run finishes, a
60-second countdown appears; **Cancel** (the default button) stops it. The
choice applies to that run only and is not saved; a stopped run never sleeps
or shuts down. Shutdown does not force other programs to close.

When a run ends, a bar above the queue shows the result: how many items
finished, failed, were skipped or cancelled, the space the conversions saved,
and how long the run took. **Details** opens the full report. The bar stays
until you close it or start the next run.

Review the result of every queue item. If an item fails repeatedly at the same point, check the source file, companion files, free space, storage health, and the displayed log before retrying.

See [Errors and logs](ERRORS.md) for common failures and the information to include in a report.
