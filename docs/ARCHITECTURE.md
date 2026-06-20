# Architecture

Hakamiq CHD Tool is a Windows WPF app for local CHD workflows.

The app does not write CHD files directly. CHD conversion, verification,
and extraction run through chdman.

The app is responsible for the workflow around that tool.

## Workflow

A normal job moves through these steps:

- input intake
- source checks
- output path planning
- command preparation
- external tool execution
- progress reporting
- result handling
- cleanup

The app should catch clear problems before starting a long job.

## Main layers

### UI

WPF windows, dialogs, layout, styling, and user interaction.

### ViewModels

Screen state, commands, queue display, options, and user-facing flow.

### Core

Workflow state, queue item lifecycle, path planning, result mapping, and
cleanup rules.

### Services

Local services handle conversion, verification, archive intake, storage
checks, process execution, metadata lookup, and helper discovery.

### Tools

Runtime tools are approved helpers used by the app.

chdman handles CHD operations. Archive tools handle archive staging.
Small helpers may inspect metadata or prepare supported input.

### Scripts

Scripts are for local verification, packaging, and repository checks.

Developer commands belong in CONTRIBUTING.md.

## chdman boundary

chdman owns CHD format behavior.

Hakamiq CHD Tool owns the desktop workflow around it. The app should not
expose every chdman switch by default. Normal users need clear and safe
paths, not every low-level option.

## Queue results

Queue states should stay specific:

- success
- failed
- cancelled
- skipped
- unsupported

These states should not be collapsed into one generic result.

## Temporary work

Temporary files are part of the workflow, not final user output.

They should use controlled locations and be cleaned after success,
failure, or cancellation.

Keeping temporary files should be an explicit developer choice, not the
normal user path.

## Helper policy

Helper tools are allowed when they solve a narrow local task.

They should be documented, packaged deliberately, and kept behind the
main WPF workflow.

The app remains C#, WPF, .NET 8, Windows x64, and chdman-based.

## Release packages

Release packages should contain the published app, approved runtime
helpers, and required legal notices.

Source files, scripts, CI folders, local artifacts, debug files, and
test material do not belong in user ZIP packages.
