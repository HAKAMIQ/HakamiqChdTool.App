# Hakamiq CsoKit bundled tool notice

Hakamiq CHD Tool bundles `Hakamiq.CsoKit` for PSP CSO v1 inspection, verification, and decompression to a temporary ISO before `chdman createdvd`.

Bundled tool version:

```text
Hakamiq.CsoKit 0.4.0-beta.1
```

Runtime path:

```text
Tools\hakamiq-cso\win-x64\hakamiq-cso.exe
```

Integration contract:

```text
hakamiq-cso.exe info input.cso --json
hakamiq-cso.exe verify input.cso --json
hakamiq-cso.exe decompress input.cso -o prepared.iso --force --json
```

`Hakamiq.CsoKit` only prepares CSO to ISO. Hakamiq CHD Tool then runs the existing ISO to CHD conversion path.

The bundled tool release includes `LICENSE.txt`, `README.md`, and `SHA256SUMS.txt` next to `hakamiq-cso.exe`.
