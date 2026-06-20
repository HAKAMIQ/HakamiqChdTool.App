# Errors and logs

Most errors come from the input file, the output path, missing helper
tools, or low disk space.

The app should stop early when the problem is clear.

## Source read errors

CRC or I/O errors usually mean the source file, archive, drive, or
storage path is not reliable enough.

Copy the file to a local disk and try again.

## Path problems

Very long paths, invalid characters, protected folders, or cloud-synced
folders can cause tool failures.

For testing, use a short local path.

## Missing descriptor files

CUE, GDI, and TOC files usually reference other track files.

Keep the descriptor and its referenced files in the same folder.

## Archive failures

Archives are staged before conversion.

Encrypted, damaged, incomplete, or nested archives may fail before the
conversion starts.

## Storage

Keep enough free space for temporary files and the final output.

A nearly full drive can make a normal job fail in a confusing way.

## External tools

chdman, archive tools, and helper probes are external processes.

If a tool is missing, blocked, or incompatible, the app should report
that clearly.

## Cancellation

Cancelled jobs are not failed jobs.

The app should keep cancelled, failed, skipped, unsupported, and
successful jobs separate.

## Logs

Logs should help explain the failure without exposing private data.

Useful details are:

- app version
- Windows version
- input type
- selected action
- short error text
- relevant log excerpt

Remove private paths, usernames, serials, and unrelated file names
before sharing logs.

## Issue reports

When reporting an issue, include only what is needed to understand the
problem.

Do not attach games, ROMs, BIOS files, disc images, CHD files, Redump
databases, keys, firmware, or private dumps.
