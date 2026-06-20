# Conversion options

Most users only need three steps:

- add a file
- choose an output folder
- start the queue

Use the extra options only when you need more control.

## Convert

Convert creates a CHD from a supported source.

Common sources are:

- ISO
- CUE with its track files
- GDI with its track files
- an image file inside ZIP, RAR, or 7Z
- CSO after it is prepared as ISO

Before conversion starts, the app checks the source file, output path,
free space, and required track files.

If something is missing or unclear, conversion should stop early.

## Verify

Verify checks an existing CHD.

Use it before deleting the original source file or moving the CHD into
your library.

## Extract

Extract copies data out of a CHD when the selected CHD type is supported.

The app does not show every chdman extract option. It only shows the
paths used by this app.

## Queue results

The queue should show clear results:

- success
- failed
- cancelled
- skipped
- not supported

These results should stay separate so the user can understand what
happened.

## Output names

Output names should be predictable.

If Redump data is available on the user's computer, the app may show a
better name. The app does not include Redump files.

## Existing files

The app should not replace an existing output file without a clear user
choice.

If the output file already exists, the app should skip it, ask the user,
or require an overwrite option.

## Temporary files

Some jobs need temporary files.

Temporary files should stay in the app work folder and should be cleaned
after the job ends.

They should not appear in the user's final library.

## Normal use

For normal use:

- test one small file first
- convert it
- verify the CHD
- check the output folder
- then run a larger queue
