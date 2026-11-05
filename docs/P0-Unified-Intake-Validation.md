# P0 — Unified Intake Path Validation

Date: 2026-11-01
Project: HakamiqChdTool.App
Platform: Windows / WPF / C# / .NET 10

## Status

Functional Validation: ACCEPTED
Full Security Validation: PENDING

## Required Components

- MediaInputDescriptor
- MediaInputKind
- IMediaInputClassifier
- MediaInputClassifier
- IMediaInputPipeline
- MediaInputPipeline

## Supported Input Kinds

Folder, ISO, PKG, CHD, CSO, CUE, BIN, GDI, Unknown, Other.

## Graphify Evidence

- Version: 0.9.99
- Code files: 606
- Nodes: 8788
- Edges: 23637
- Communities: 348

## Build Results

- Application: PASS
- Test Harness: PASS
- Target Framework: net10.0-windows10.0.17763.0

## Regression Tests

- PASS: 191
- SKIP: 4
- FAIL: 0
- TOTAL: 195
- Exit Code: 0

## Intake Safety

- Read-only header probing confirmed.
- Maximum probe allocation: 4096 bytes.
- CHD header read: up to 124 bytes.
- CSO and PKG magic checks: 4 bytes.
- Cancellation behavior tested.
- Queue Intake rejects blocked decisions.
- Fast Intake respects pipeline decisions.
- BIN-to-CUE redirection reclassifies the target.

## Security Exceptions

Four symbolic-link tests were skipped because Windows denied
symbolic-link creation privileges.

These tests are NOT counted as PASS.

The path-validation-to-open TOCTOU limitation remains documented.

## Change Restrictions

No changes to:
- chdman
- codecs
- queue or workflow behavior
- defaults
- WPF UI or UX
- internal resolution

## Final Decision

P0 functional validation is accepted based on the tested paths.

Full security sign-off remains pending until symbolic-link
tests are executed successfully and remaining risks are reviewed.

No source changes are required by this report.