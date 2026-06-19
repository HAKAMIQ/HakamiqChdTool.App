# Errors and logs

Most failures are local: the source cannot be read, the output path is unsafe, the disk is short on space, or the external tool exits with an error.

The goal is to catch the problem early. Better to stop before conversion than keep a CHD that was built from a bad read.

## Common error areas

### Source read errors

CRC or I/O errors usually mean the source file, archive, drive, or storage path is not reliable enough for conversion. Copy the file to a healthy local disk and try again.

### Path problems

Very long paths, control characters, invalid names, or blocked folders can make `chdman` fail. Use a short local path for the first test.

Example:

```text
D:\CHDWork\Game.iso
D:\CHDOut\Game.chd
```

Simple. Fewer moving parts.

### Missing descriptor dependencies

CUE, GDI, and TOC files reference other track files. If those files are missing or renamed, conversion is blocked before `chdman` runs.

### Archive failures

Archives are staged before conversion. Encrypted, damaged, incomplete, or nested archives may fail during preview or extraction.

### Storage pressure

The app estimates work space, but storage can still change while a job runs. Keep extra free space for temporary files and final output.

### Cancellation

A cancelled job is not the same as a failed job. The app separates cancellation, failure, and success so cleanup and final status stay readable.

## Logs for issue reports

When opening an issue, include:

- app version
- Windows version
- input type, not the game file itself
- operation selected: convert, verify, extract, or archive staging
- short error text from the UI
- relevant log excerpt with personal paths removed

Do not attach games, ROMs, BIOS files, disc images, Redump databases, keys, or private media.
