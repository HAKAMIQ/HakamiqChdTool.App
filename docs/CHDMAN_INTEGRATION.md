# chdman integration

Hakamiq CHD Tool does not implement a CHD encoder from scratch. It orchestrates MAME `chdman` from a Windows WPF application.

That choice is deliberate. `chdman` owns the CHD format behavior; the app focuses on intake, safety checks, queue handling, progress, logging, cancellation, and a cleaner desktop experience.

## What the app prepares

Before a job starts, the workflow resolves:

- the source path
- the output path
- the operation profile
- descriptor dependencies for CUE, GDI, and TOC
- archive staging when the input is a supported archive
- disk-space estimates
- source readability and path safety
- process and cancellation guards

Only then does the app start `chdman`.

## Commands used by the workflow

The current code paths recognize the main operations below:

- `createcd`
- `createdvd`
- `verify`
- `extractcd`
- `extractdvd`
- `extracthd`
- `extractraw`

The app does not expose every `chdman` switch. That is a feature, not a bug. A smaller, safer surface is easier to support.

## Progress and cancellation

`chdman` output is redirected and parsed for progress where possible. If a job is cancelled, the process runner tries to stop the process tree and waits for background output pumps to finish.

Failure and cancellation are kept separate. A cancelled job should not look like a corrupted conversion.

## Bundled tool behavior

The project may include `Tools/chdman.exe` for convenience. Release packages must include the required MAME license files and the chdman notice.

Do not run the app as Administrator unless you have a specific reason. Normal user permissions are the safer default.

## Missing or replaced chdman

If `chdman` is missing, blocked, or replaced with an incompatible binary, the app should fail clearly instead of starting a half-known conversion.

Use the bundled tool from the official release package when testing user-facing behavior.
