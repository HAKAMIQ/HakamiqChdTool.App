# CHD Logical Reader Probe

Internal WPF-only helper used to enrich CHD metadata without changing the conversion workflow.

## Purpose

The application still uses `chdman` for conversion, verification, and extraction. The CHD logical reader is a secondary probe that reads CHD geometry as a logical byte space:

- physical CHD file size
- logical virtual disc size
- hunk size
- total hunk count
- decoded cache size reported by the helper
- compression ratio and estimated storage-saved ratio through `ChdInfoResult`

## Architecture

`Services/Chd/ChdLogicalProbeService.cs` executes `Tools/chd_reader_tool.exe info <file.chd>` with `UseShellExecute=false`, redirected output, argument-list escaping, cancellation support, and a short timeout.

`Services/Chd/ChdLogicalProbeResult.cs` carries the parsed geometry.

`Services/ChdInfoService.cs` uses the probe only as metadata enrichment after `chdman info`. If the probe is missing or fails, the existing CHD workflow continues through `chdman info`; no conversion, verification, queue, Redump, or cleanup behavior is changed.

## User-facing behavior

The verification/result dialog now adds a compact logical CHD report when a readable output CHD is available, or when the saved `info_*.log` already contains probe geometry. This is appended to the existing user-facing report without adding a new button or changing conversion, verification, queue, Redump, or cleanup behavior.

The helper must not expose internal runtime names or raw process failures to the end-user UI.
