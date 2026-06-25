# Conversion options

Most jobs are simple: add a file, choose the output folder, start the queue.

Use the extra controls only when you need them. Fewer moving parts makes the first result easier to trust.

## Convert

Convert creates a CHD from a supported source.

Common inputs:

- ISO
- CUE with its track files
- GDI with its track files
- a supported image inside ZIP, RAR, or 7Z
- CSO after it has been prepared as ISO

Before anything runs, the app checks the source — path length, readability, output location, required track files, and enough space for temporary work.

Most avoidable failures should be caught here, not halfway through a long conversion.

## Verify

Verify checks an existing CHD.

Use it before deleting the original source file or moving the CHD into your library. One verify pass is boring; losing a good dump is worse.

## Extract

Extract copies data out of a CHD when the CHD type is supported.

Common outputs:

- `extractcd` -> CUE/BIN
- `extractdvd` -> ISO
- `extracthd` -> IMG
- `extractraw` -> RAW

The app does not expose every chdman extraction option. It shows the paths it can name, check, and validate.

## Queue results

Queue results are kept separate:

- success
- failed
- cancelled
- skipped
- not supported

Cancelled is not failed. Unsupported is not the same as a broken file. The distinction is useful when you are processing a large folder.

## Output names

Output names should be predictable.

For extraction, the final output follows the CHD name and the expected extension. CUE/BIN bundles are treated as one disc output, not as random loose files.

If Redump data is available on your machine, the app may suggest a better name. The app does not include Redump files.

## Existing files

The app should not overwrite final output without a clear user choice.

If you are testing, use an empty output folder. Simple.

## Temporary files

Some jobs need temporary files, especially archive and CSO paths.

Temporary work should stay inside the app workspace and not appear in the final library. If a job fails, the final result should not be marked as complete unless the expected output exists.

## Suggested workflow

For a new source type:

1. Convert one small sample.
2. Verify the CHD.
3. Check the output name.
4. Run a larger queue only after that.

A slow, correct first pass beats cleaning up a bad batch later.
