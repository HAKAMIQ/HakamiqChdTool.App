# Security Boundaries

This document records the architectural trust boundaries that must remain explicit
as the application evolves.

## Dependency direction

The intended dependency direction is:

`Views -> ViewModels -> QueueRun/Core workflows -> Services -> external tools and storage`

The repository verification gate enforces these rules:

- `Models` do not depend on UI, workflow-shell, or infrastructure namespaces.
- `Core` does not depend on Views, ViewModels, UI shell, QueueRun, or Startup.
- `Services` does not depend on Views, ViewModels, UI shell, QueueRun, or Startup.
- `ViewModels` do not depend directly on Views.

`Core -> Services` remains a documented transitional dependency. Its reference
count is capped by the repository gate and should be reduced incrementally by
moving contracts and neutral DTOs inward, without changing queue behavior.

## Trust boundaries

### User-selected input

File paths, archive entries, CUE references, and file names are untrusted. They
must be normalized and checked for containment and reparse-point traversal before
read, extraction, move, overwrite, or deletion.

CHD and CSO inputs that require header evidence are fail-closed. Wrong magic,
truncated headers, unsupported CHD versions, invalid CHD header lengths, and
unavailable probes must not be routed to conversion based on extension alone.

Archive processing is bounded by entry-count, captured-output, expanded-byte,
and free-space-reserve policies. These limits are checked before extraction,
during streamed or external extraction, and again before the output is accepted.
Disk extraction uses the pinned 7-Zip path only. SharpCompress remains a
read-only integrity fallback; if 7-Zip is unavailable or fails integrity checks,
extraction fails closed instead of switching to a weaker writer.

### Embedded and external tools

`chdman`, 7-Zip, and helper tools are process boundaries. Arguments must use
`ProcessStartInfo.ArgumentList` with `UseShellExecute = false`. The runtime copy
of embedded `chdman` must be inside the private session directory, must not
traverse a reparse point, and must match the embedded resource's SHA-256 digest
immediately before its path is returned for execution.

The bundled MAME 0.289 `chdman` requires x86-64-v2. Runtime initialization checks
the required CPUID feature bits before extracting or returning the executable.

### Network data

Update metadata and Redump catalogs are untrusted remote data. Downloads require
HTTPS, bounded size and time, validation before activation, and atomic replacement
of the last-known-good local copy.

Redump synchronization disables automatic redirects. It follows at most five
redirect hops and validates HTTPS, user-info, host, port, and redirect cycles
before every request is sent. It also enforces download and ZIP budgets and
rebuilds the SQLite catalog in a single transaction so a failed import cannot
activate a partial catalog.

### Local state

Configuration, caches, runtime tools, logs, and SQLite data are mutable local
state. Code must tolerate malformed data and must not treat local state as an
authorization signal.

## Cryptographic intent

SHA-256 is the minimum digest for application security and integrity decisions.
MD5 and SHA-1 values may remain only where they are identifiers required for
Redump catalog compatibility; they must never authorize execution or establish
trust.

## Maintenance priorities

1. Reduce `Core -> Services` dependencies by extracting contracts at stable seams.
2. Consolidate duplicated path-containment and reparse-point checks behind the
   existing safe-path policy in small, tested changes.
3. Keep the .NET LTS patch, direct NuGet dependencies, and bundled command-line
   tools current; run restore auditing and the full release gate after upgrades.
4. Keep the executable negative tests for archive resource-monitor failure,
   7-Zip output flooding, redirect validation, SQLite rollback, shutdown timeout,
   CUE path escape, and runtime-tool tampering whenever those seams change.
5. Keep `packages.lock.json` and the CycloneDX 1.7 SBOM synchronized; CI restores
   in locked mode and rejects a stale generated SBOM.
