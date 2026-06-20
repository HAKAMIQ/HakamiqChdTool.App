# CHD reader helper

The CHD reader helper is a read-only metadata probe. It is not the
conversion engine and it does not replace chdman.

Its job is narrow: give the app better information about an existing
CHD without changing that CHD.

## Purpose

The helper can inspect CHD structure and report values that are useful
for UI display, diagnostics, and safer workflow decisions.

Typical values include:

- physical file size
- logical virtual size
- hunk size
- hunk count
- compression hints
- read-only metadata

This information helps the app show clearer details without asking the
user to run low-level tools manually.

## Boundary

Conversion, verification, and extraction still belong to chdman.

The reader helper should not decide final conversion behavior by itself.
It can support the workflow, but it should not become a second CHD
engine hidden inside the app.

If the helper is missing or fails, the app should continue where it can
safely continue. A metadata probe failure should not be treated as a
conversion failure unless the selected action depends on that metadata.

## Logical size

Logical size is the virtual size represented by the CHD.

It is not the same as the physical size of the `.chd` file on disk. A
small physical CHD can still represent a much larger source image.

This matters for:

- progress display
- storage estimates
- verify and extract planning
- user-facing file details

## Read-only rule

The helper must stay read-only.

It should not repair, rewrite, recompress, patch, normalize, or mutate
CHD content. Any write path belongs to a separate reviewed workflow.

Keep this line clear. Read-only helpers are easier to trust, test, and
package.

## Failure behavior

A helper failure should be reported as helper failure, not as corrupt
media unless there is direct evidence.

The app should prefer clear messages:

- helper missing
- helper blocked
- helper unsupported
- metadata read failed
- CHD action cannot continue without metadata

Avoid vague errors when the problem is only a missing helper.

## Packaging

Public release packages should include only approved helper binaries and
their matching legal notices.

Source files for helper experiments do not belong under `docs/`.
Implementation source should live in a real source or tool location, or
not be shipped at all.

Release packaging rules live in CONTRIBUTING.md. This page only
describes the helper boundary.
