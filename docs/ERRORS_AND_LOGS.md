# Errors and logs

Most failures come from the source file, output folder, missing tools, or storage problems.

The app tries to catch the obvious ones early. If it stops before conversion, that is usually a safer result than producing a partial output.

## Source read errors

CRC and I/O errors usually point to the source file, archive, drive, or storage path.

Copy the file to a local disk and try again. Network paths and failing drives turn simple jobs into noisy ones.

## Path problems

Long paths, protected folders, invalid characters, and cloud-synced folders can break external tools.

For testing, use something short:

`D:\CHDWork\Sample.iso`

## Missing track files

CUE, GDI, and TOC files refer to other files.

Keep the descriptor and track files together. If one track is missing or renamed, the job should stop.

## Unsafe CUE references

CUE files are plain text. They can reference files by name, relative path, or sometimes unsafe paths.

The app blocks references that escape the expected folder. That is not being picky; it prevents a CUE from pulling in the wrong file.

## Archive problems

ZIP, RAR, and 7Z files must open before conversion.

Password-protected, damaged, incomplete, or deeply nested archives can fail before the CHD step. Fix the archive first.

## Low space

Keep enough free space for temporary files and the final CHD.

A nearly full drive can make a normal conversion fail late. That is the worst time to discover it.

## Tool errors

Some actions need chdman, 7-Zip, or CsoKit.

If a tool is missing, blocked by security software, or incompatible, the app should report that as a tool problem.

## Cancellation

Cancelled jobs are shown separately from failed jobs.

This is intentional. It keeps a user action from looking like data loss or a broken source.

## What to include in a report

Include:

- app version
- Windows version
- input type
- action used: Convert, Verify, or Extract
- short error text
- the relevant part of the log

Remove usernames, private paths, tokens, unrelated file names, and screenshots with private data.

## What not to attach

Do not attach games, ROMs, BIOS files, disc images, CHD files, Redump files, keys, firmware, or private dumps.
