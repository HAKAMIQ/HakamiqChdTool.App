# Hakamiq CsoKit

Hakamiq CsoKit is a command-line tool for working with PSP CSO files.

It can:

* Show CSO file information.
* Check whether a CSO file structure looks valid.
* Decompress CSO v1 files back to ISO.
* Show progress while decompressing.
* Stop safely when you press Ctrl+C.
* Output JSON for automation or integration.

## Download and run

Download the latest `hakamiq-csokit-*-win-x64.zip` from the Releases page.

Extract the ZIP file to any folder. The extracted folder contains:

```text
hakamiq-cso.exe
README.md
LICENSE.txt
SHA256SUMS.txt
```

This is a command-line tool. Do not run `hakamiq-cso.exe` by double-clicking it.

Open PowerShell inside the extracted folder, then run:

```powershell
.\hakamiq-cso.exe --help
```

## Commands

Show CSO information:

```powershell
.\hakamiq-cso.exe info ".\game.cso"
```

Verify a CSO file:

```powershell
.\hakamiq-cso.exe verify ".\game.cso"
```

Decompress CSO to ISO:

```powershell
.\hakamiq-cso.exe decompress ".\game.cso" -o ".\game.iso"
```

Overwrite an existing ISO:

```powershell
.\hakamiq-cso.exe decompress ".\game.cso" -o ".\game.iso" --force
```

Run without progress messages:

```powershell
.\hakamiq-cso.exe decompress ".\game.cso" -o ".\game.iso" --quiet
```

## Full path example

You can also use full file paths:

```powershell
.\hakamiq-cso.exe decompress "D:\Games\PSP\game.cso" -o "D:\Games\PSP\game.iso"
```

## JSON output

Use `--json` if another program or script needs to read the result:

```powershell
.\hakamiq-cso.exe verify ".\game.cso" --json
```

Most users do not need this option.

## SHA256SUMS.txt

`SHA256SUMS.txt` is optional.

It is included only for users who want to verify that the release files were not changed or corrupted.

You do not need it to run the tool.

## Exit codes

Exit codes are mainly for scripts and automation.

Most users do not need them.

```text
0    Success
1    General failure
2    Invalid command or missing argument
10   Input file not found
11   Invalid CSO file header
12   Unsupported CSO version
13   Corrupt CSO index table
14   Output file already exists
15   Cannot write output file
16   Not enough disk space
20   Decompression failed
130  Operation canceled by user
```

## Current limitations

* Decompression currently supports CSO v1 only.
* Verification checks the CSO structure, not game compatibility.
* ISO to CSO compression is not implemented yet.
* CHD integration is not included in this tool yet.
