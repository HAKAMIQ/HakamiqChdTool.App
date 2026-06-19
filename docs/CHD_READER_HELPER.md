# CHD reader helper

The source tree can contain `chd_reader_tool.exe` and its C++ source as an internal CHD metadata probe.

It is not a replacement for `chdman`, and it is not the main conversion engine. Think of it as a read-only helper used during development or approved diagnostic builds.

## Current release policy

The current end-user release gate blocks native CHD inspection artifacts such as `chd_reader_tool.exe` and `libchdr` binaries. If the helper is absent, the normal `chdman` workflow should continue where possible.

That keeps the public package smaller and avoids shipping extra native inspection code unless it is explicitly approved.

## What the helper can provide

Depending on the build and workflow, the helper may provide:

- physical CHD file size
- logical virtual size
- hunk size
- hunk count
- decoded-cache information reported by the helper
- extra data for the verification/result dialog

## Runtime policy

The helper must stay read-only. No conversion, queue, Redump, cleanup, or extraction behavior should depend on it as the only source of truth.

For current implementation notes, see [tools/CHD_LOGICAL_READER.md](tools/CHD_LOGICAL_READER.md).
