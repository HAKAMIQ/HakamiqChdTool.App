# CsoKit notice

Hakamiq CHD Tool may include Hakamiq.CsoKit so it can handle PSP CSO
files.

CsoKit is used before CHD conversion. It can read CSO file details,
check a CSO file, and make a temporary ISO from it.

After that, chdman creates the CHD.

## Included tool

- Tool: hakamiq-cso.exe
- Project: Hakamiq.CsoKit
- Version: 0.4.0-beta.1
- Path: Tools\hakamiq-cso\win-x64\hakamiq-cso.exe

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

- LICENSE.txt
- README.md
- SHA256SUMS.txt

Keep these files next to hakamiq-cso.exe in public releases.
