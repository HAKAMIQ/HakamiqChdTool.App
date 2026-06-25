# Architecture

Hakamiq CHD Tool is a Windows WPF app built with C# and .NET 8.

It is an orchestration layer around chdman, not a CHD encoder. The value is in the workflow: input checks, naming, queue behavior, progress, result validation, and release packaging.

## Current direction

The app stays on this path:

- Windows x64
- WPF
- C#
- .NET 8
- local processing on the user's machine
- chdman for CHD work

No Android port, no cloud conversion, no C++ rewrite of the WPF app.

## Normal job flow

A normal job moves through these steps:

1. Add a file or folder.
2. Identify the input.
3. Check source safety.
4. Plan the output path.
5. Prepare the chdman command or helper step.
6. Run the tool.
7. Track progress and errors.
8. Validate the expected output.
9. Clean temporary files.

The important part is not just running chdman. It is knowing when not to run it.

## Layers

### Views

WPF windows, dialogs, themes, and layout.

### ViewModels

Screen state, commands, visible queue rows, options, and user-triggered actions.

### QueueRun

Coordinates queue execution from the UI side. This area is sensitive: cancel, retry, and active job state should not be refactored casually.

### Core/Input

Input classification and intake rules. This is the closest part to pure core logic.

### Core/Workflow

Despite the folder name, this is currently an application workflow layer.

It plans and coordinates work, and it depends on Services. Treating it as pure domain core would be misleading. This decision is documented in [Architecture boundaries](architecture/ARCHITECTURE_BOUNDARIES.md).

### Services

Execution code: conversion, extraction, verification, file checks, archive handling, tool runners, and package support.

### Tools

External tools used by the app:

- chdman for CHD create/verify/extract
- 7-Zip support for archive paths
- CsoKit for CSO preparation
- CHD reader/helper tooling for CHD information paths

## Boundaries that should stay clear

User-facing README content should not drift into developer scripts.

Workflow safety should be tested before moving files between layers.

Media classification should not grow random input kinds for internal disc assets. Files such as `SYSTEM.CNF`, TIM, STR, VAG, MCR, MCD, GME, and PPF are not top-level media kinds.

## Temporary files

Temporary files are implementation detail.

They should stay in the workspace and should not appear in the user's final library. Final output is only final when the expected bundle exists.

## Release packages

Release ZIP files should include the app, required tools, and required legal notices.

They should not include source folders, scripts, CI files, debug symbols, test output, or local artifacts.

## Future refactoring

The safest future work is incremental:

1. Add tests around queue/cancel/retry before touching queue behavior.
2. Reduce direct UI dependence on execution details.
3. Move workflow boundaries only after tests prove the behavior.

Do not chase a cleaner folder tree at the cost of a less reliable converter.
