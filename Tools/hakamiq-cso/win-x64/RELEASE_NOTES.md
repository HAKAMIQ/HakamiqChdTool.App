# CsoKit 0.6.6

## What's new

- Decompress CSO2 and DAX to ISO, not only CSO and ZSO.
- CSO, CSO2, and DAX files made by maxcso for images of 2 GB and more (for example PS2 games) are no longer reported as damaged ("Re-dump required") by `verify --deep`, `decompress`, and `repair`.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.5

## What's new

- Numbers print the same on every Windows region. On Arabic Windows, ratios no longer show as `??`.
- `compress`, `--measure`, and `repair` warn when `CsoKit.Native.dll` is missing instead of silently making larger files.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.4

## What's new

- Decompress ZSO to ISO: `csokit decompress game.zso -o game.iso`.
- ZSO files made by ziso.py larger than 2 GB, and by maxcso with large blocks, are no longer rejected as corrupt by `verify --deep` and `repair`.
- A file with a damaged header gets an error message instead of crashing `detect`, `decompress`, or `verify`.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.3

## What's new

- Decompression rejects damaged CSO files instead of writing a wrong ISO: data after the last block, a wrong final index offset, undersized stored blocks, or blocks that do not decode to their exact size.
- A failed compression or decompression no longer leaves temporary files behind.
- `verify` reports impossible header values as `InvalidHeaderValues`.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.2

## What's new

- No changes to commands or output. Release packaging and verification were tightened.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.1

## What's new

- Ctrl+C cancels verify, compress, decompress, and repair cleanly. An incomplete output never replaces the destination file.
- Output base names must have 2 to 10 characters; the extension is not counted.
- The native library is loaded only from the program folder.
- Clearer errors for invalid arguments, including in `--json` output.

## How to update

Extract the new ZIP over the old folder. Keep `csokit.exe` and `CsoKit.Native.dll` together.

# CsoKit 0.6.0

## What's new

- Deep verification of raw ISO files, and detailed CSO verification results.
- Repair tells apart a plain rebuild, a repaired file, and a file that must be re-dumped.
