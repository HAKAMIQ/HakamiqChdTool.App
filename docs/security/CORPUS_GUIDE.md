# CHD and CSO corpus campaign

The repository contains a small generated corpus only. Real disc images must remain outside Git and must be owned or lawfully controlled by the operator.

Build the Release configuration, then run an external corpus with:

```powershell
dotnet .\HakamiqChdTool.App.Tests\bin\Release\net10.0-windows10.0.17763.0\HakamiqChdTool.App.Tests.dll `
  --app-assembly .\bin\Release\net10.0-windows10.0.17763.0\win-x64\HakamiqChdTool.dll `
  --security-mode corpus `
  --corpus-root D:\OwnedSecurityCorpus `
  --max-files 2000 `
  --max-total-bytes 2199023255552 `
  --per-file-timeout-seconds 600 `
  --security-output .\TestResults\Security
```

Supported inputs are CHD, CSO, ZSO, DAX, ISO, CUE, BIN, ZIP, 7Z, and RAR. Reports contain relative paths, sizes, hashes, classifier results, and verifier outcomes. They do not copy source media.
Treat those reports as potentially sensitive metadata. Review and redact them before sharing outside the audit team.

For safety, the corpus root itself must not be a reparse point. Enumeration skips reparse points and stops after 32 directory levels. File count, aggregate size, per-file hashing/verifier time, and captured process output are bounded before results are accepted.

A useful private corpus should cover multiple CHD versions and media types, parent/child CHDs, different codecs and hunk sizes, CSO/ZSO/DAX block sizes and compression modes, multi-track CUE/BIN images, Unicode names, very large files, valid edge cases, and known-corrupt samples. Record the lawful source and expected outcome in a separate private inventory.

Do not interpret verifier rejection of a deliberately malformed sample as a campaign failure. A campaign fails when a parser crashes, hangs beyond the configured timeout, exceeds the output bound, or violates a harness invariant.
