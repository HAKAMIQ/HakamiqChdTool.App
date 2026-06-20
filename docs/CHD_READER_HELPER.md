# CHD reader helper

The CHD reader helper is a read-only metadata tool.

It reads information from an existing CHD. It does not convert, verify,
extract, repair, or replace chdman.

## Purpose

The helper gives the app useful details about a CHD without changing the
file.

Typical details include:

- physical file size
- logical size
- hunk size
- hunk count
- compression hints
- basic read-only metadata

The app can use this information for display, diagnostics, and safer
workflow decisions.

## Logical size

Logical size is the virtual size represented by the CHD.

It is not the same as the physical size of the .chd file on disk. A CHD
file can be small while representing a much larger source image.

## Boundary

CHD conversion, verification, and extraction still belong to chdman.

The helper should support the workflow, not become a second CHD engine
inside the app.

If the helper is missing or fails, the app should continue where it can
safely continue. Only actions that require that metadata should stop.

## Read-only rule

The helper must stay read-only.

It should not repair, rewrite, recompress, patch, normalize, or change
CHD content.

Any write behavior belongs in a separate reviewed workflow.

## Failure behavior

A helper failure should be reported as a helper problem unless there is
direct evidence that the CHD itself is bad.

Clear examples are:

- helper missing
- helper blocked
- helper unsupported
- metadata read failed
- action requires metadata

Avoid vague errors when the problem is only the helper.

## Packaging

Public releases should include only approved helper binaries and their
required notices.

Helper source files do not belong under docs. Implementation files
should live in a real source or tool location.
