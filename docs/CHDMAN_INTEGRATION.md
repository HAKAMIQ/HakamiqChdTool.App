# chdman integration

Hakamiq CHD Tool uses chdman for CHD work.

chdman creates, checks, and extracts CHD files. Hakamiq CHD Tool prepares
the command, runs chdman, shows progress, and shows the final result.

The app does not replace chdman.

## What chdman does

The app uses chdman to:

- create CHD files
- check existing CHD files
- extract supported CHD files
- report progress and tool errors

## What the app checks first

Before running chdman, the app checks:

- source file path
- output file path
- required CUE, GDI, or TOC track files
- supported archive files
- supported CSO files
- free disk space

If a required file is missing, the app should stop before chdman starts.

## Common chdman commands

The app may use these chdman commands:

- createcd
- createdvd
- verify
- extractcd
- extractdvd
- extracthd
- extractraw

The app does not need to show every chdman option.

## Progress and cancel

The app reads chdman output when progress is available.

If the user cancels a job, the app should stop chdman and show the job
as cancelled, not failed.

## Included chdman

A release package may include chdman so users do not need to install it.

If chdman is missing, blocked, or incompatible, the app should show a
clear tool error.

## License files

If chdman is included, the release package must include the required
MAME license and notice files.

Release package details are in CONTRIBUTING.md.
