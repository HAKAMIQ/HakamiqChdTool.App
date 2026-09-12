# Independent security audit scope

## Independence requirement

The reviewer must not be the project author, a contributor to the reviewed revision, or the operator who prepared the audit evidence. Automated scans and internal code reviews may supplement the engagement but do not satisfy the independent-review requirement by themselves.

## In-scope trust boundaries

1. Archive discovery, listing, extraction, path containment, reparse-point handling, resource limits, process termination, and cleanup.
2. CHD intake classification, `chdman` path resolution, embedded-tool extraction, hash pinning, command construction, cancellation, output bounds, verification, and promotion of final files.
3. CSO/ZSO/DAX intake and the bundled CsoKit executable/native DLL trust chain, version contract, hash pinning, temporary workspace, decompression, and failure cleanup.
4. Redump HTTPS redirects, host allow-listing, download limits, archive validation, DAT parsing, SQLite transactions, rollback, and cancellation.
5. Queue concurrency, shutdown timeouts, WPF closing, late tasks, child processes, workspace ownership, and deletion boundaries.
6. Update and external-link flows, settings parsing, logging, secrets exposure, local privilege assumptions, and single-instance behavior.
7. Build scripts, locked dependencies, SBOM, release manifest, Authenticode signing, deterministic packaging, provenance attestation, and GitHub Actions permissions.

## Required attack classes

- ZIP Slip, absolute paths, alternate separators, duplicate entries, ADS/device names, decompression bombs, oversized metadata, nested archives, junction/reparse swaps, and time-of-check/time-of-use races.
- Malformed/truncated CHD versions 3/4/5, unsupported versions, malicious metadata, invalid hunk maps, parent references, codec failures, and tool-output floods.
- Malformed CSO/ZSO/DAX headers and indexes, integer overflow, overlapping blocks, truncated blocks, decompression bombs, native crashes, and output-path races.
- SSRF through every redirect hop, scheme/port/user-info confusion, DNS rebinding assumptions, redirect loops, partial downloads, malicious DAT/ZIP content, and rollback interruption.
- Cancellation and process-tree termination during every external-tool stage, shutdown while workers are active, resource disposal races, and cleanup crossing ownership boundaries.
- Dependency substitution, compromised build runner, manifest or SBOM mismatch, unsigned or incorrectly timestamped binaries, tag/revision mismatch, and provenance verification failure.

## Deliverables

- Reviewed commit SHA and artifact hashes.
- Evidence that the reviewed source bundle was captured from that immutable commit rather than from unrelated working-tree or untracked files.
- Test environment, tools, versions, configuration, and dates.
- Reproduction steps and minimal non-copyrighted proof inputs for every finding.
- Severity, exploit preconditions, affected boundary, impact, and remediation guidance.
- Explicit statement of untested areas and time limitations.
- Retest report that maps each accepted fix to a commit and test result.

## Exit criteria

The independent audit is complete only when a named external reviewer delivers a signed or otherwise attributable report for an immutable commit and final artifact. Creating an audit bundle or passing the internal campaigns does not change audit status to complete.
