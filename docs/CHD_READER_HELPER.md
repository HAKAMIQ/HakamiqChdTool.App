# CHD reader tool

The CHD reader tool only reads details from an existing CHD file.

It does not edit, repair, convert, verify, or extract CHD files. Those
actions still belong to chdman.

## What it shows

The tool can show simple CHD details, such as:

- CHD file size on disk
- size after opening
- block size
- block count
- compression type, when available

These details help the app show clearer file information.

## Size after opening

A CHD file can be smaller than the disc image it represents.

For example, a 3 GB CHD may open as a 7 GB disc image. The reader tool
can show both numbers so the user understands the file better.

## What it must not do

The reader tool must not change CHD files.

It must not:

- repair files
- rewrite files
- recompress files
- patch files
- change CHD content

Any tool that writes to CHD files must be reviewed separately.

## If the tool fails

If the reader tool is missing or fails, the app should show a clear
message.

Good messages are:

- CHD reader tool is missing
- CHD reader tool is blocked
- CHD type is not supported by the reader tool
- CHD details could not be read

The app should not say the CHD is bad unless there is real proof.

## Packaging

Public releases should include only approved tool files and required
license notices.

Tool source files should not be placed under docs.
