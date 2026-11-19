# CsoKit

CsoKit is a Windows x64 command-line tool for PSP disc images. It detects, inspects, verifies, compresses, decompresses, and rebuilds ISO, CSO, and ZSO files.

Every operation checks its input first and writes output through a temporary file, so a failed or canceled run never leaves a partial image behind.

## Download

[Download the latest release](https://github.com/HAKAMIQ/CsoKit/releases/latest)

- `csokit-X.Y.Z-win-x64.zip` — self-contained, no .NET installation required.
- `SHA256SUMS.txt` — SHA-256 of every file inside the ZIP.

The release page also lists the ZIP's own SHA-256.

## Quick start

1. Extract the complete ZIP into one folder.
2. Keep `csokit.exe` and `CsoKit.Native.dll` together.
3. Open PowerShell in that folder and run:

        .\csokit.exe native-info
        .\csokit.exe --help

`native-info` should report that the native backend and codecs are available.

Step-by-step guide in English and Arabic: **[CsoKit Wiki](https://github.com/HAKAMIQ/CsoKit/wiki)**.

## Supported formats

| Operation | ISO | CSO1 | CSO2 | ZSO | DAX |
| --- | :---: | :---: | :---: | :---: | :---: |
| Detect | ✓ | ✓ | ✓ | ✓ | ✓ |
| Inspect header (`info`) | | ✓ | ✓ | | |
| Analyze PSP structure | ✓ | | | | |
| Verify header and index (`verify`) | | ✓ | ✓ | | |
| Verify every block (`verify --deep`) | | ✓ | ✓ | ✓ | ✓ |
| Decompress to ISO | | ✓ | ✓ | ✓ | ✓ |
| Repair or rebuild to CSO1 | ✓ | ✓ | ✓ | ✓ | ✓ |
| Compress to CSO1 | ✓ | | | | |

CSO, ZSO, and DAX input is read in the layouts written by maxcso and ziso.py, including images larger than 2 GB with large blocks or index alignment.

## Commands

Detect the input format:

    .\csokit.exe detect ".\game.iso"

Inspect a compressed image:

    .\csokit.exe info ".\game.cso"

Analyze a PSP ISO without changing it:

    .\csokit.exe analyze ".\game.iso" --psp

Verify a compressed image, every block, with SHA-256:

    .\csokit.exe verify ".\game.cso" --deep --sha256

Compress ISO to CSO with the recommended profile:

    .\csokit.exe compress ".\game.iso" -o ".\game.cso" --profile game-safe

Estimate the compressed size without creating a file:

    .\csokit.exe compress ".\game.iso" --measure

Decompress CSO, ZSO, or DAX to ISO:

    .\csokit.exe decompress ".\game.cso" -o ".\game.iso"
    .\csokit.exe decompress ".\game.zso" -o ".\game.iso"
    .\csokit.exe decompress ".\game.dax" -o ".\game.iso"

Repair or normalize into CSO1:

    .\csokit.exe repair ".\game.cso" -o ".\fixed.cso" --profile game-safe --deep-verify

Add `--json` to any command for structured output in scripts.

## Compression profiles

| Profile | Purpose |
| --- | --- |
| `game-safe` | Recommended default |
| `compat` | Compatibility-focused |
| `fast` | Faster compression |
| `smallest` | More compression trials |
| `archive-smallest` | Size-focused experimental profile |

Use `game-safe` unless you have a specific reason to choose another profile.

## Output files

- An existing output file is never overwritten unless `--force` is supplied.
- The output base name must contain 2 to 10 Unicode characters; the extension is not counted. `game.cso` and `back.iso` are valid; `x.cso` and `verylongname.cso` are not.
- Without `-o`, the output is named after the input in the same folder, without overwriting existing files.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | General failure |
| 2 | Invalid command or argument |
| 10 | Input file not found |
| 11 | Invalid container header |
| 12 | Unsupported container |
| 13 | Corrupt index |
| 14 | Output already exists |
| 15 | Output cannot be written |
| 16 | Insufficient disk space |
| 20 | Decompression failed |
| 21 | Compression failed |
| 130 | Operation canceled |

## Safety

- Keep the original image until the output is verified with `verify --deep`.
- Damaged input is rejected before output is written: bad headers, broken indexes, undersized blocks, and data after the last block.
- Repair rebuilds only readable data. It cannot recreate missing or corrupted sectors.
- Structural verification does not guarantee emulator or device compatibility.

## Limitations

- Windows x64 only.
- Compression writes CSO1 only.
- `CsoKit.Native.dll` must stay beside `csokit.exe`.

## Security

Every release is built by GitHub Actions from a signed tag, with a GitHub build provenance attestation for the ZIP and for `SHA256SUMS.txt`. To check a download:

    gh attestation verify .\csokit-X.Y.Z-win-x64.zip --repo HAKAMIQ/CsoKit

The binaries are not code-signed. If SmartScreen appears, check the SHA-256 first, then choose **More info** and **Run anyway**.

See `SECURITY.md` for vulnerability reporting.

## Legal

CsoKit does not include games, BIOS files, firmware, keys, or disc images.

Use CsoKit only with files you own or are legally authorized to process.

## Developers

Building, tests, release gates, and the desktop app preview are covered in `CONTRIBUTING.md` and `docs/`.

## License

CsoKit is proprietary software. See `LICENSE.txt`. Third-party components keep their own licenses; see `THIRD_PARTY_NOTICES.md`.
