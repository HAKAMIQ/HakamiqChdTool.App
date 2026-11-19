# CsoKit 0.6.4

CsoKit 0.6.4 adds ZSO decompression and reads ZSO images in the layouts written by maxcso and ziso.py, for Windows x64.

## Main changes

- `decompress` accepts ZSO input and writes the original ISO, with the same output protection as CSO: no overwrite without `--force`, a disk space check, a temporary file moved into place on success, cancellation, and progress.
- ZSO reading (used by `decompress`, `verify`, and `repair`) accepts layouts that 0.6.3 rejected as corrupt:
  - ziso.py images larger than 2 GB, which pad between blocks with `X` and store the end-of-data index entry rounded down to the alignment.
  - maxcso images with blocks larger than 2048 bytes, whose final partial block is zero-filled and compressed as a full block. CSO2 LZ4 blocks get the same handling.
- Bytes after the last ZSO block are rejected, matching the CSO rule from 0.6.3.
- A CSO or ZSO header with an overflowing uncompressed size is reported as invalid instead of crashing `detect`, `decompress`, and `verify`.
- The desktop app source has tests for its view model, reports, and localization, and lists ZSO in decompression text.
- Release pages show only their own version's notes plus the ZIP's SHA-256. The repository adds `SECURITY.md`, `CONTRIBUTING.md`, a weekly CodeQL scan, and workflow actions pinned to exact commits.

## Supported workflows

- Detect ISO, CSO, ZSO, DAX, and supported CSO2 input.
- Inspect CSO1 and supported CSO2 input.
- Analyze PSP ISO structure.
- Verify compressed containers, including deep block verification and SHA-256.
- Compress ISO into CSO1.
- Decompress CSO or ZSO into ISO.
- Rebuild readable input into verified CSO1 output.
- Produce structured JSON output for scripts.

## Recommended usage

Recommended compression profile:

    .\csokit.exe compress ".\game.iso" --profile game-safe

Decompress a ZSO image:

    .\csokit.exe decompress ".\game.zso" -o ".\game.iso"

Verify important output before deleting the original image:

    .\csokit.exe verify ".\game.cso" --deep --sha256

## Installation

1. Download csokit-0.6.4-win-x64.zip.
2. Extract the complete archive into one folder.
3. Keep csokit.exe beside CsoKit.Native.dll.
4. Run:

    .\csokit.exe native-info
    .\csokit.exe --help

## Verification

Final release gate run against the release commit on 2026-11-19 with a real 364 MB PSP ISO:

- Debug build: PASS — 0 warnings, 0 errors.
- Release build: PASS — 0 warnings, 0 errors.
- Automated tests: 294/294 PASS (244 core, 50 desktop app).
- Native integration: PASS — native version 0.6.4 ABI 2 with zlib, libdeflate, Zopfli, and LZ4 decode available.
- Published executable smoke: PASS.
- Release package verification: PASS.
- SHA-256 manifest: 6 entries.
- NuGet Audit: not run; the gate restores with auditing disabled.
- Real ISO round trip: PASS — restored ISO SHA-256 matched the original for game-safe, compat, fast, and smallest, through both the build output and the published executable.

ZSO decompression, checked separately against the same ISO:

- ZSO files written by ziso.py, from the 364 MB ISO and from a 2.2 GB image (index alignment 1, `X` padding): restored ISO SHA-256 matched.
- ZSO files in the maxcso layout (2048 and 16384-byte blocks), produced with the reference LZ4 library: restored ISO SHA-256 matched. maxcso itself was not run.

## Important notes

- Existing output files are not overwritten unless --force is supplied.
- Repair cannot recreate unreadable or missing source data.
- Structural verification does not guarantee emulator or physical-device compatibility.
- Output base names must contain 2 to 10 Unicode characters; the extension is not counted.

CsoKit does not include games, BIOS files, firmware, keys, or disc images.

# CsoKit 0.6.3

CsoKit 0.6.3 is a decompression-hardening and release-gate reliability update for Windows x64.

## Main changes

- Decompression rejects stored blocks smaller than the block payload and CSO files whose final index offset does not match the file size.
- Decompression inflates blocks as exact raw deflate first, with zlib as an exact-length fallback that rejects truncated input, extra decoded data, and trailing data beyond index-alignment padding.
- Decompression feeds each compressed block to the inflater in bulk and only steps byte by byte through the permitted alignment padding, keeping throughput at the pre-validation level.
- Compression and decompression remove temporary output on unexpected errors, and verification reports overflowing header values as `InvalidHeaderValues`.
- Release gate scripts disable MSBuild node reuse so stale build nodes cannot fail gate builds with access-denied errors.

## Supported workflows

- Detect ISO, CSO, ZSO, DAX, and supported CSO2 input.
- Inspect CSO1 and supported CSO2 input.
- Analyze PSP ISO structure.
- Verify compressed containers, including deep block verification and SHA-256.
- Compress ISO into CSO1.
- Decompress CSO into ISO.
- Rebuild readable input into verified CSO1 output.
- Produce structured JSON output for scripts.

## Recommended usage

Recommended compression profile:

    .\csokit.exe compress ".\game.iso" --profile game-safe

Verify important output before deleting the original image:

    .\csokit.exe verify ".\game.cso" --deep --sha256

## Installation

1. Download csokit-0.6.3-win-x64.zip.
2. Extract the complete archive into one folder.
3. Keep csokit.exe beside CsoKit.Native.dll.
4. Run:

    .\csokit.exe native-info
    .\csokit.exe --help

## Verification

Final release gate run against the `v0.6.3` tag on 2026-11-19 with a real 364 MB PSP ISO:

- Debug build: PASS — 0 warnings, 0 errors.
- Release build: PASS — 0 warnings, 0 errors.
- Automated tests: 225/225 PASS.
- Native integration: PASS — native version 0.6.3 ABI 2 with zlib, libdeflate, Zopfli, and LZ4 decode available.
- Published executable smoke: PASS.
- Published native compress/decompress round trip: PASS.
- Release package verification: PASS.
- SHA-256 manifest: 6 entries.
- NuGet Audit: not run; the gate restores with auditing disabled.
- Real ISO round trip: PASS — restored ISO SHA-256 matched the original for game-safe, compat, fast, and smallest, through both the build output and the published executable. Published CSO size was 57.6%–57.9% of the ISO.

## Important notes

- Existing output files are not overwritten unless --force is supplied.
- Repair cannot recreate unreadable or missing source data.
- Structural verification does not guarantee emulator or physical-device compatibility.
- Output base names must contain 2 to 10 Unicode characters; the extension is not counted.

# CsoKit 0.6.2

CsoKit 0.6.2 is a maintenance and release-pipeline update for Windows x64.

## Main changes

- Strengthen release gates by validating central managed and native version wiring.
- Build, stage, and verify the native runtime used by release tests and published outputs.
- Remove a redundant native backend build from the GitHub release workflow while keeping the clean packaging build.
- Clarify supported inspection formats and CSO2 verification wording.
- Preserve existing CLI behavior, native staging, packaging, verification, and release artifacts.

## Supported workflows

- Detect ISO, CSO, ZSO, DAX, and supported CSO2 input.
- Inspect CSO1 and supported CSO2 input.
- Analyze PSP ISO structure.
- Verify compressed containers, including deep block verification and SHA-256.
- Compress ISO into CSO1.
- Decompress CSO into ISO.
- Rebuild readable input into verified CSO1 output.
- Produce structured JSON output for scripts.

## Recommended usage

Recommended compression profile:

    .\csokit.exe compress ".\game.iso" --profile game-safe

Verify important output before deleting the original image:

    .\csokit.exe verify ".\game.cso" --deep --sha256

## Installation

1. Download csokit-0.6.2-win-x64.zip.
2. Extract the complete archive into one folder.
3. Keep csokit.exe beside CsoKit.Native.dll.
4. Run:

    .\csokit.exe native-info
    .\csokit.exe --help

## Verification

- Debug build: PASS — 0 warnings, 0 errors.
- Release build: PASS — 0 warnings, 0 errors.
- Automated tests: 201/201 PASS.
- NuGet Audit: PASS.
- Native integration: PASS.
- Published executable smoke: PASS.
- Release package verification: PASS.
- SHA-256 manifest: 6/6 entries verified.
- Real ISO corpus gate: skipped for this release validation run.

## Important notes

- Existing output files are not overwritten unless --force is supplied.
- Repair cannot recreate unreadable or missing source data.
- Structural verification does not guarantee emulator or physical-device compatibility.
- Output base names must contain 2 to 10 Unicode characters; the extension is not counted.
