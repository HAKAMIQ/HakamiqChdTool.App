# CHD reader helper

The CHD reader helper is a read-only metadata probe. It is not the
conversion engine and it does not replace chdman.

The helper exists to give the app better information about an existing
CHD without mutating it.

## Purpose

The helper can inspect CHD structure and report values that are useful
for UI display, diagnostics, and safer workflow decisions.

Typical fields include physical file size, logical virtual size, hunk
size, hunk count, compression hints, and read-only metadata.

## Boundary

Conversion, verification, and extraction still belong to chdman.

If the reader helper is missing or fails, the app should continue where
the workflow can safely continue. A metadata probe failure should not be
treated as a conversion engine failure unless the selected action
depends on that metadata.

## Logical size

The logical size is the virtual size represented by the CHD. It is not
the same as the physical size of the `.chd` file on disk.

This matters for progress, storage estimates, and user-facing details.
A small physical CHD can still represent a much larger source image.

## Read-only rule

The helper must stay read-only.

It should not repair, rewrite, recompress, patch, or normalize CHD
content. Any write path belongs to a separate, reviewed workflow.

## Packaging

Public release packages should include only approved helper binaries and
their matching legal notices.

Source files for helper experiments do not belong under `docs/`.
Implementation source should live in a real source/tool location, or not
be shipped at all.
