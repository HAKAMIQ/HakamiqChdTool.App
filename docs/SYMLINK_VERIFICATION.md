# Symlink test verification

Four tests create real symbolic links and are skipped unless the account holds
`SeCreateSymbolicLinkPrivilege` (Win32 error 1314). A skip is not a pass.

| Test name (as printed by the runner) | Covers |
| --- | --- |
| File symlinks are rejected by intake resolution | `InputResolver`, `MediaInputClassifier` |
| Directory symlinks are rejected by intake resolution | `InputResolver`, `MediaInputClassifier` |
| Library index does not follow file symlinks | `LibraryIndexer` |
| Library index does not follow directory symlinks | `LibraryIndexer` |

## Procedure (disposable Windows VM or isolated runner only)

Do not change privileges or security settings on a normal workstation.

1. Use a throwaway Windows VM or runner, and snapshot it first.
2. Either run the tests from an elevated prompt, or enable Developer Mode on that VM.
3. Build and run:

```powershell
dotnet build .\HakamiqChdTool.App.sln -c Debug --no-restore -p:NuGetAudit=false -m:1
dotnet .\HakamiqChdTool.App.Tests\bin\Debug\net10.0-windows10.0.17763.0\HakamiqChdTool.App.Tests.dll `
  --app-assembly .\bin\Debug\net10.0-windows10.0.17763.0\HakamiqChdTool.dll
```

4. Accept only if the four tests above print `[PASS]` and the summary reports
   `0 skipped`. The tests skip only on error 1314; any other link-creation
   failure fails the test.
5. Revert or discard the VM.
