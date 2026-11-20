# Changelog

This file tracks source release notes.

GitHub Releases remain the public download history.

Each version starts with a short `### Release notes` list written for users.
Only that list is published as the GitHub Release text; the subsections after
it are the detailed record.

## v1.7.0 - Unreleased

### Release notes

- Optional Explorer right-click menu: Convert to CHD on disc images and
  archives, Extract and Verify on CHD files, and a CHD Tool submenu on folders.
- Opening files while CHD Tool is already open adds them to the open window.
- Watch folder (optional): new disc images and archives copied into a chosen
  folder are converted once fully written, while the app is open.
- Command line: `--start` processes the added items and `--exit-when-done`
  closes the app afterwards with an exit code for scripts.
- After a run, a bar above the queue shows how many items finished, failed or
  were skipped, the space saved and the time taken, with a Details button for
  the full report.
- The tray notification after a run, shown when the window is not in front,
  now has the same result line, including the space saved and the time taken.
- Advanced options read more clearly: Arabic text has no diacritics, labels
  drop repeated "(recommended)" marks and English duplicates, the CPU name and
  igir version show in the right order, and the Explorer menu and watch folder
  options explain themselves on screen (on Windows 11 the menu entries are
  under Show more options).
- A CHD platform profile no longer marks the other disc types in the queue as
  unsupported; they convert with Auto. The hard-disk profile is unchanged.
- With more than one file converting at once, Auto threads are shared
  between them instead of each chdman taking the full Auto count.
- Choosing an igir.exe that is not the pinned build no longer switches off
  the bundled igir, and an unusable external chdman is written to the log.
- The tray icon menu no longer shows a light block behind the text in the
  dark theme.
- Options that could not work are gone: the "Manual" performance mode (it
  behaved like Safe) and "Force CD" for ISO files (always refused). Saved
  values load as Safe and Auto.
- The delete-source options are available only while "Verify after
  conversion" is on, the Redump option says it reads each source before
  converting, and "Delete incomplete output" says it applies to failed
  CUE/BIN extractions.
- The Quiet priority setting now also covers 7-Zip, csokit and igir, not only
  chdman.
- If the watch folder is deleted while the app is open, watching stops and
  the status bar says so.
- Importing a Redump DAT names it after the chosen file; a hidden system name
  left in old settings no longer re-labels it.
- Number lists in Advanced options use Latin digits, and the hunk-size presets
  say they are not applied to PS2 DVD and PSP images.
- Arabic messages across the app use plain wording: direct instructions
  instead of "يرجى", simple verbs instead of "سيتم + verbal noun", and no
  English word repeated in brackets.
- Opening Advanced options no longer replaces the saved Redump download
  address.
- "When done" next to the queue buttons: sleep or shut down the computer
  after the run, with a 60-second countdown you can cancel. It applies to one
  run, is never saved, and does nothing after a stopped run.
- Target emulator setting: PCSX2 and PPSSPP (DVD CHDs with 2048-byte hunks)
  or RetroArch and older emulators (no zstd). Automatic, the default, keeps
  the current behavior.
- Recompress CHD files (Tools in the sidebar): makes existing CHDs smaller with
  stronger compression. Each new file is verified and matched against the
  original, and kept only if it saves at least 1%. New files go to another
  folder, or replace the originals when you choose that.

### Details

- `HakamiqChdTool.exe --mode convert|extract|verify --add <path>...` starts the
  app with files in a mode; a second launch hands them to the running window
  over a current-user-only named pipe.
- The Explorer menu is written under `HKCU\Software\Classes` only and is off
  by default.

## v1.6.0 - 2026-11-19

Adds five input formats and updates the bundled tools. Existing CD/DVD
conversion defaults, queue run/cancel/retry behavior and output naming are
unchanged.

### Release notes

- New inputs: hard-disk images (`.img`, `.hdd`), ZSO, CloneCD (`.ccd`),
  Alcohol 120% (`.mds`) and ECM.
- Hard-disk images convert only when the "Hard disk image" profile is selected
  in Options.
- Dreamcast GD-ROM CHDs extract to GDI; a Settings option keeps CUE/BIN.
- ZSO, CloneCD, Alcohol 120% and ECM images also convert from inside ZIP, 7Z
  and RAR archives.
- Deleting the source after a verified conversion now also covers ZSO, CloneCD,
  Alcohol 120% and ECM sets, and keeps sets whose subchannel data the CHD lacks.
- Security: a source file can no longer be swapped between its checks and the
  conversion; files another program is writing are refused.
- CSO files that maxcso made for large images now convert.
- Updated the bundled CsoKit to 0.6.6 and 7-Zip to 26.06.
- The optional igir report now uses igir v5.8.1.
- The Add files dialogs and the empty-queue hint list every supported format.

### Hard-disk images

- Raw hard-disk images (`.img`, `.hdd`) convert with `chdman createhd` when
  the new "Hard disk image" CHD profile is selected in Options. Auto mode never
  converts them and names the profile to choose.
- An `.img` that belongs to a CD/DVD (a same-name CUE/CCD/TOC/GDI/MDS, a
  sibling descriptor that references it, a CD sync header or an ISO9660
  descriptor) stays out of the queue and is refused even with the profile
  selected. The size must be a non-zero multiple of 512 bytes.
- Compression and hunk size are left to chdman's hard-disk defaults.

### ZSO

- `.zso` files with the `ZISO` signature are expanded to a temporary ISO with
  CsoKit and converted with `createdvd`. The declared size is read with
  `csokit detect`, damaged data is rejected by `decompress`, and the temporary
  ISO must match the declared size exactly. CsoKit 0.6.4 or newer is required;
  an older external CsoKit is refused with a message.
- ZSO inside ZIP, 7Z and RAR archives is extracted and converted the same way.
- CloneCD and Alcohol 120% images inside archives are extracted with their
  `.img`/`.mdf`; an archived CUE may name tracks stored as `.bin.ecm`.
  Source cleanup after a verified ZSO conversion deletes the `.zso`.

### CloneCD

- Adding a `.ccd` converts the CloneCD set through a temporary CUE with
  `createcd`. The `.img` is hard-linked into the temp workspace when possible,
  otherwise copied after a free-space check; nothing is written to the source
  folder.
- Multiple sessions, scrambled data tracks, a missing or misaligned `.img`, and
  track layouts that cannot be mapped exactly are refused before chdman runs.
- Subchannel data (`.sub`) is not kept, so copy protections that depend on it
  do not survive the conversion.

### Alcohol 120% (MDS/MDF)

- Adding an `.mds` converts the Alcohol 120% image: a CD through a temporary
  CUE with `createcd`, a DVD with `createdvd`. Raw 2352-byte, 2048-byte Mode 1
  and 2448-byte images with subchannel data are supported; subchannel data is
  dropped in the temp workspace and not kept in the CHD.
- MDS version 2/MDX, multiple sessions, mixed or unsupported sector sizes, a
  missing, split or mismatched `.mdf`, and gaps between tracks are refused
  before chdman runs. Nothing is written to the source folder.
### ECM

- `.bin.ecm` and `.img.ecm` images are decoded in the temp workspace and
  converted with `createcd`, through their CUE when one is next to them (mixed
  `.ecm` and plain tracks are supported) or as a single data track otherwise.
- The decoded data must match the checksum stored in the file; damaged or
  truncated files are refused before chdman runs. A CUE covered by `.ecm`
  tracks is queued once.
### Dreamcast GD-ROM extraction to GDI

- **Behavior change:** a CHD whose metadata marks it as a GD-ROM now extracts
  to GDI (one file per track) instead of CUE/BIN. CD CHDs are unchanged. The
  new setting "Extract Dreamcast GD-ROM to CUE/BIN" keeps the old output.
- The GDI set is published like CUE/BIN: tracks renamed to the output name,
  the GDI written last, no overwrite without permission, rollback on failure,
  and the extraction proof rebuilds a CHD from the GDI and compares the SHA-1.
### Security

- Fixed a low-severity race between path validation and file use. The intake
  probe validates the opened file handle, and each conversion holds its source
  and track files open for reading (no writes, renames or deletes by others)
  until chdman finishes. A source that is open for writing elsewhere, or whose
  path changed after it was checked, is refused before anything runs.

### CsoKit 0.6.6

- Updated the bundled CsoKit from 0.6.3 to the official 0.6.6 release (source
  commit `2f4da904e1420bbb9da2e508415841108414ab0c`). Native ABI 2 and the CLI
  arguments the app uses are unchanged.
- **Behavior change:** CSO files written by maxcso for large images (16 KB
  blocks with a full-size compressed final block), which CsoKit 0.6.3 to 0.6.5
  rejected, now convert.
- Pinned the new SHA-256 digests and updated the tool manifest, CsoKit legal
  notice, third-party notice and SBOM.

### 7-Zip 26.06

- Updated the bundled 7-Zip (`7z.exe`, `7z.dll`) from 26.02 to the official
  26.06 x64 release. The installer digest matched the GitHub release digest;
  the license text is unchanged.
- Pinned the new SHA-256 digests in the runtime check, the repository and
  release gates, the tool manifest and the SBOM.

### igir 5.8.1

- The optional igir report now requires the official v5.8.1 Windows x64
  executable (was v5.7.2). `scripts/FetchIgir.ps1` and
  `scripts/FetchIgirSource.ps1` fetch and verify v5.8.1 and its GPL-3.0
  Corresponding Source (`igir-5.8.1-source.tar.gz`). An existing v5.7.2
  executable is no longer accepted; run the fetch script again.
- The app still runs only `igir report` with the same arguments. Run side by
  side on a synthetic library, v5.8.1 produced the same CSV columns and rows
  as v5.7.2 and wrote nothing outside the run directory.
- Pinned the new digests in the locator, fetch scripts, release gates, legal
  notices, tool manifest and SBOM; updated the Options and Library messages.

### Interface

- The Add files dialogs list `.zso`, `.ccd`, `.mds`, `.ecm`, `.img` and `.hdd`
  in both languages, and the empty-queue hint names every supported format on
  its own left-to-right line so the Arabic layout keeps the order. A test
  checks that every extension the queue accepts is in the filters.
- Danger buttons (Clear log, Orphan clean) take their colours from the active
  theme; the colours themselves are unchanged.
- The queue row activity spinner runs only while it is shown and stays still
  when Windows animations are off.
- Removed 13 unused button styles (one whole dictionary); no button looks
  different.

### Release pipeline

- The Secure Release workflow publishes only the version's `### Release notes`
  list plus a short download line, and fails when that list is missing or
  empty. The fixed "Download options" and "Verification" sections are gone.

### Validation

- Application tests: 240 passed, 0 failed, 6 skipped in a non-elevated run;
  the skipped tests are the symbolic-link tests, which need administrator
  rights. An elevated run before the four interface tests were added passed
  242 of 242.
  New tests cover each format's refusal rules and real round trips through the
  bundled tools (createhd/extracthd, ZSO and maxcso-layout CSO through CsoKit
  and createdvd/extractdvd, CloneCD, Alcohol MDS/MDF and ECM through createcd/extractcd
  and createdvd/extractdvd), each compared
  by SHA-256 or byte for byte.
- Repository convention, local verification and end-user release output gates
  passed.

## v1.5.1 - 2026-10-24

Maintenance release. The only functional change is the bundled CsoKit update.

### CsoKit 0.6.3

- Updated the bundled CsoKit runtime from 0.6.1 to the official 0.6.3 release
  (source commit `e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d`). The CLI and the
  native ABI 2 contract are unchanged.
- Pinned the new SHA-256 digests of `csokit.exe` and `CsoKit.Native.dll`; both
  are still revalidated before every execution.
- **Behavior change:** CSO preparation now rejects damaged CSO files that
  CsoKit 0.6.1 accepted, such as data after the last block, a final index
  offset that does not match the file size, undersized stored blocks, or
  blocks that do not decode to their exact length. A rejected file fails before
  chdman runs, leaves no temporary ISO, and the source file is not modified.
- Updated the tool manifest, CsoKit legal notice, third-party notice and SBOM.

### Release pipeline

- The Secure Release workflow now publishes this changelog section as the
  GitHub Release notes and fails when the version has no section, instead of
  publishing fixed highlights written for v1.4.0.

### Validation

- Application tests: 190 passed, 0 failed, 0 skipped when run with
  administrator rights, which the symbolic-link tests require. Added a test
  proving a CSO with trailing data is rejected without leaving a temporary ISO.
- The bundled CsoKit ISO/CSO/ISO round trip and a CSO to ISO to CHD to ISO
  round trip with chdman verify preserved SHA-256.
- Repository convention, local verification and end-user release output gates
  passed.

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
