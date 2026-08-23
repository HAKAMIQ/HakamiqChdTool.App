# Security assurance status

Status date: 2026-08-18

This document separates implemented security controls from assurance activities that still require elapsed time, private test media, a production code-signing identity, or an independent reviewer.

## Implemented and automated

- Deterministic mutation campaigns exercise CHD headers, CUE descriptors, 7-Zip list output, malformed CSO containers, and malformed archive containers.
- The fuzz harness persists the active input before execution and preserves managed failures, native crashes, and timeouts for replay.
- A Windows security workflow runs bounded fuzz, synthetic corpus, WPF shutdown, and soak campaigns.
- The soak harness performs real CsoKit compression, application preprocessing, deep verification, and SHA-256 round-trip comparison while checkpointing memory and handle counts.
- The WPF shutdown campaign creates the real application resources and `MainWindow`, runs a busy queue with bundled CsoKit processes, closes the window, and verifies cancellation, quiescence, and process cleanup.
- The corpus runner accepts an external directory without copying source media into the repository. It bounds recursion, file count, total bytes, per-file time, captured output, and crash artifacts, and skips reparse points.
- Release manifest timestamps derive from `SOURCE_DATE_EPOCH` or the Git commit time so a manifest is stable for the same revision and inputs.
- `VerifyReproducibleBuild.ps1` performs two complete end-user publishes on the same Windows runner and compares output paths, sizes, and SHA-256 digests.
- `SignRelease.ps1` is fail-closed. It requires a real code-signing certificate, signs the application executable and DLL with SHA-256, requests an RFC 3161 SHA-256 timestamp, verifies signatures, and regenerates the release manifest.
- The secure release workflow verifies the requested version, requires the release tag to resolve to the commit being built, requires signing configuration for signed publishing, and generates provenance attestation for the final release artifact.

## Verified local evidence on 2026-08-18

The following evidence was produced from isolated clean-worktree verification during the security-assurance branch work:

- Application regression tests: 33/33 passed.
- Mutation fuzz smoke: 208/208 cases passed (200 managed cases and 8 external-tool cases).
- Synthetic corpus: 7 files processed; 370,043 aggregate input bytes.
- WPF busy-queue shutdown: 6 operations started, 2 pending operations cancelled, shutdown completed in 1,058 ms, with the campaign reporting success.
- Soak smoke: 3 iterations passed; peak private memory was 24,068,096 bytes and peak handle count was 268.
- Same-runner reproducible publish comparison: 50/50 output files matched by path, size, and SHA-256 digest.
- The Authenticode-required release gate correctly rejected an unsigned test release.
- Repository convention verification and `git diff --check` passed for the verified slices.

These are bounded smoke and regression results. They do not constitute a multi-hour soak result, a broad real-media corpus result, a production signed-release result, or an independent penetration test.

## Not yet completed

- No multi-hour or multi-day soak result has been completed and reviewed. `scripts/RunLongSoak.ps1` supports longer local campaigns.
- No broad private corpus of real commercial disc images has been supplied or executed. The repository intentionally contains no copyrighted corpus.
- No independent penetration test has been performed. The audit scope and evidence-bundle tooling prepare material for an external reviewer; they are not substitutes for independent review.
- No production Authenticode certificate has been supplied for the verified local run. The unsigned-gate rejection was tested, but a production signed artifact has not been demonstrated here.
- No independent reproducible-build result has been produced. The current reproducibility evidence is same-runner only.
- Provenance generation is implemented in the release workflow, but this document does not claim a completed external provenance or SLSA certification event unless a published workflow run and artifact are separately verified.

## Audit evidence handling

`scripts/NewIndependentAuditBundle.ps1` packages source from the immutable Git `HEAD`, not from untracked or modified working-tree files. This avoids accidentally including unrelated local files while allowing the bundle to identify the exact commit under review.

Security-campaign evidence under `TestResults\Security` is excluded by default because reports may contain relative media names, hashes, paths, or other operator-sensitive metadata. Use `-IncludeSecurityEvidence` only after reviewing the evidence for material that is appropriate to share with the reviewer.

## Required signed-release policy

A release may be described as signed only after all of the following pass for the same revision and release artifact:

1. Locked restore, Release build, and regression tests.
2. Same-runner reproducible publish comparison before signing.
3. Authenticode SHA-256 signing and RFC 3161 SHA-256 timestamp verification.
4. Signed release-manifest regeneration and `VerifyRelease.ps1 -RequireAuthenticode`.
5. Deterministic ZIP creation and checksum verification.
6. Provenance attestation generation for the final release artifact.

Independent penetration testing and independent reproduction remain separate external assurance activities.
