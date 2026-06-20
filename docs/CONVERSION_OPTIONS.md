# Conversion options

Most users can add a file, pick an output folder, and start the queue. The extra options are there for awkward media, large libraries, or recovery work.

Start simple. One file, one output folder, verify the result.

## Convert

Convert creates a CHD from a supported source such as ISO, CUE, GDI, or a staged archive image.

Before conversion starts, the app resolves descriptor dependencies, checks source readability, plans the output path, and prepares the temporary workspace. If something looks wrong, conversion does not start.

## Verify

Verification checks an existing CHD. Use it before deleting a source image or moving a CHD into long-term storage.

Good habit. Cheap check.

## Extract

Extraction runs only when the CHD type and extraction profile are supported. The app does not expose every possible `chdman` path because not every path is safe for normal users.

## Queue behavior

The queue keeps success, failure, cancellation, skipped-existing, and unsupported input separate. Do not flatten those results; users need to know what actually happened.

## Output names

The app tries to keep output names predictable. Redump-assisted names may be displayed when local metadata is available, but the tool does not include Redump databases.

## Existing files

The app should not silently replace an existing output. If you want to overwrite, make that decision explicitly.

## Temporary files

Temporary work belongs in controlled workspace folders. After success, failure, or cancellation, cleanup should leave the user-facing output folder understandable.
