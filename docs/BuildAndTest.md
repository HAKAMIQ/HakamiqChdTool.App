# Build and verification

Target: Windows x64 WPF on .NET 8.

Use this page for local developer checks. End users do not need these commands.

## Requirements

- Windows
- .NET 8 SDK matching `global.json` or a compatible roll-forward SDK
- PowerShell 5.1 or later

## Local gate

```powershell
.\scripts\Verify-Local.ps1
```

This restores, builds Release for `win-x64`, and prints the manual smoke checklist. No automated test project is currently included, so do not require `dotnet test` until a real test project exists.

## Release gate

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

The publish script performs repository checks, publishes the app, generates release metadata, and invokes the end-user release verifier.

## Manual check

Use:

```text
docs/SMOKE_TEST_CHECKLIST.md
```

Run the smoke checklist before publishing a public release. Build passing is not enough for a WPF desktop tool.
