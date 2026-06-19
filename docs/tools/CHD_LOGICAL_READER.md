# CHD logical reader probe

The logical reader is an internal WPF-only helper used to enrich CHD metadata without changing the conversion workflow.

## Purpose

The application still uses `chdman` for conversion, verification, and extraction. The logical reader is a secondary read-only probe for CHD geometry:

- physical CHD file size
- logical virtual disc size
- hunk size
- total hunk count
- decoded cache size reported by the helper
- compression ratio and estimated storage-saved ratio through `ChdInfoResult`

## Architecture

`Services/Chd/ChdLogicalProbeService.cs` looks for:

```text
Tools\chd_reader_tool.exe info <file.chd>
```

The process uses `UseShellExecute=false`, redirected output, argument-list escaping, cancellation support, and a short timeout.

`Services/Chd/ChdLogicalProbeResult.cs` carries the parsed geometry.

`Services/ChdInfoService.cs` uses the probe only as metadata enrichment after `chdman info`. If the probe is missing or fails, the existing CHD workflow continues through `chdman info` where possible.

## Release gate

Current end-user release checks block `chd_reader_tool.exe`, `libchdr.dll`, and related native inspection artifacts from the public package. Keep that behavior unless a future release explicitly approves shipping the helper and its full third-party notices.

No conversion, verification, queue, Redump, or cleanup behavior should depend on this helper as the only path.

## User-facing behavior

The verification/result dialog can append a compact logical CHD report when probe data is available. If not, the UI should stay quiet and rely on the existing `chdman` path.

The helper must not expose raw internal process failures to the user-facing UI.
