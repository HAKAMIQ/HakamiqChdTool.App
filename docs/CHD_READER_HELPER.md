# CHD reader tool

The CHD reader tool only reads details from an existing CHD file.

It does not edit, repair, convert, check, or extract CHD files. Those
actions belong to chdman.

## What it shows

The tool can show:

- CHD file size on disk
- size after opening
- block size
- block count
- compression type, when available

These details help the app show clearer file information.

## Size after opening

A CHD file can be smaller than the disc image inside it.

For example, a 3 GB CHD may open as a 7 GB disc image. Showing both
numbers helps the user understand the file.

## What it must not do

The reader tool must not change CHD files.

It must not:

- repair files
- rewrite files
- recompress files
- patch files
- change CHD content

Any tool that changes CHD files must be handled separately.

## If the tool fails

If the reader tool is missing or fails, the app should show a clear
message.

Clear messages include:

- CHD reader tool is missing
- CHD reader tool is blocked
- CHD type is not supported
- CHD details could not be read

The app should not say the CHD is bad unless there is clear proof.

## Packaging

Public releases should include the needed tool files and license files.

Tool source files should not be placed under docs.
