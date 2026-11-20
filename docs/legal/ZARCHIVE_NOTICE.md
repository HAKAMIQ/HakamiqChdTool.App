# ZArchive notice

CHD Tool includes the ZArchive tool so it can pack Xbox 360 games into a ZAR
file, which Xenia Canary can load.

## Included tool

- Tool: zarchive.exe
- Project: ZArchive by Exzap
- Version: 0.1.2
- License: MIT-0 (`Tools\zarchive\win-x64\LICENSE.txt`)
- Source: https://github.com/Exzap/ZArchive/tree/v0.1.2
- Path: Tools\zarchive\win-x64
- SHA-256: F9BBA11DF7EE120186E013214C14C210690F72AB2940ECC158FC40BF36E78B6C

## What it is used for

- pack an unpacked Xbox 360 game folder into a .zar file
- unpack that file again to check every file is unchanged

It never writes to or deletes the source. The app checks the SHA-256 above
before every run.