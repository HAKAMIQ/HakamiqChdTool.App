# Security note — TOCTOU in media intake (fixed)

Status: FIXED in v1.6.0. Severity was LOW (reproduced, not exploited).
Reported: 2026-11-05. Fixed: 2026-11-19.

## What it was
The input path was validated (no reparse points) and then opened again by path:
by the intake header probe in `Core/Input/MediaClass.cs`, and later by chdman and
the preparation tools. A local process with write access to the folder could swap
a path component for a junction or symbolic link in between, so the classifier read
a different file than the one validated. Four scenarios were reproduced: regular
file replacement, parent junction, file symlink, missing path then symlink.

## Fix
- **Intake probe:** after opening the file, the opened object is validated
  (`Core/Input/HandleIdentity.cs`): the handle must not be a reparse point and its
  final path (links resolved, the volume root resolved through a handle so mapped
  and subst drives compare correctly) must equal the validated path. Otherwise the
  probe is unavailable and CSO, ZSO and CHD inputs are refused.
- **Conversion:** `SourceFileLease` (`Services/Conversion/SourceLease.cs`) opens the
  input and the track files it names in its own folder for reading, with read
  sharing only, from the start of preparation until chdman finishes, and checks each
  handle the same way. chdman and the preparation tools can still read; no other
  process can write, replace, rename or delete the files, or rename their folder,
  during the job. A source that is open for writing elsewhere, or whose path changed,
  is refused with `LocConversion_SourceNotStable`.

## Remaining behaviour
- A plain file replaced in place before the job starts is a regular file at the
  validated path; it is classified and converted as the file that is there. The
  lease keeps it from changing once the job has started.
- Files in an app-owned temp workspace (archive extraction, CSO/ZSO/ECM/CloneCD/MDS
  staging) are leased too when they are the conversion input.

## Tests
`HakamiqChdTool.App.Tests/MediaInputToctouTests.cs` (formerly the reproduction on
branch `test/p0-toctou-reproduction`): a parent junction, a file symlink and a
symlink at a missing path swapped in after validation are refused; a plain
replacement is classified as itself; the lease blocks write, rename, delete and
folder rename, allows readers, refuses a junction path and a file open for writing;
a conversion is refused while a track file is open for writing. The two symlink
tests need `SeCreateSymbolicLinkPrivilege` (run elevated or with Developer Mode).
