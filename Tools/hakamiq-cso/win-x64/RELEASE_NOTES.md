# CsoKit 0.6.1

CsoKit 0.6.1 is a security, reliability, and release-quality update for Windows x64.

## Main changes

- Restrict production native-library loading to the application directory.
- Validate native ABI compatibility before native codecs are used.
- Improve cancellation for verification, compression, decompression, repair, CLI Ctrl+C, and the desktop Stop action.
- Prevent incomplete output from replacing the final destination after cancellation or failure.
- Apply bounded compression-worker limits and safe queue sizing.
- Add a central Application layer shared by the CLI and desktop interface.
- Enforce output base names containing 2 to 10 Unicode characters.
- Improve early CLI validation and JSON failure responses.
- Strengthen verification and repair handling for supported containers.
- Repair the standalone published-executable smoke test and include it in the main verification gate.
- Simplify end-user documentation and remove internal reports.

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

1. Download csokit-0.6.1-win-x64.zip.
2. Extract the complete archive into one folder.
3. Keep csokit.exe beside CsoKit.Native.dll.
4. Run:

    .\csokit.exe native-info
    .\csokit.exe --help

## Verification

- Debug build: PASS.
- Release build: PASS.
- Automated tests: 201/201 PASS.
- Native integration: PASS.
- Published executable smoke: PASS.
- Native ISO to CSO to ISO round-trip: PASS.
- Release package verification: PASS.

## Important notes

- Existing output files are not overwritten unless --force is supplied.
- Repair cannot recreate unreadable or missing source data.
- Structural verification does not guarantee emulator or physical-device compatibility.
- Output base names must contain 2 to 10 Unicode characters; the extension is not counted.
