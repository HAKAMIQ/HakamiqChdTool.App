# Changelog

This file tracks source release notes.

GitHub Releases remain the public download history.

## v1.5.0 - 2026-10-18

### Library organizer

- Added a Library window that indexes a game library, reads Logiqx XML and
  ClrMamePro DATs, matches files and previews a per-platform organization.
- Analysis and preview are read-only. Content hashing runs only when requested;
  name/size or CRC32-only matches stay unverified candidates.
- Added an optional organizer that moves only reviewed, verified items. Every
  move is journaled, can be undone, and is refused when the source or the
  destination changed after preview. CUE/BIN and GDI sets move as one unit and
  roll back together. The organizer never deletes files.

### igir (optional)

- Added an optional read-only DAT report through the unmodified igir v5.7.2
  executable. Only `igir report` is invoked, and the executable's SHA-256 is
  verified before each run.
- igir is not committed to the repository; `scripts/FetchIgir.ps1` and
  `scripts/FetchIgirSource.ps1` fetch the pinned executable and its GPL-3.0
  Corresponding Source. Release packaging, SBOM and legal notices include igir
  when it is bundled.

### Redump verification

- Added a disc verification policy: a Redump match counts as verified only when
  every track is present and size, MD5, SHA-1 and CRC32 all agree, the matched
  system fits the disc structure, and the platform is supported for Redump proof.
- CHD disc structure is taken from CHD metadata tags only.
- Identical hashes owned by several disc records are resolved against every
  owning disc; the same content proving under two systems is reported as a
  conflict.
- Oversized CUE/GDI descriptors are refused, a file that changes while it is
  hashed is rejected, and importing a DAT with no ROM rows no longer empties
  the existing database.

### Reliability and data safety

- Hardened output, pending-workspace, cleanup and orphan handling against
  junctions, 8.3 short-name aliases, wildcard archive entries, concurrent files
  and directories replaced after a scan.
- Orphan cleanup now requires the app's ownership marker and skips while another
  workflow is active.
- Settings saves are atomic and recover stale staging files.
- A queue item now releases only its own input-path reservation, so a late
  completion of a cancelled item can no longer free the reservation of a newer
  item for the same path.
- Overlapping add-files requests are serialized; a second request while one is
  running is refused with the existing "wait for background operation" status.
- **Behavior change:** with "Skip existing output" enabled (the default), a
  `.sbi` file already next to the output is kept instead of overwritten.

### Interface

- Queue intake adds rows in one batch, which keeps large intakes responsive.
  Batches above 16 rows refresh the queue list once instead of row by row.
- The footer queued count is shown as `{0} / {1}`.
- Added igir settings to External Tools.
- Adjusted theme colors to meet WCAG AA contrast in Light and Dark.

### Validation

- See the release gate results for this version.

## v1.4.0 - 2026-09-12

### Reliability and data safety

- Hardened CUE/BIN rescue so contradictory adjacent CUE layouts fail closed.
- Refused ambiguous adjacent CUE layouts instead of selecting one by enumeration order.
- Prevented unsupported legacy CUE encodings from being silently ignored during rescue inference.
- Refused multi-track BIN rescue when INDEX/PREGAP timing evidence is unavailable.
- Added CSO decompression size validation against the declared uncompressed size.

### Runtime and maintenance

- Hardened CHD operation lifecycle, cleanup, verification, and diagnostic logging.
- Removed obsolete and unreachable source components and unused abstractions.
- Cleaned architecture documentation and release verification residue.

### Validation

- Application regression tests passed: 62/62.
- Debug and Release builds passed.
- Repository convention verification passed.


## v1.2.1 - 2026-08-02

### Changed

- Updated the bundled CsoKit runtime to 0.6.1 and adopted its `csokit.exe` plus
  native ABI 2 deployment contract.
- Pinned and revalidated the SHA-256 digests of both `csokit.exe` and
  `CsoKit.Native.dll` before bundled-tool execution.
- Strengthened archive, Redump, external-tool, shutdown, and release-output
  security boundaries.
- Added deterministic NuGet lock files, CycloneDX 1.7 SBOM generation, source
  manifests, and end-user release verification gates.
- Updated the About dialog credits and contributor presentation.

### Validation

- Debug and Release builds passed with zero warnings and zero errors.
- Application regression tests passed: 31/31.
- CsoKit 0.6.1 upstream tests passed: 201/201.
- Repository convention and end-user release security gates passed.
- The bundled CsoKit integration ISO/CSO/ISO round trip preserved SHA-256.

## v1.2.0 - 2026-08-01

### Security and reliability

- Updated the bundled official MAME `chdman` to 0.289 and added an x86-64-v2
  compatibility gate.
- Added typed CHD header evidence aligned with MAME's magic, version, and header
  length semantics; wrong, truncated, unsupported, locked, and unavailable
  probes no longer reach the queue by extension fallback.
- Bounded 7-Zip output and added entry-count, expanded-size, live extraction,
  free-space-reserve, and partial-output cleanup policies.
- Made extraction-root sampling fail closed and removed the SharpCompress
  disk-extraction fallback; SharpCompress is now read-only archive validation.
- Bounded Redump downloads and ZIP expansion, restricted synchronization to
  GitHub hosts, disabled automatic redirects, validated every redirect before
  the request, limited chains to five hops, and made catalog activation atomic.
- Made shutdown cleanup conditional on confirmed queue quiescence and observed
  tasks that finish after a timeout.
- Added NuGet lock files and a deterministic CycloneDX 1.7 SBOM gate.
- Updated the bundled CsoKit runtime from 0.4.0-beta.1 to 0.6.1, adopted its
  `csokit.exe` plus native ABI 2 deployment contract, and pinned both runtime
  files before every execution.
- Recorded the exact CsoKit source commit and runtime hashes in the tool manifest,
  legal notice, and SBOM, closing the previously undocumented provenance gap.

### Tests

- Added negative coverage for CHD v3/v4/v5 evidence, wrong magic, truncated
  headers, unsupported versions, invalid header lengths, archive budgets, list
  parsing, and Redump source policy.
- Added executable regression coverage for extraction-monitor failure, 7-Zip
  output-flood termination, allowed/blocked/circular Redump redirects, SQLite
  rollback after malformed DAT parsing, and shutdown timeout reporting.
- Added a bundled CsoKit integration round trip through the application's probe,
  process runner, JSON parser, verification, and temporary ISO preparation layers.

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
