# Release safety

Use only the approved release path:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

Do not assemble a public ZIP by hand.

## Rules

- Do not publish from `bin` or `obj`.
- Do not ship source files, scripts, CI files, logs, temp files, or user media.
- Do not include private keys, tokens, Redump databases, or local settings.
- Do not include games, ROMs, BIOS files, ISO/CHD images, or copyrighted content.
- Release output must pass `Verify-EndUserRelease.ps1`.

## Expected output areas

The exact file list is enforced by the verifier. In general, a release may include:

```text
HakamiqChdTool.exe
HakamiqChdTool.dll
HakamiqChdTool.deps.json
HakamiqChdTool.runtimeconfig.json
Tools\7zip\
Tools\hakamiq-cso\   bundled Hakamiq.CsoKit helper when CSO preprocessing is enabled
Resources and runtime dependencies
LICENSE
README.md
docs\legal\
release-manifest.json
```

Use the script output as the final authority. Documentation can drift; the gate should not.
