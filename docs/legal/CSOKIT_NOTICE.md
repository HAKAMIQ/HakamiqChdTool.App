# CsoKit notice

CHD Tool includes CsoKit so it can handle PSP CSO
files.

CsoKit is used before CHD conversion. It can read CSO file details,
check a CSO file, and make a temporary ISO from it.

After that, chdman creates the CHD.

## Included tool

- Tool: csokit.exe
- Native library: CsoKit.Native.dll (ABI 2)
- Project: CsoKit
- Version: 0.6.3
- Source: https://github.com/HAKAMIQ/CsoKit
- Reviewed commit: e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d
- Path: Tools\hakamiq-cso\win-x64

## What it is used for

- show CSO file details
- check the CSO file
- make a temporary ISO from the CSO file

## What it does not do

CsoKit does not create CHD files.

It does not replace chdman.

It does not include games, ROMs, BIOS files, keys, firmware, or private
user files.

## Required files

The CsoKit folder should include:

- csokit.exe
- CsoKit.Native.dll
- LICENSE.txt
- README.md
- RELEASE_NOTES.md
- SHA256SUMS.txt
- THIRD_PARTY_NOTICES.md

Keep all these files together in public releases. The application validates the
exact SHA-256 digests of both runtime binaries before executing the bundled tool.
