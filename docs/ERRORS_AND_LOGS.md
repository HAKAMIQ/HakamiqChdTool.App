# Errors and logs

Most errors come from the source file, output folder, missing tools, or
low disk space.

The app should stop before conversion when the problem is clear.

## Source read errors

CRC or read errors usually mean the source file, archive, drive, or
storage path has a problem.

Copy the file to a local disk and try again.

## Path problems

Long paths, invalid characters, protected folders, or cloud folders can
make tools fail.

For testing, use a short local path.

## Missing track files

CUE, GDI, and TOC files point to track files.

Keep the CUE, GDI, or TOC file and its track files in the same folder.

## Archive problems

ZIP, RAR, and 7Z files must open before conversion.

Password-protected, damaged, incomplete, or deeply nested archives may
stop before conversion starts.

## Low space

Keep enough free space for temporary files and the final CHD output.

A nearly full drive can make a normal job fail.

## Tool errors

Some actions use chdman or archive tools.

If a required tool is missing, blocked, or incompatible, the app should
show that clearly.

## Cancellation

A cancelled job is not a failed job.

The app should show cancelled jobs separately.

## Logs

When reporting a problem, include:

- app version
- Windows version
- input type
- action used
- short error text
- small part of the log

Remove private paths, usernames, and unrelated file names before sharing
logs.

## Files not to attach

Do not attach games, ROMs, BIOS files, disc images, CHD files, Redump
files, keys, firmware, or private dumps.
