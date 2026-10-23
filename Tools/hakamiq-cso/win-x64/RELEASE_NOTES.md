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

- Final release gate: pending; results will be recorded after it runs for 0.6.3.

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
