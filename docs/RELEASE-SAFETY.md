# Release safety

Use only:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

## Rules

- Do not publish from `bin` or `obj`.
- Do not ship source files, scripts, logs, temp files, or user media.
- Do not include private keys.
- Do not include ROMs, BIOS files, disc images, DAT databases, or copyrighted content.
- Release output must pass `Verify-EndUserRelease.ps1`.

## Expected output

```text
HakamiqChdTool.exe
Tools\7zip\
LICENSE
LEGAL.md
THIRD_PARTY_NOTICES.txt
CHDMAN_NOTICE.md
MAME_COPYING.txt
MAME_GPL-2.0.txt
SEVENZIP_NOTICE.md
release-manifest.json
```
