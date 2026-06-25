# chdman integration

Hakamiq CHD Tool uses chdman for CHD work.

The app is not a CHD encoder. It prepares the command, checks the surrounding files, runs chdman, reads progress, and turns the result into something useful on screen.

## What chdman handles

chdman handles the CHD format itself:

- create CHD files
- verify existing CHD files
- extract supported CHD files
- report tool-level errors and progress where available

Hakamiq CHD Tool handles the user workflow around that.

## What the app checks first

Before chdman starts, the app checks the parts that commonly waste time:

- source path and readability
- output path
- required CUE, GDI, or TOC track files
- CUE/BIN path safety
- archive staging
- CSO preparation
- free disk space
- existing output files

If a CUE points outside its folder, or a required track is missing, the app should stop before chdman runs.

## Commands used by the app

The app may prepare these chdman commands:

- `createcd`
- `createdvd`
- `verify`
- `extractcd`
- `extractdvd`
- `extracthd`
- `extractraw`

Normal users should not need to type any of them.

## Progress

When chdman prints progress, the app reads it.

For some extraction paths, direct progress is limited. In those cases the app can still track pending output growth so the queue does not look frozen.

## Cancellation

Cancelling a job should stop the running tool and mark the item as cancelled.

That result is kept separate from failure. The user asked to stop; that is not the same thing as a broken source.

## Bundled tools

Release packages may include chdman and related helper tools so users do not need to install them manually.

If a required tool is missing, blocked, or incompatible, the app should show a tool error instead of a vague conversion failure.

## Legal notices

When a release includes third-party tools, the matching license and notice files must be included in the package.

Release packaging rules are maintained in [CONTRIBUTING.md](../CONTRIBUTING.md).
