# Audit coverage ledger

Repository-local record of what the audit inspected, how deeply, what it found and which test covers it.
Update it in place; do not keep the only copy elsewhere.

Depth classes:

- **Full**: the whole file was read.
- **Partial**: only the cited line ranges were read.
- **Pattern**: found or checked by search/script only, not read.
- **None**: not inspected.

Baseline: 124 passed, 0 failed, 4 skipped. Previous recorded run: 162 passed, 0 failed, 4 skipped. Resumed run (Debug and Release): **166 passed, 0 failed, 6 skipped** (172 total); see section E.

**The repository audit is NOT complete.** 515 production C#/XAML files exist (about 100,000 lines, excluding `artifacts/`, `obj/`, `bin/` and the test project). The "File coverage" section lists what was read; every other file is Pattern or None.

Initial state of the second pass: branch `feature/library-organizer`, HEAD `686e7553d279dd7b45f58d88ededc58622fa30d0`, 65 status entries, staged diff SHA-256 `5F4BF315...601EE9`, unstaged diff SHA-256 `AD8DF768...350250` (before any change in this pass).
The 4 skips are the symbolic-link tests; see `docs/SYMLINK_VERIFICATION.md`. A skip is not a pass.

## Defects fixed (tests added)

| ID | Path | Defect | Test |
| --- | --- | --- | --- |
| F1 | `Core/Queue/QueueStore.cs`, `Core/Queue/QueueRtState.cs` | `CompleteScheduledItem` is called twice for a cancelled item (by `Cancel`/`Stop`, then by its cancelled worker). The second call released the input-path reservation of a newer item admitted in between, so the duplicate-path guard could be bypassed. The reservation now records its owner and only the owner releases it. Verified red before the fix. | `QueuePathOwnershipTests.cs` |
| F2 | `Services/RedSqlMgr.cs`, `Services/RedumpV2/RedumpV2Engine.cs` | `TryMatchHash` returns one arbitrary row (`LIMIT 1`, no order) when identical hashes belong to several disc records. A disc whose tracks are shared with an earlier-imported game was reported as a conflicting match. The multi-file path now resolves against every disc owning every file (`FindCommonDiscs`), runs the policy per candidate, and fails closed if the same content proves under different systems. | `RedumpAmbiguityTests.cs` (not run against the old code; the failure mode follows from the import order the test sets up) |
| F3 | `Services/RedumpV2/RedumpV2Engine.cs` | The size used for matching came from `FileInfo.Length` read after hashing, not from the bytes hashed. The hashed byte count is now used, and a file whose length moved during hashing is rejected. | No dedicated test (needs a file mutated mid-hash). |
| F4 | `ViewModels/LibraryAnalysisVM.cs` | `RebuildFilters` rebuilt every visible row twice on a new result (filter change handler plus an explicit call). At 100,000 rows: 200,003 collection notifications and 615 ms with a bound `DataGrid`; now 100,002 and 360 ms. | `LibFilterTests.cs` |
| F5 | `HakamiqChdTool.App.Tests/LibLayoutTests.cs` | The real-window layout test created the process-wide WPF `Application` in the test process; a failure or hang would leave it behind for every later test. It now runs in a child process (`--ui-test library-layout`) that is killed on timeout. | itself |

| F6 | `Core/Workflow/ActiveWorkflowRegistry.cs` (new), `ChdFlowOrch.cs`, `Services/OrphanScan.cs`, `Core/Workflow/FlowPreStage.cs` | **U2 fixed.** Automatic orphan cleanup (minimum age zero) could classify a sibling in-process job's live workspace as abandoned, because the only guard looked at other processes. Every workflow now holds an `ActiveWorkflowRegistry` lease for its whole run; the scanner returns nothing while any workflow besides the caller's own is active. Fails closed. Manual/other callers default to zero tolerated workflows. | `ActiveWorkspaceTests.cs` (2 and 3 active, double dispose, 16-way barrier contention) |
| F7 | `Core/Workflow/FlowOutClean.cs` | **U6 fixed.** `ContainsReparsePoint` used `IgnoreInaccessible = true`, so an unreadable subtree was skipped and the tree could be deleted. Enumeration errors now report "unsafe". Verified red by construction (the old option skipped the denied directory). | `PendingCleanFailClosedTests.cs` (ACL deny on a disposable directory, rule removed afterwards) |
| F8 | `Core/Workflow/Paths/SafePathValid.cs` | **U7 partly fixed.** A relative CUE reference through a junction inside the cue folder passed the string-prefix containment check. Every component below the cue directory is now checked for reparse points; unreadable means unsafe. Absolute-reference and whole-file `ReadAllLines` points remain. | `CueReferenceLinkTests.cs` |

## Pass 5 (this session; per-file depth is in `docs/AUDIT_FILES.md`)

| ID | Path | Result | Test |
| --- | --- | --- | --- |
| F24 | `Core/Workflow/Paths/PendingWsPath.cs` | **`.hakamiq-job` ownership marker revalidated.** The marker was a constant string, so a job folder copied or renamed together with its marker (or any folder given the constant text) qualified for automatic deletion. The marker now names its own directory; a copy, a rename, the old constant-only text, an oversized file or a reparse-point marker is refused. Unknown ownership fails closed. Pre-existing user folders, stale workspaces (age cutoff plus per-item "changed since scan" check), directory replacement (reparse scan, per-item re-check), concurrent cleanup (registry re-checked per item) and shutdown were already covered by F6, F9, F19, F21 and the tests named there. Markers written by the unreleased F21 build are not recognised and their folders are left in place. | `OrphanMarkerTests.cs` (bound to directory) |
| F25 | `Core/Workflow/Extraction/ExtOutValid.cs` | **Overwrite policy at the filesystem operation, CUE/BIN bundles.** With overwrite off, the early `File.Exists` check was the only guard; the dependency and CUE promotions used the replace-with-backup move, so a file created in between was replaced. Each bundle file now uses the non-replacing move, a lost race rolls back what this call promoted, and rollback restores a backup with `overwrite: false` instead of deleting whatever sits at the original path. | `SingleFileFinalizeTests.cs` (concurrent BIN, concurrent CUE; new `BeforeBundlePromotion` seam) |
| F26 | `Services/PostProcessing/SbiCopy.cs`, `ISbiCopy.cs`, `PostArtSvc.cs`, `Core/Workflow/FlowPost.cs` | The post-conversion `.sbi` copy replaced a pre-existing `<name>.sbi` next to the output regardless of skip-existing. It now replaces only when skip-existing is off (the output itself is being replaced); otherwise the existing file is kept and nothing is reported as copied. | `SbiCopyOverwriteTests.cs` |
| F27 | `Core/Workflow/FlowOutPath.cs` | **Replace-mode promotion deleted a file it did not create.** If the move failed because a file had appeared at the final path after the existence check, the failure handler deleted that file. It now deletes the target only when this call may have created it (cross-volume copy that began and did not report "already exists"). Verified red. | `SingleFileFinalizeTests.cs` (`BeforePromotionMoveForTests` seam) |
| F28 | `Services/ZipExtract.cs` | 7-Zip runs with `-y` and every failure path empties the destination, so a destination that already held user files had them overwritten or deleted. Extraction now refuses a non-empty destination. Both production callers use a fresh directory, so this is defence in depth. Real 7-Zip test. | `ArchiveWildcardRealTests.cs` |
| F29 | `Services/ZipInspect.cs` | The descriptor read (`7z e -so`) accepted `*` and `?` in the entry name (the extraction path already refused them). Verified red against real 7-Zip, which streamed both matching entries. | `ArchiveWildcardRealTests.cs` |

Other pass-5 results:

- Queue through a real `DataGrid`: with 5,000 rows and a selected row, a single-item Add, a Reset batch (200 rows), a descending sort and a filter that keeps the row all left `SelectedItem` unchanged (`ShellMatrixTests.cs`, 4 language/theme runs).
- `docs/architecture/SECURITY_BOUNDARIES.md` described a "private session directory" copy of chdman that does not exist. The code runs `Tools\chdman.exe` under the application directory after a SHA-256 check (check and start are separate steps). 7-Zip is the bundled copy (digest pinned) or the 7-Zip folder under Program Files (no digest). The document now says so.
- Known and accepted: the optional external CsoKit path from the settings file is not digest-pinned (name, reparse and sibling-DLL checks only); it is a user-chosen tool.
- Dead code with weaker overwrite behaviour, no production caller: `WorkflowPathUtilities.CopyMatchingSbiIfExists` / `WorkflowPendingPathPolicy.CopyMatchingSbiIfExists` (`overwrite: true`), `WorkflowOutputPathPlanner.TryFinalizeExtractedDiscImageOutput`, `MoveCompanionFileIfExists`, `ExtractionOutputBundleValidator.CleanupPromotedTargets`. Left in place.

Pass-2 investigations with no code change (rule: prove first):

- **U3** Queue scheduling. `QueueManager` driven with a gated orchestrator at limits 1, 2, 3, with one running item cancelled and with one failing item: observed concurrency never exceeded the limit (the semaphore enforces it), items complete, nothing stays active. The register-after-start window (and a limit change inside it) could not be reproduced deterministically without a test hook, so scheduling is unchanged. Test: `QueueConcurrencyInvariantTests.cs`.
- **U4** Intake overlap. Two `IngestPathsAsync` calls issued back to back on a real dispatcher thread were serialized (one ran, one got the wait message) and `IsAddingFiles` changed only on the UI thread. The pre-await check-then-set gap is real by reading the code but did not manifest because the probe completes synchronously for the files tried; the off-thread `IsAddingFiles = false` after `ConfigureAwait(false)` on the success and cancel paths was not exercised (needs a queue-row store in the test session). Unchanged; test kept as a guard: `IntakeOverlapTests.cs`.

Also added: `InputResolverJunctionTests.cs` (junctions need no privilege, so this runs where the symlink tests skip) and `PerfMeasurements.cs` (opt-in, `--perf-mode all|library|redump`, asserts nothing).

## Pass 6 (resumed stabilization audit)

The starting index and worktree were both dirty, with staged, unstaged and untracked changes. No index operation was performed; all existing staged and untracked work was preserved. Two changes in this pass are limited to test fixture capability reporting in `PendingCleanFailClosedTests.cs` and `LibIndexTests.cs`: when Windows refuses `SetAccessControl`, the ACL-dependent case now reports `SKIP` and removes its temporary fixture. The security assertions still execute when the ACL can be installed.

Fresh validation on 2026-10-11:

- Debug and Release solution builds: passed, 0 warnings and 0 errors each.
- Debug and Release harnesses: **166 passed, 0 failed, 6 skipped of 172** each. Four symlink cases were unavailable because Windows denied symlink creation; two ACL-denial cases were unavailable because `SetSecurityInfo` was denied. Junction cases passed. A skipped ACL test is not evidence that inaccessible-subtree behavior passed.
- `scripts/VerifyRepo.ps1`: passed. `scripts/PkgCleanGate.ps1`: passed for 585 tracked files; its Release output check explicitly skipped because no release output was supplied.
- Release security campaigns in a temporary directory outside the repository: fuzz **1048/1048**, corpus **7 files / 370043 bytes**, and WPF busy-queue shutdown **8 started / 4 cancelled / 1393 ms** passed.
- Final Git-visible diff checks are recorded in section E below; untracked additions are outside Git's diff checks.
- `--perf-mode all`: 100,000-row library filtering populated a bound grid in 354 ms; filtering to a status took 92 ms and returning to All took 394 ms. Queue `AppendRange` took 11 ms at 100,000 rows. The one-by-one path took 13957 ms to append 100,000 and 23101 ms to remove 5,000 from that collection (synthetic rows; no UI window). A second queue run that attempts 50,000 individual removals was interrupted after the known quadratic cost became clear; no result is claimed for that measurement.

Architecture/security pass reviewed the current queue ownership and batching paths, media classifier/reparse boundary, CUE bounds and rollback ownership, orphan workspace registry/marker checks, overwrite-race handling, archive wildcard and destination protections, Redump candidate resolution, library DAT/index/matching/planning services, and settings atomic-save changes against the architecture and security boundary documents. No additional confirmed defect was identified in these reviewed paths. This is not a full source review: the file ledger still has **101 UNINSPECTED production files**, and a number of services, views, models, resource dictionaries and runtime paths remain Pattern or None.

Fluent 2 validation: the existing bilingual/theme tests, library-window layout cases, 224-case dialog matrix, and four MainWindow language/theme runs with 5,000 rows passed. These use logical work-area sizes, not true per-monitor DPI. High Contrast, genuine DPI transitions, rendered-control contrast across every state, and assistive technology behavior remain unvalidated.

Release status: **NOT RELEASE READY.** There is no produced release output or end-user release gate result in this pass; the organizer's apply/undo end-to-end workflow is not present or validated; ACL and symlink behavior has the skips above; DPI/High Contrast accessibility coverage is incomplete; and the production audit is incomplete. Passing builds, source cleanliness, tests, and synthetic security campaigns do not close those gaps.

## Pass 3 (this session)

| ID | Path | Result | Test |
| --- | --- | --- | --- |
| F9 | `Services/OrphanClean.cs`, `Core/Workflow/FlowPreStage.cs` | **P0.** The registry was consulted only at scan time. A sibling workflow starting between scan and delete was not seen. `Clean` now re-checks `ActiveWorkflowRegistry` and deletes nothing if any workflow other than the caller's is active. | `ActiveWorkspaceTests.cs` (sibling starts after the scan) |
| F10 | `ViewModels/MainVM.Queue.cs`, `MainVM.Intake.cs` | **U4 fixed.** Intake is claimed with `Interlocked.CompareExchange` before the first await; the old check-then-set on `IsAddingFiles` left a window through the fast-path probe. `IsAddingFiles` is now written on the dispatcher thread. | `IntakeOverlapTests.cs` (existing; passes) |
| F11 | `Core/Workflow/Paths/SafePathValid.cs` | **U7 closed.** All three whole-file cue reads are bounded (1 MiB, 8192 lines); an absolute `.bin` reference is refused when the file itself is a reparse point. `CueReferenceLinkTests` was **vacuous before**: the cue text contained `\t` (a tab) instead of `\\t`, so the junction path was never reached. It now exercises the junction and passes. | `CueReferenceLinkTests.cs` (junction, oversized cue) |
| F12 | `Services/ZipExtract.cs` | **U8 fixed.** Entry names containing `*` or `?` are refused (7-Zip would treat them as wildcards). | `ArchiveEntryArgTests.cs` |
| F13 | `Services/FilePathGate.cs` | **U5 fixed.** The lock key folds extended-length prefixes and 8.3 short names onto one key (key only; the CLI path is unchanged). | `GatePathAliasTests.cs` (real 8.3 alias) |
| F14 | `Services/Configuration/AppSetSvc.cs` | An explicit `Save`/`SaveAsync` now cancels a pending debounced save, whose older snapshot could otherwise overwrite it 500 ms later. Verified red (cancel removed: expected 3, actual 1). | `SettingsAtomicTests.cs` |
| F15 | `ViewModels/Virtualization/QueueRowStore.cs`, `VirtQueue.cs`, `MainVM.Fast.cs`, `MainVM.Queue.cs` | **Performance, and a defect found while fixing it.** Intake appended rows one at a time and every append copied the visible array (100,000 rows: 9.6 s). `AppendRange` batches the append. The first version raised one multi-item `Add`; a test with a real sorted `ListCollectionView` showed WPF throws `NotSupportedException: Range actions are not supported` for that, so a bound DataGrid would have crashed on the first batch of two or more rows. The collection now raises one single-item `Add` per row for batches of 16 or fewer (selection and scroll position behave as before) and one `Reset` for larger batches. 100,000 rows: 12 ms. Rows built before a cancel or failure are still added (try/finally). | `QueueRowStoreTests.cs`, `QueueBatchingTests.cs` (filter, sorted and filtered `ListCollectionView`, repeated sessions, partial progress on cancel) |
| F16 | `Core/Workflow/FlowSrcClean.cs` | **Withdrawn.** The earlier alias change was unproven. Investigation (log capture with the change removed) showed `NormalizeFullPath` already resolves 8.3 aliases, so the output is refused by the original check. The change is reverted (file equals HEAD apart from line-ending noise). `SourceCleanAliasTests.cs` is kept as a characterization test and says so. | `SourceCleanAliasTests.cs` |
| F17 | `Core/Workflow/Paths/SafePathValid.cs` | **Confirmed defect, fixed.** `TryFinalizeExtractedCueBinOutput` deleted `finalCuePath` in every failure path, including when the pending cue could not be read, so a pre-existing user cue at the final path was deleted. Rollback now deletes only what this call promoted and refuses paths through reparse points. Verified red (the old line restored: test fails; fixed: passes). | `PromotedCleanupTests.cs` |
| F18 | `Services/Conversion/ChdSvcSupport.cs` | **U9 closed.** The only producer of the auxiliary cue is `RestoreTargetPolicy` (`<stem>.legacy-restore.cue` in the pending workspace). `TryDeleteAuxiliaryOutputFile` now deletes only that shape and refuses paths through reparse points. | `AuxCueDeleteTests.cs` (uses the real policy output) |
| F19 | `Services/OrphanClean.cs` | Deletion authorization is per item, immediately before each delete: registry re-checked, and an item whose directory time changed after the scan is skipped. A deterministic seam registers a sibling between two items. Workspace names are fresh GUIDs, so a workflow that starts after a scan cannot adopt a listed workspace; the remaining window is one item's deletion and cannot affect a path that was not listed. | `OrphanOwnershipTests.cs` (sibling after scan, replaced directory, changed after scan) |
| F20 | `Services/RedumpSync.cs` | The Redump sync keeps a work directory under the scanned process temp root but was not a registered workflow, so automatic orphan cleanup could delete a live download. It now holds an `ActiveWorkflowRegistry` lease for the whole run (released on cancellation). | `OrphanOwnershipTests.cs` (blocking handler, lease held, released on cancel) |
| F21 | `Core/Workflow/Paths/PendingWsPath.cs`, `FlowOutPath.cs`, `Services/OrphanScan.cs`, `OrphanClean.cs` | **Confirmed data-loss path, fixed.** With a custom pending-workspace root, any folder named `Operation_*` under it was classified as a job directory and deleted by automatic orphan cleanup. Job directories now carry a `.hakamiq-job` marker written when the app creates them; orphan scan and clean require it. Directories created by earlier versions have no marker and are no longer cleaned automatically (fail closed). | `OrphanMarkerTests.cs` (unmarked user folder survives, marked folder is cleaned, new job directories are marked) |
| F22 | `Core/Workflow/Extraction/ExtOutValid.cs`, `Core/Workflow/FlowOutPath.cs`, `FlowConv.cs` | **Confirmed defect, fixed.** `TryFinalizeSingleFile` ignored `allowOverwrite`, and conversion promotion never re-checked skip-existing. Two inputs that map to one final file (`Game.cue` and `Game.iso` both give `Game.chd`) both pass the early check; the second to finish replaced the first's output. New `TryPromoteProducedFileIfAbsent` is one non-overwriting move; with skip-existing on, a taken final path is reported as skipped (conversion) or refused (extraction) and the produced file stays in its workspace for cleanup. With skip-existing off the old backup-and-replace behaviour is unchanged. | `SingleFileFinalizeTests.cs` (red before the fix) |
| F23 | `Services/ZipExtract.cs` | Real 7-Zip execution test: an archive with entries `g*.chd` and `gz.chd`; with the wildcard check removed 7-Zip extracts both files, with it only `gz.chd`. The earlier test covered only the helper. | `ArchiveWildcardRealTests.cs` |

Read in full this pass, no defect: `Services/ChdCliRun.cs`, `Services/ChdProcRun.cs` (process start, kill-tree, pump timeouts, disposal), `ViewModels/Virtualization/VirtQueue.cs`, `QueueRowStore.cs`, `Core/Workflow/FlowSrcClean.cs`, `Services/OrphanClean.cs`, `Services/OrphanScan.cs`.

Delete sites reviewed in context (all reparse-aware or bounded to the process temp root): `ExtOutProof`, `FlowOutPath`, `FlowSrcClean`, `CueFlowAdapt`, `CueResWriter`, `RedumpSync`, `RedumpV2Engine.CleanupTemporaryRoots`, `CsoPrep`, `M3uGen`, `SbiCopy`.

Open, unfixed:

- `ExtOutValid.CleanupPromotedTargets` (line 971) has no caller; it is dead code and was left in place.
- The check-then-move gap in the overwrite path (`PromoteProducedFileToFinalLocation`, used when skip-existing is off) is unchanged by design: that mode replaces by intent, with a backup.
- `QueueRowStore.TryRemove` is O(n) (reindex) and `VirtualizedQueueCollection` copies on each remove: removing 50,000 of 100,000 rows one by one takes 68 s. The UI removes one row per user action, so this is not reachable in bulk today.
- `DeepHash` legacy `TryMatchHash` first-row semantics are reachable only when no V2 options are passed (tests, `ConvSafe` with no database); the UI passes options.
- A `maxExecutionTime` timeout in `ChdmanCliRunner` is reported as `WasCancelled`, so a caller cannot tell it from a user cancel.

## A.1 InputResolver optimization

`EnumerateFilesSafe` verifies the directory chain from the volume root once per directory, then checks only each file's own reparse attribute instead of re-walking every ancestor per file. Rejection policy is unchanged: a reparse point anywhere in the chain, or on the file, rejects it.

What changed in the failure window, stated exactly:

- `Directory.EnumerateFiles` opens its directory handle after the chain check and enumerates that handle, so the files returned belong to the directory that was verified even if a path component is swapped afterwards.
- The strings returned are paths. Any consumer that opens them later resolves the path again. That window existed before the change and still exists; neither the per-file walk nor the per-directory check closes it.
- The old per-file walk would sometimes notice a swap that persisted while a large directory was still being enumerated; the new code does not. That detection was best-effort, not a boundary.
- `HasReparsePointInExistingPathFromVolumeRoot` and the `File.Exists` + `GetAttributes` pair are separate calls, so a check can race a replacement. Errors reject (fail closed).

Conclusion: no new unsafe acceptance was found; the best-effort re-detection during long enumerations is gone. Real protection against replacement needs the consumer to open by handle and verify, which is out of scope here. Covered by `InputResolverJunctionTests.cs` and, with privilege, the four symlink tests.

## A.2 "Delete incomplete output file" (`DeleteFailedOutput`, default off)

Two independent layers delete outputs:

1. `Services/Conversion/ChdResultMap.cs` `TryDeleteIncompleteOutputs`, called by `ChdConvSvc` on cancel, retry and failure. It is **not** gated by the setting and has no reparse check. It deletes the output, its `.sbi`, and known extraction companions.
2. `Core/Workflow/FlowCleanup.cs` `TryCleanupFailedOutput`, gated by the setting.

Where output goes: chdman writes into a pending workspace job directory and the file is promoted to the final location only on success (`WorkflowOutputPathContract.BuildPendingOutputPath` / `PromoteProducedFileToFinalLocation`). When the failed candidate is inside a known pending workspace, `FlowCleanup` deletes that whole job tree regardless of the setting (`DeletePendingWorkspaceDirectoryTree`, which refuses reparse points and non-job directories).

So the setting only governs a candidate that is already in the user's output folder, for example a promoted file whose later verification failed. For that case:

- `.cue`: `TryDeleteFailedCueBinBundle` deletes only a bundle that validates as a complete CUE/BIN set with every file inside the cue directory and no reparse point. A broken or partial bundle fails validation and survives.
- Anything else (`.chd`, `.iso`, `.gdi`, single `.bin`): `CleanupService.DeleteFiles(candidate, .sbi)`. That method refuses every path outside the process temp root (`TryResolveDeletableFile`), so for any file in a user output folder it deletes nothing and logs at Debug. **This is why non-CUE outputs survive.** `CleanupBoundaryTests.cs` asserts the boundary.
- `.gdi` track files are never part of any cleanup set.
- A cancel or exception that never returns a result leaves `failedOutputCandidate` null, so the flow stage has nothing to delete; only layer 1 applies.

Ownership risk (not changed): `FlowExtract` returns a Failure with `OutputPath = finalOutputPath` when `SkipExistingOutput` finds an existing final file that fails validation. That file was not created by the app. The `.cue` path is safe (an invalid bundle is not deleted) and the non-CUE path is currently inert because of the temp-root refusal. If the non-CUE deletion is ever "fixed" by widening `DeleteFiles`, that pre-existing file becomes deletable. A safe design needs a per-run ownership record (the app promoted this exact path during this run) before any deletion outside the workspace. Not implemented: it changes deletion semantics, which the audit rules forbid without a maintainer decision.

## A.3 Cleanup and path-safety review

Read in full: `Services/CleanupService.cs`, `Services/OrphanClean.cs`, `Services/OrphanScan.cs`, `Services/FilePathGate.cs`, `Core/Workflow/FlowOutClean.cs`, `Core/Workflow/FlowCleanup.cs`, `Core/Workflow/Paths/SafePathValid.cs`.

Open items (unfixed):

- **U2 (fixed, see F6)** `FlowPreStage.TryCleanupOrphanedWorkspacesAfterBlocker` scans with minimum age zero. The only guard against live work is "another Hakamiq/chdman/7z process is running" (`HasPotentiallyActiveSiblingWorkload`), which excludes the current process. With `MaxConcurrentConversions` above 1, a sibling job in an in-process phase (no chdman/7z child running) can have its pending workspace or temp extraction deleted when another job hits a disk-space blocker. A fix needs a registry of active job directories that the scanner excludes; that touches every workspace creation site.
- **U6 (fixed, see F7)** `FlowOutClean.ContainsReparsePoint` uses `IgnoreInaccessible = true` (fail open on unreadable subtrees) while `CleanupService.ContainsReparsePoint` treats unreadable as unsafe (fail closed). `FlowOutClean.DeleteDirectoryIfEmpty` has no reparse or root check, unlike `CleanupService`'s version; it only removes empty directories.
- **U5** `FilePathExclusiveGate` keys on `Path.GetFullPath`, so 8.3 short names, `\\?\` prefixes and other aliases of one file take different locks. The reference counting itself is correct.
- **U7** `SafePathValid.IsCueReferenceUsable` checks containment by string prefix and does not check reparse points on the referenced file; the extracted-cue paths read the whole cue with `ReadAllLines`.
- **U8** Archive extraction (`Services/ZipExtract.cs`): entry arguments go after `--` and reject rooted, `:`, `.`/`..` and `@`; the extracted tree is walked and any reparse point or out-of-root path throws. Entry names containing `*` or `?` are wildcards to 7-Zip and may select more entries than intended (still inside the root).

No path-escape defect was found in these files.

## A.4 Redump

See F2 and F3. Fast path (`TryFastPath`) already requires a unique SHA-1+size hit (`FindRomsBySha1AndSize`). The legacy first-row semantics remain in `Services/DeepHash.cs` (`TryMatchHash` at line 246 and its own `MatchesBelongToOneDisc`); reachability of that path from the UI was not confirmed.

## B. Runtime audit

Read in full: `Core/Queue/QConcurrency.cs`, `QueueStore.cs`, `QueueRtState.cs`, `QueueCtrl.cs`, `QueueManager.cs`, `QueueRun/RunCoord.cs`, `ViewModels/MainVM.cs`, `ViewModels/MainVM.Queue.cs`.

Findings left as they are, because the rules forbid changing queue run/cancel/retry behavior:

- **U3** `ScheduleQueuedItem` starts `RunScheduledItemAsync` before adding the task to `RunningItems`. The semaphore reference is read before registration, so a concurrency-limit change applied in that window lets an item gate on the old semaphore. The duplicate-id branch also leaves the already-started task running. Both need a duplicate id or a limit change at the same instant.
- Terminal status is published before `CompleteScheduledItem` releases the path, so an immediate re-add of the same path can be rejected as a duplicate for a moment. This errs safe.
- **U4** `AddPathsCoreAsync` checks `IsAddingFiles` and sets it only after an `await`, so two intakes can overlap (the second replaces `_intakeCancellationCts`, so Cancel reaches only one). Row insertion re-checks existing paths on the UI thread, so no duplicate rows result. `IsAddingFiles = false` is set after `ConfigureAwait(false)` continuations, which raises `CanExecuteChanged` off the UI thread; whether WPF marshals every handler was not verified at runtime.
- `QueueController` and `QueueRunCoordinator`: no defect found. Cancellation drains for 15 s, then releases the UI wait while workers finish; late completions are handled.

Not inspected: queue virtualization (`ViewModels/Virtualization`), `TaskExec`, `QTransSvc`, `QNotifyPub`, process runners (`ChdCliRun`, `ChdProcRun`), other ViewModels, Services, Models.

## C. Performance (Debug build, this machine; relative numbers matter)

`--perf-mode all`:

| Measurement | Result |
| --- | --- |
| `ApplyFilter` / apply, 10,000 rows, no UI | 13 ms |
| 100,000 rows, no UI | 49 ms |
| 10,000 rows, bound `DataGrid` | 97 ms |
| 100,000 rows, bound `DataGrid` | 360 ms (was 615 ms) |
| Filter to one status, 100,000 rows, grid | 94 ms |
| Back to All, 100,000 rows, grid | 367 ms |
| Redump import, 100,000 ROMs | about 1.5 s |
| `TryMatchHash`, hit or miss | about 135 us per call (about 7,400 per second); one connection opened per call |
| `FindCommonDiscs`, 1 file / 40 files | 200 us / 640 us per call |

The filter is not a bottleneck at these sizes; only the duplicate pass was removed. Lookup cost is connection overhead, and a 40-track disc costs a few milliseconds against minutes of hashing, so nothing was changed there. Pass 3, `--perf-mode queue` (Release, row store plus virtualized collection, no window):

| Rows | One-by-one append | `AppendRange` | Heap |
| --- | --- | --- | --- |
| 10,000 | 288 ms | 206 ms | 17 MiB |
| 100,000 | 9,614 ms | 1,306 ms | 173 MiB |

Removing 5,000 rows one by one: 395 ms at 10,000 rows, 10,086 ms at 100,000. Removing 50,000 of 100,000: 68,124 ms (quadratic; see F15 and the open list).

Not measured: Dispatcher responsiveness during a real queue run, DataGrid scrolling at 100,000 rows in a real window, memory growth over a long run, cancellation latency.

## D. Fluent 2

Pattern-checked only:

- English and Arabic dictionaries have the same 1,606 keys.
- Every `Loc*` key referenced from XAML (`StaticResource`/`DynamicResource`) and from C# string literals (1,040 distinct) exists.
- XAML has 4 literal text attributes: the product name, a version placeholder and a rename example (`AboutWindow.xaml`, `RenameCfm.xaml`).
- `LibLayoutTests.cs` covers the Library window in both languages and themes at four work areas (keyboard focus, clipping). `ThemeContrastTests.cs` covers theme contrast.

Pass 3 and 4:

- `WindowsMatrixTests.cs` (child process): About, ClearLog, OrphanedCleanup, RedumpNotice, Rename, Options, VerificationResult and RedumpDetails, in Arabic RTL and English LTR, Light and Dark, at seven work areas that stand in for 100/125/150/200 percent on 1920x1040 and 100/150/200 percent on 1366x728 (224 cases). Per case: clipped buttons and text, enabled controls that cannot take keyboard focus, input controls with no accessible name, windows larger than the logical work area. Result: 0 findings.
- `ShellMatrixTests.cs` (child process): the real `MainWindow` against a temporary settings file, with the Loaded startup handler unhooked, in both languages and themes at five work areas, with 5,000 queue rows added through the row store. 0 layout findings; the DataGrid holds 5,000 items and realizes 7 rows, including after scrolling to the end.
- Pattern: no literal hex or named colours and no literal `FontSize`/`FontFamily` in `Views/` or `Ui/`; ComboBox suppresses the default focus rectangle but its template has an `IsKeyboardFocusWithin` trigger.
- **Not genuine DPI.** The work-area sizes emulate only the logical size. This machine has one monitor at 100 percent (96 DPI); changing the system scale would modify Windows settings and was not done. `VisualTreeHelper.SetRootDpi` was tried and discarded: it changes rendering scale but not the HWND size, so the window-size checks it produced were not trustworthy. Per-monitor DPI changes, monitor moves and mixed-DPI setups are therefore **not validated**.
- Not covered: hover/pressed/disabled states, high-contrast mode, contrast of rendered controls (only theme tokens are tested, by `ThemeContrastTests.cs`), screen-reader behaviour beyond automation names.

## E. Gates (serial)

- Previous pass recorded Debug and Release builds with 0 warnings/errors and 162 passed / 4 skipped. On 2026-10-11, both Debug and Release builds passed with 0 warnings/errors; both full harness runs completed **166 passed, 0 failed, 6 skipped** (172 total).
- Four skips are the symbolic-link creation cases (two intake, two library index); Windows denied the privilege. Two are ACL-denial fixtures; Windows denied `SetSecurityInfo`. Junction cases passed. Skips are not passes.
- Release security campaigns, output outside the repository: fuzz **1048/1048** (1,024 managed plus 24 external cases), corpus **7 files / 370043 bytes**, WPF shutdown **8 started / 4 cancelled / 1,393 ms**. Long soak and fastpath-proof were not run; fastpath-proof writes repository-local report/ledger files.
- `scripts/VerifyRepo.ps1`: passed. `scripts/PkgCleanGate.ps1`: passed for 585 tracked files; Release output validation was skipped. Since this checkout contains untracked production/test files, the tracked-file package check does not establish what a package containing those additions would include.
- `git diff --check` and `git diff --cached --check`: passed for the final Git-visible diff. Untracked files are not covered by these commands. No `add`, `commit`, `reset`, `stash`, `clean`, `restore`, `push`, `merge` or `rebase` operation was performed. Do not infer index-byte identity; the staged state is intentionally left as found.

## File coverage

| Depth | Paths |
| --- | --- |
| Full | `Core/Input/InputResolver.cs`, `Services/CleanupService.cs`, `Services/OrphanClean.cs`, `Services/OrphanScan.cs`, `Services/FilePathGate.cs`, `Core/Workflow/FlowOutClean.cs`, `Core/Workflow/FlowCleanup.cs`, `Core/Workflow/Paths/SafePathValid.cs`, `Core/Queue/QConcurrency.cs`, `Core/Queue/QueueStore.cs`, `Core/Queue/QueueRtState.cs`, `Core/Queue/QueueCtrl.cs`, `Core/Queue/QueueManager.cs`, `Core/Queue/ChdQueueItem.cs`, `QueueRun/RunCoord.cs`, `ViewModels/MainVM.cs`, `ViewModels/MainVM.Queue.cs`, `Services/RedumpV2/RedumpV2Policy.cs`, `Services/ChdCliRun.cs`, `Services/ChdProcRun.cs`, `ViewModels/Virtualization/VirtQueue.cs`, `ViewModels/Virtualization/QueueRowStore.cs`, `Core/Workflow/FlowSrcClean.cs`, `Core/Workflow/Extraction/ExtOutValid.cs`, `Core/Workflow/ArchivePrep.cs`, `Core/Workflow/Paths/PendingWsPath.cs`, `Core/Workflow/FlowExtract.cs`, `Core/Workflow/IChdFlowOrch.cs`, `Core/Workflow/FlowResult.cs`, `HakamiqChdTool.App.Tests/LibLayoutTests.cs`, `HakamiqChdTool.App.Tests/RedumpPolicyTests.cs` |
| Partial | `Services/Conversion/ChdResultMap.cs` (1-330), `Services/Conversion/ChdConvSvc.cs` (580-640), `Core/Workflow/ChdFlowOrch.cs` (95-235), `Core/Workflow/FlowOutPath.cs` (125-275), `Core/Workflow/FlowExtract.cs` (150-210, 425-450), `Core/Workflow/FlowConv.cs` (1005-1040), `Core/Workflow/FlowPreStage.cs` (120-185, 317-340), `Services/AppPaths.cs` (150-215, 355-410), `Services/RedSqlMgr.cs` (1-40, 225-455, 1145-1300 and the working-tree diff), `Services/RedumpV2/RedumpV2Engine.cs` (750-960, 1287-1325), `Services/RedumpSync.cs` (60-130, 225-710), `Core/Workflow/FlowOutPath.cs` (1-345), `Core/Workflow/FlowConv.cs` (125-175, 1215-1345), `Services/BinCueRescue/CueResWriter.cs` (520-640), `Core/Library/DatParser.cs` (XML reader settings), `Services/DeepHash.cs` (225-300), `Services/ZipExtract.cs` (725-825, 1030-1110), `ViewModels/MainVM.Intake.cs` (590-640), `ViewModels/LibraryAnalysisVM.cs` (1-100, 195-275, 438-745) |
| Pattern | `Core/**` reparse-point usage (grep), `Resources/EnglishStrings.xaml` and `ArabicStrings.xaml` key parity, `Loc*` key references in XAML and C#, literal text attributes in XAML |
| Pattern (delete sites, async void, blocking waits, hex colours, literal fonts) | all of `Core/`, `Services/`, `ViewModels/`, `Views/`, `Ui/` by search; delete sites additionally read in context (see Pass 3) |
| None | `ViewModels/Virtualization/QViewportSvc.cs`, `QueueRowData.cs`, `VisibleQueue.cs`, `ViewModels/Task*.cs`, `Core/Queue/QTransSvc.cs`, `QNotifyPub.cs`, `Core/Workflow/Extraction/**` (except the delete sites above), remaining `Services/**`, `Models/**`, remaining `Views/**`, `Resources/Themes/**`, `Resources/Tokens/**`, `Startup/**` |
