# chdman integration

Hakamiq CHD Tool uses chdman for CHD work.

chdman handles the CHD format. The app prepares the job, starts the
tool, reads its output, and reports the result to the user.

## What chdman does

chdman is used for:

- creating CHD files
- verifying existing CHD files
- extracting supported CHD types
- reporting tool-level errors and progress

The app does not replace chdman.

## What the app prepares

Before chdman starts, the app prepares the workflow.

This includes:

- checking the source path
- checking the output path
- resolving CUE, GDI, and TOC track files
- staging supported archive input
- preparing supported CSO input
- checking available disk space
- building the command line
- setting up progress and cancellation handling

If these checks fail, the app should stop before running chdman.

## Operations

The app uses the chdman operations needed by the desktop workflow.

Common operation paths include:

- createcd
- createdvd
- verify
- extractcd
- extractdvd
- extracthd
- extractraw

The app does not need to expose every chdman switch. The goal is to keep
normal workflows clear and predictable.

## Progress and cancellation

The app reads chdman output when progress is available.

When a job is cancelled, the app should stop the running process and
report the job as cancelled, not failed.

## Bundled tool

Release packages may include chdman so users do not need to install it
separately.

If chdman is missing, blocked, or incompatible, the app should show a
clear tool error.

## Release notes

When chdman is bundled, the release package must include the required
MAME license and notice files.

Packaging details belong in [CONTRIBUTING.md](../CONTRIBUTING.md).
