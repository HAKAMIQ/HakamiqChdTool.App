# Build and verification

Target: .NET 8 WPF, Windows x64.

## Local gate

```powershell
.\scripts\Verify-Local.ps1
```

## Release gate

```powershell
.\scripts\Publish-EndUserRelease.ps1
```

## Manual check

```text
docs/SMOKE_TEST_CHECKLIST.md
```

No automated test project is included yet. Do not require `dotnet test` until a real test project exists.
