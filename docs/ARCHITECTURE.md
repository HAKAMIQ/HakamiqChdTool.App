# Architecture

Hakamiq CHD Tool is a Windows WPF app for managing local CHD
workflows. It does not write CHD files by itself. The CHD engine is
still chdman.

The app handles the workflow around chdman: input checks, output
planning, temporary paths, command execution, progress, cancellation,
and cleanup.

The goal is not to replace chdman. The goal is to make CHD work safer,
clearer, and easier for normal users.

## Workflow

A normal conversion moves through these stages:

- intake and classification
- source safety checks
- output path planning
- command preparation
- external tool execution
- progress reporting
- result mapping
- cleanup

Most problems should be caught before the external command starts. Bad
input should not turn into a questionable CHD halfway through a long
queue.

## Main parts

### Views and UI

WPF windows, dialogs, layout, styling, and user interaction.

### ViewModels

Screen state, commands, queue display, options, and user-facing flow.

### Core

Workflow state, item lifecycle, path planning, execution contracts,
cleanup, and result shaping.

### Services

Services handle conversion, verification, archive intake, chdman
process handling, storage checks, Redump display support, PS3 intake,
and helper tool discovery.

### Tools

Tools are approved runtime helpers bundled with the release package.

### Scripts

Scripts handle local checks, release packaging, package validation, and
repository convention checks.

Developer commands belong in CONTRIBUTING.md, not here.

## chdman boundary

chdman owns CHD format behavior. Hakamiq CHD Tool owns the workflow
around it.

The app should not expose every chdman switch just because chdman has
one. Normal users need safe paths, clear results, and fewer ways to
produce broken output.

## Queue model

Queue results must stay specific:

- Success means the operation finished and produced the expected result.
- Failed means a check, staging step, tool run, or post-process failed.
- Cancelled means the user stopped the item or queue.
- Skipped means the item was intentionally not processed.
- Unsupported means the input is outside the current workflow.

Do not collapse these into one generic state. Users need to know what
actually happened.

## Temporary work

Temporary files are part of the workflow, not final user output. They
should live in controlled locations, use predictable names, and be
cleaned after success, failure, or cancellation.

If temporary output needs to be kept for debugging, make that an
explicit developer path. It should not be normal user behavior.

## Helper tools

The app may use small helper tools for narrow jobs:

- chdman handles CHD conversion, verification, and extraction.
- archive backends handle archive staging.
- read-only CHD helpers can inspect metadata.
- CSO input may use a helper to prepare a temporary ISO.

These helpers are not the architecture. They are runtime dependencies
around the WPF workflow.

## CSO input

CSO handling is intentionally limited.

A CSO file is prepared as an input step, usually into a temporary ISO.
After that, the normal CHD workflow continues through chdman.

CsoKit is not a CHD encoder and not a replacement for chdman. Keep it
documented as a CSO input helper only.

## Native code policy

Native helpers are allowed only for narrow, approved jobs.

The app remains C# / WPF / .NET 8. CHD conversion remains chdman-based.
No C++ rewrite. No second architecture hiding beside the desktop app.

## Release packaging

Release packages should contain the published app, approved runtime
helpers, and legal notices.

Source files, scripts, CI folders, local artifacts, debug files, and
test material do not belong in user packages.

Packaging rules live in CONTRIBUTING.md. This page only explains the
application architecture.
