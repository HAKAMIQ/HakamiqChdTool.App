# xdvdfs notice

CHD Tool includes xdvdfs so it can convert Xbox games to XISO, the format xemu
reads, and unpack Xbox 360 games before ZAR packing.

## Included tool

- Tool: xdvdfs.exe (xdvdfs-cli)
- Project: xdvdfs by antangelo
- Version: 0.8.3
- License: MIT (`Tools\xdvdfs\win-x64\LICENSE.txt`)
- Source: https://github.com/antangelo/xdvdfs/tree/v0.8.3
- Path: Tools\xdvdfs\win-x64
- SHA-256: DF5F19954EF706C130C256546BF67CF7725611BDB30D1815577187007A6F907A

## What it is used for

- pack an Xbox ISO or game folder into an XISO
- check that the XISO holds the same files (`checksum`, `md5`)
- unpack an Xbox 360 image into a temporary folder for ZAR packing

It never writes to or deletes the source. The app checks the SHA-256 above
before every run.