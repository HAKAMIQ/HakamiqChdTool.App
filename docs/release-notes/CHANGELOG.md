# Changelog

## v1.0.3 — 2026-06-06

### Added

- Integrated Hakamiq.BluRayAnalyzer as an internal PS3 / Blu-ray ISO analysis layer.
- Added raw ISO detection support for PS3 and Blu-ray/UDF media.
- Added metadata checks for PS3 disc markers such as `PS3_DISC.SFB`, `PARAM.SFO`, and `EBOOT.BIN`.

### Changed

- Kept `HakamiqChdTool.App` as the fixed source/package folder name while advancing assembly metadata to `v1.0.3`.
- Preserved the existing queue, workflow, Redump, 7-Zip, and `chdman` runtime behavior from v102.

## v1.0.2 — 2026-06-06

### Changed

- Converted the application to one unified public version for all users.
- Removed Free/Premium/licensing runtime gates and related product activation flow.
- Kept third-party legal notices for MIT, MAME/chdman, and 7-Zip.
- Aligned application version metadata with the GitHub release tag.

### Fixed

- Redump standard-name suggestion setting can be enabled or disabled from Advanced Options without requiring the local Redump database to be available first.

## GitHub source package cleanup — 2026-06-04

### Changed

- Prepared the WPF source tree as the public GitHub repository snapshot.
- Removed generated build output folders such as `bin`, `obj`, and `Release` from the source package.

### Safety

- No queue, workflow, `chdman`, Redump, conversion, or verification runtime logic was changed for this cleanup.
- No games, ROMs, BIOS files, copyrighted disc images, Redump databases, keys, or user media are included.

## v1.0.0 — 2026-04-05

### Added

- Arabic RTL WPF interface.
- CHD conversion, extraction, verification, and archive handling.
- Queue processing with cancellation and duplicate path guard.
- Embedded `chdman` support.
- Optional Redump SQLite hash matching using user-provided local data.
- Dark, Light, and HAKAMIQ themes.
- Optional Velopack update checks.
- Global exception handling and crash logging.

### Improved

- CHD media type detection.
- Runtime tool cleanup under `%LocalAppData%\HakamiqChdTool\`.
- Async Serilog logging.
- `chdman` command logging.
- Optional `ChdmanCliRunner` timeout.

### Packaging

- Added `LICENSE`.
- Added `LEGAL.md`.
- Added `SECURITY.md`.
- Added `THIRD_PARTY_NOTICES.txt`.
- Added `CHDMAN_NOTICE.md`.
- Added `SEVENZIP_NOTICE.md`.
- Added `MAME_COPYING.txt`.
- Added `MAME_GPL-2.0.txt`.

## Release hardening pass — CHD phase 1

### Changed

- Converted Rename Confirmation dialog/viewmodel from VB artifacts to C#.
- Reconnected rename confirmation to the WPF dialog.
- Updated queue viewport pin tracking to reference counting.
- Preserved selected queue item viewmodel during DataGrid loading/unloading.
- Corrected Arabic wording: “استخراج من CHD”.
- Clarified CHD extraction smoke tests.

### WPF-only CHD logical reader probe

- Added an internal `ChdLogicalProbeService` wrapper for `chd_reader_tool.exe info`.
- Enriched `ChdInfoResult` with physical/logical bytes, hunk size, total hunks, decoded cache bytes, and probe status.
- Kept the existing `chdman` conversion/verification workflow unchanged; the probe is metadata enrichment only.
- Added a compact logical CHD report to the existing verification/result dialog when probe data is available.
- The report shows compressed size, logical size, estimated saved storage, hunk size, total hunks, and decoded read cache.
