# Organizer implementation status

Date: 2026-10-17

## Status

**Implemented; synthetic Release acceptance passed with documented limits.** The
analyzer and planner are read-only. The move service, journal, recovery, WPF
Execute/Journal/Undo actions, and report-only igir integration are present.

## Implemented

- Index → DAT parse/match → reviewable preview uses the existing `Core/Library`
  and `LibraryAnalysisService` pipeline.
- `Services/Library/LibraryOrganizerService.cs` validates preview freshness and
  path containment, hashes sources with SHA-256, refuses occupied destinations
  and cross-volume moves, journals intent before rename, verifies moved bytes,
  reconciles interrupted intent, and supports guarded undo.
- Multi-file groups are selected and undone as units. Group undo preflights all
  original paths and rolls back already-restored members if a later member
  fails. Recovery-required journal states remain visible for inspection.
- Library window Execute, Journal, Undo, and optional igir report actions use
  Arabic/English resources. Execute and Undo require localized confirmation.
- Optional igir v5.7.2 integration is report-only. `IgirArgs` allow-lists
  arguments, `IgirToolLocator` verifies the pinned SHA-256, and the bounded CSV
  parser retains raw ROM file lists and rejects unknown status values.

## Executed acceptance evidence

- Extracted Release WPF application: actual clicks completed Analysis, reviewed
  the synthetic ISO/CUE-BIN/GDI/CHD preview, confirmed Execute, inspected
  Journal, confirmed Undo, then verified all 8 original paths and SHA-256 hashes
  restored with no files left in the organized output.
- Release UI language and theme controls toggled Arabic/English and Light/Dark.
  The window remained laid out at 820×600.
- The final self-contained ZIP was extracted and launched. Its WPF igir action
  launched official igir v5.7.2 against the isolated synthetic ISO and XML DAT;
  the displayed CSV contained `FOUND Synthetic PlayStation acceptance`.
- Missing/tampered igir paths, invalid report columns/status, and process failure
  are covered by the custom test runner. The WPF run with the mixed synthetic
  media DAT/library also surfaced the native igir process exit failure; no
  igir write/move/delete arguments are used.
- Custom Release test runner: 182 passed, 0 failed, 4 skipped (186 total).
  Skips require symlink privileges unavailable to this account.
- `VerifyRepo.ps1`, `VerifyRelease.ps1 -DeploymentMode self-contained`, and
  `PackRel.ps1` including extracted-ZIP verification passed.
- Final package: `Release/packages-v1.4.0-selfcontained-20261017-02/HakamiqChdTool-v1.4.0-win-x64-self-contained.zip`,
  159,050,452 bytes; SHA-256
  `91171A52C644A7B7BC6D71908B52BF65C22824CDB98F4705158C948AA871A652`.

## Not verified / remaining limits

- Synthetic fixtures establish workflow behavior, not compatibility with real
  game media. The synthetic mixed-media igir invocation exited with a native
  failure; only the isolated ISO/XML DAT invocation produced a valid report.
- Cancellation, interruption recovery, collision/conflict, locked file,
  disk-full, cross-volume and race matrices were not fully exercised in the UI.
- Four symlink privilege-dependent tests were skipped. Restore reported NU1900
  because the configured vulnerability feed was unreachable; locked restore and
  package verification still completed.
- Organizer group operations are not atomic at the filesystem level; a power
  loss or rollback failure may require journal recovery and manual resolution.

No commit or push was performed.
