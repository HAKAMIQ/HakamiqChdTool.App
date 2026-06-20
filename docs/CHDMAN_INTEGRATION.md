# chdman integration

Hakamiq CHD Tool does not implement a CHD encoder from scratch. It
orchestrates chdman from a Windows WPF application.

This split is deliberate. chdman owns CHD format behavior. The app owns
the workflow around it: intake, safety checks, queue state, progress,
logging, cancellation, and cleanup.

## What the app prepares

Before a job starts, the workflow resolves the details that users
should not have to manage by hand:

- source path
- output path
- operation profile
- descriptor dependencies for CUE, GDI, and TOC
- archive staging for supported archive input
- disk-space estimates
- source readability
- path safety
- process and cancellation guards

Only after those checks does the app start chdman.

## Supported operation paths

The app recognizes the main chdman operation paths used by the desktop
workflow:

- createcd
- createdvd
- verify
- extractcd
- extractdvd
- extracthd
- extractraw

The app does not expose every chdman switch. That is intentional. A
smaller surface is easier to test, document, and support.

## Progress and cancellation

chdman output is redirected and parsed for progress where possible.

If a job is cancelled, the process runner tries to stop the process
tree and waits for background output handling to finish. Cancellation
and failure must stay separate. A cancelled job should not look like a
corrupted conversion.

## Bundled tool behavior

Official release packages may include chdman for convenience. When
testing user-facing behavior, prefer the bundled tool from the official
package.

If chdman is missing, blocked, or replaced with an incompatible binary,
the app should fail clearly before running a half-known conversion.

## Permissions

Do not run the app as Administrator unless there is a specific reason.
Normal user permissions are the safer default for desktop conversion
workflows.

## Release requirements

When a release includes chdman, the package must include the matching
MAME license files and the chdman notice.

Release packaging is documented in CONTRIBUTING.md. This page only
describes how the app integrates with chdman.
