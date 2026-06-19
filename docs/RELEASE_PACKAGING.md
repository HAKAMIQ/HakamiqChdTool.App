# Release packaging

End-user releases should contain only what a user needs to run the app.

Do not publish from the source tree. Use the release scripts and verify the output before uploading assets to GitHub Releases.

## Expected shape

A release package should contain the published app output, runtime files required by the selected package type, bundled tools that are explicitly approved, and legal notices.

It should not contain:

- `.git`
- source files
- project files
- CI folders
- scripts
- logs
- test data
- build caches
- user media
- games, ROMs, BIOS files, Redump databases, keys, or disc images

## Package names

Use stable naming:

```text
HakamiqChdTool-vX.Y.Z-win-x64-runtime-required.zip
HakamiqChdTool-vX.Y.Z-win-x64-self-contained.zip
```

Avoid mixing `framework-dependent`, `runtime-included`, and `runtime-required` in new release assets. Users notice inconsistent names.

## Gates

Use:

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

Then verify the generated output with the release gate. The scripts are the source of truth for exact file checks.
