# Conversion options

Most users can add a file, choose an output folder, and start the
queue. The extra options exist for larger libraries, awkward media, and
recovery-style workflows.

Start simple: one file, one output folder, then verify the result.

## Convert

Convert creates a CHD from a supported source.

Common sources include:

- ISO
- CUE with referenced track files
- GDI with referenced track files
- staged archive images
- prepared CSO input

Before conversion starts, the app checks the source, resolves descriptor
dependencies, plans the output path, and prepares temporary work.

If something looks wrong, conversion should not start.

## Verify

Verify checks an existing CHD.

Use it before deleting a source image, moving a CHD to long-term
storage, or trusting a large queue result.

Verification is cheap compared with rebuilding bad output later.

## Extract

Extract is available only when the CHD type and selected extraction
profile are supported.

The app does not expose every chdman extraction path. That is
intentional. Some paths are easy to misuse, especially with console
disc images.

## Queue behavior

The queue keeps result states separate.

The important states are:

- success
- failed
- cancelled
- skipped existing output
- unsupported input

Do not flatten these into one generic result. Users need to know what
actually happened.

## Output names

The app tries to keep output names predictable.

Redump-assisted names may be displayed when local metadata is
available, but the release package does not include Redump databases.

Output naming should help the user, not hide what source was processed.

## Existing files

The app should not silently replace an existing output.

If the target file already exists, the workflow should either skip,
ask, or require an explicit overwrite path. Silent overwrite is not a
safe default.

## Temporary files

Temporary work belongs in controlled workspace folders.

After success, failure, or cancellation, cleanup should leave the
user-facing output folder understandable.

Temporary files should not become part of the user's library unless the
workflow explicitly says so.

## Practical recommendation

For normal use:

- convert one small source first
- verify the CHD
- confirm the output location
- then run a larger queue

That is slower at the beginning, but safer for a real library.
