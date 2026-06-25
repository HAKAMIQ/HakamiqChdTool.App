# Architecture boundaries

This document records the current architecture decision for Hakamiq CHD Tool.
It is a development guide. It does not change app behavior.

## Current decision

`Core/Workflow` is not treated as a pure domain core.

For now, `Core/Workflow` is treated as the app workflow layer. It coordinates
job steps, paths, checks, conversion stages, extraction stages, verification,
progress, cleanup, and final results.

This is intentional for the current codebase. Do not move files, rename
namespaces, or split service ports only to make the folder name look cleaner.
Those changes require a separate refactor stage and broader tests.

## Layer meaning

### Core/Input

`Core/Input` contains the top-level media input path. It may classify supported
inputs such as folders, ISO, PKG, CHD, CSO, CUE, BIN, GDI, unknown files, and
other files.

Do not add input kinds for loose PSX internal files or assets.

Examples that should stay `Other` or `Unknown` at the top-level intake path:

- `SYSTEM.CNF`
- `.TIM`
- `.STR`
- `.VAG`
- `.MCR`
- `.MCD`
- `.GME`
- `.PPF`

This rule only applies to top-level intake classification. It does not remove
specialized scanners that read files inside disc images, such as the existing
PS2 advisory scanner.

### Core/Queue

`Core/Queue` holds queue state, queue item data, transitions, and queue control
rules.

Do not change queue behavior as part of architecture cleanup. Changes to run,
cancel, retry, skip, and state transitions need their own stage and tests.

### Core/Workflow

`Core/Workflow` is the current application workflow layer.

It may coordinate services and app steps. It is allowed to know about job
stages, prepared inputs, output paths, work folders, extraction results,
verification results, cleanup, and user-facing job results.

Do not treat this folder as a pure domain boundary in the current app.

### Services

`Services` contains concrete app tasks, external tool calls, conversion support,
file checks, archive support, advisory services, and app-specific helpers.

Services may use core contracts and workflow data. They should not introduce new
user-visible behavior without tests.

### ViewModels and Views

`ViewModels` and `Views` are the WPF UI layer.

Do not move queue, run, cancel, or window behavior during P3-B or P3-C1.
UI and composition cleanup belongs to a later stage.

## Work rules

These rules apply to architecture work after P3-B:

1. Do not add features because of PSXSPX or no$psx references.
   Those references are useful only for future CUE, BIN, ISO, CHD guidance.

2. Do not add `MediaInputKind` values for PSX internal files or loose assets.

3. Do not mix top-level input classification with specialized disc scanners.

4. Do not add Python, `python.exe`, or a parser outside .NET.

5. Do not add zstd or cdzs UI options until backend support and compatibility
   policy are planned. They must not become the default by accident.

6. Do not change queue behavior during architecture documentation or test work.

7. Do not change conversion defaults without a separate stage and tests.

8. Do not do micro-cleanup. A patch must add clear architecture value, tests, or
   release safety.

9. P3-B is test-only. It must not change behavior, UI, or conversion logic.

10. P3-C does not split `Core/Workflow` until the tests are broad enough and the
    refactor target is explicit.

11. Do not push or release unless the maintainer explicitly asks for it.

## P3-C1 scope

P3-C1 is documentation only.

Allowed:

- document current boundaries
- document current risk areas
- document what not to change yet
- document the rules for future architecture work

Not allowed:

- moving files
- renaming namespaces
- adding new ports or interfaces
- changing queue behavior
- changing conversion defaults
- changing UI behavior
- adding PSX asset parsing
- adding Python
- adding zstd or cdzs UI options

## Future refactor rule

A future refactor may split ports or move workflow code only after the target is
small and test-covered.

Good future candidates:

- output path planning
- safety path validation
- CUE and BIN safety checks
- workflow profile planning
- command preparation boundaries

Bad candidates for a first refactor:

- moving all `Core/Workflow` at once
- changing queue run behavior together with architecture cleanup
- changing UI and services in the same patch
- adding new media kinds while moving architecture boundaries

## Current position

The current architecture is acceptable for the app as long as these boundaries
are understood:

- `Core/Input` is the top-level intake area.
- `Core/Workflow` is the app workflow layer.
- `Services` performs concrete app work.
- `ViewModels` and `Views` stay UI-focused.
- queue behavior and conversion defaults stay stable unless a separate stage
  explicitly changes them.
