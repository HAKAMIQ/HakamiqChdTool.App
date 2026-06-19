# Hakamiq CHD Tool

Hakamiq CHD Tool is a Windows x64 desktop app for converting, checking, and extracting disc image files with CHD workflows. It is built for local use: add files, review the queue, run the job, then check the result.

## Download

Get the latest release from:

<https://github.com/HAKAMIQ/HakamiqChdTool.App/releases/latest>

Choose one package:

| Package | Use it when |
| --- | --- |
| `HakamiqChdTool-*-win-x64-self-contained.zip` | Best for most users. Runs without installing .NET separately. |
| `HakamiqChdTool-*-win-x64-runtime-required.zip` | Smaller download. Requires .NET 8 Desktop Runtime x64. |

## Quick start

1. Download one of the ZIP files above.
2. Extract it to a normal folder, not inside `Program Files`.
3. Run `HakamiqChdTool.exe`.
4. Add your disc image files.
5. Pick an output folder and start the queue.

That's it. For most cases, the self-contained package is the easiest choice.

## Common uses

### Convert ISO to CHD

Add an `.iso` file, choose the output folder, then start the queue. The app keeps the original file and writes a new CHD.

### Convert CUE or GDI to CHD

Add the `.cue` or `.gdi` file, not only the track files. Keep the referenced `.bin` or track files in the same folder.

### Prepare CSO input

CSO files are prepared through the bundled Hakamiq.CsoKit helper before CHD conversion. If the helper is missing, the item fails cleanly instead of producing a bad CHD.

### Verify a CHD

Use verification when you want to check an existing CHD before archiving it or moving it to long-term storage.

### Extract a CHD

Add a CHD and choose an extraction workflow when supported. Good for checking a result against the original source.

### Use the queue

Add multiple files, review each item, then run the queue. Failed and canceled items stay separate, which makes cleanup easier.

### Check external tools

Open Options, then External Tools. You should see the bundled tools detected from the release folder.

### Work with Arabic paths

The interface supports Arabic and English. Technical paths stay readable left-to-right so file locations do not become confusing.

## Package reference

| Item | Meaning |
| --- | --- |
| Windows support | Windows x64 desktop |
| UI | WPF |
| Main output | CHD |
| Bundled helpers | chdman and Hakamiq.CsoKit |
| Release type | Portable ZIP |
| Installer | Not provided |

## Limitations

No games, ROMs, BIOS files, ISO files, CHD files, or Redump databases are included. PS3-related handling is conservative and still experimental. Use the tool only with files you own or are allowed to process.

## More documentation

Detailed notes live in [`docs/`](docs/README.md). Start there if you need supported format notes, troubleshooting, legal notices, or deeper behavior details.
