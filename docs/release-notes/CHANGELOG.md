# Changelog

This file tracks source release notes.

GitHub Releases remain the public download history.

## v1.0.8 - 2026-06

### Added

- Added PS2 compatibility advisory notes for CD/CUE-BIN input, CONFIG
  guidance, and emulator profile differences.
- Added PS2 disc structure scanning from SYSTEM.CNF for ISO and CUE/BIN
  input.
- Added source package and release output cleanliness gates.

### Changed

- Unified CUE/BIN file reference parsing across intake, safety, metadata,
  extraction, and workflow path checks.
- Cleaned obsolete CUE and CHD workflow helper code after the shared parser
  and release gates were added.
- Kept release output verification as a disposable gate so generated files are
  checked without staying in the source tree.

## v1.0.7 - 2026-06

### Changed

- Improved CHD extraction output naming for CUE/BIN and Dreamcast-style disc
  layouts.
- Clarified supported input formats in user documentation.
- Improved media intake failure handling for unsupported or unsafe inputs.

## v1.0.6 - 2026-06

### Added

- Added PSP CSO input support through Hakamiq.CsoKit.
- Added CSO checking before CHD conversion.
- Added CSO to temporary ISO preparation.
- Added CsoKit legal notice files.

### Changed

- Kept CHD creation under chdman.
- Cleaned user and legal documentation.

## v1.0.5 - 2026-06

### Changed

- Prepared a maintenance release.
- Cleaned project structure.
- Cleaned release files.
- Improved UI consistency.

No new public feature was added in this release.

## v1.0.4 - 2026-06

### Improved

- Strengthened source checks before conversion.
- Blocked conversion when CRC or I/O read errors are found.
- Improved handling for large ISO and CHD files.
- Improved low-space warnings.
- Improved queue performance options.
- Improved cleanup after cancellation or failure.

## v1.0.3 - 2026-06-06

### Added

- Added PS3 and Blu-ray ISO analysis.
- Added raw ISO detection for PS3 and Blu-ray/UDF media.
- Added checks for PS3 files such as PS3_DISC.SFB, PARAM.SFO, and
  EBOOT.BIN.

### Changed

- Kept HakamiqChdTool.App as the source and package folder name.
- Kept the existing queue, Redump, 7-Zip, and chdman behavior.

### Fixed

- Redump standard-name suggestions can be enabled or disabled from
  Advanced Options without requiring a local Redump database first.

## v1.0.2 - 2026-06-06

### Changed

- Converted the app to one public version for all users.
- Removed Free and Premium gates.
- Removed product activation flow.
- Kept third-party notices for MIT, MAME/chdman, and 7-Zip.
- Aligned app version files with the GitHub release tag.
- Added CHD reader tool support for showing CHD file details.

## GitHub source package cleanup - 2026-06-04

### Changed

- Prepared the WPF source tree for the public GitHub repository.
- Removed generated folders such as bin, obj, and Release.

### Safety

- No queue, chdman, Redump, conversion, or verification logic was
  changed in this cleanup.
- No games, ROMs, BIOS files, disc images, Redump files, keys, or user
  media were included.

## v1.0.0 - 2026-04-05

### Added

- Arabic RTL WPF interface.
- CHD conversion, extraction, and verification.
- Archive handling.
- Queue processing with cancellation.
- Duplicate output path guard.
- Bundled chdman support.
- Optional Redump SQLite hash matching using user-provided local data.
- Dark, Light, and HAKAMIQ themes.
- Optional Velopack update checks.
- Global error handling and crash logging.

### Improved

- CHD media type detection.
- Tool cleanup under %LocalAppData%\HakamiqChdTool\.
- Serilog file logging.
- chdman command logging.
- Optional timeout for chdman runs.

### Packaging

- Added LICENSE.
- Added LEGAL.md.
- Added SECURITY.md.
- Added third-party notices for MAME/chdman and 7-Zip.

## Release hardening pass - CHD phase 1

### Changed

- Converted the rename confirmation dialog from VB artifacts to C#.
- Reconnected rename confirmation to the WPF dialog.
- Improved queue item tracking.
- Preserved selected queue item during DataGrid loading and unloading.
- Corrected Arabic wording: استخراج من CHD.
- Clarified CHD extraction smoke tests.

## WPF-only CHD reader tool

### Added

- Added a CHD reader tool for existing CHD files.
- Added physical size, size after opening, block size, block count, and
  read status to CHD result details.
- Kept conversion and verification under chdman.
- Added a compact CHD details report to the result dialog when available.
