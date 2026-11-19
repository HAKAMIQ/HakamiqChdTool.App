# Bundled Tool Versions

Last reviewed: 2026-08-02.

## igir

- Version: 5.7.2 (optional; fetched by `scripts/FetchIgir.ps1` because the executable exceeds the 50 MB repository binary limit)
- Upstream release: <https://github.com/emmercm/igir/releases/tag/v5.7.2>
- ZIP SHA-256: `7E448AE8E34377A8829E33C949B07A09613CB3A0DAD2B4325CA9BB52CF8D7584`
- `Tools/igir/win-x64/igir.exe` SHA-256: `716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511`
- Observed executable size: 92,256,256 bytes.
- Command verification: executable reported `v5.7.2 (Bun v1.4.2)`; help lists `report`, `--dat`, `--input`, `--report-output`, `--cache-path`, `--temp-dir`, `--input-checksum-min/max`, `--reader-threads`, and `--dat-threads`.

## chdman

- Version: MAME 0.289
- Upstream release: <https://github.com/mamedev/mame/releases/tag/mame0289>
- Downloaded package: `mame0289b_x64.exe`
- Package SHA-256:
  `A1AA7912168C9D1B05E611906BC21B8B9BE3935822AEAD36D12A1DA363150B7D`
- `Tools/chdman.exe` SHA-256:
  `8A74468E3B0879698835B57C3B58E88E5A51E4DE73BEE6EF755C28530B5B040F`
- Compatibility note: the official 0.289 Windows x64 package requires an
  x86-64-v2 capable processor, matching MAME's published Windows requirement.

## 7-Zip

- Version: 26.02 x64
- Upstream release: <https://github.com/ip7z/7zip/releases/tag/26.02>
- Downloaded package: `7z2602-x64.exe`
- Package SHA-256:
  `6745FA76DC2EA031596D8678F6F6B99C3C1B435B4164A63485ADBBC7B8D82EF0`
- `Tools/7zip/7z.exe` SHA-256:
  `83967F1B02B43C4EFEDA302795722C809E0E81B8307DE73558D10484D5676A7D`
- `Tools/7zip/7z.dll` SHA-256:
  `69FD4DF057985C40E510E2FAC182881C7F85E90AA13EC703F763A8FDB2CE61F8`

## CsoKit

- Bundled version: 0.6.5
- Source repository: <https://github.com/HAKAMIQ/CsoKit>
- Upstream release: <https://github.com/HAKAMIQ/CsoKit/releases/tag/v0.6.5>
  (immutable release)
- Reviewed source commit:
  `872cd68455acbbf37414b07cc9e76bf5130b9af6`
- Reviewed source tree:
  <https://github.com/HAKAMIQ/CsoKit/tree/872cd68455acbbf37414b07cc9e76bf5130b9af6>
- Downloaded package: `csokit-0.6.5-win-x64.zip`
- Package SHA-256:
  `AE09EBBACBC278412A8BF4FFFC43FC1363D383F58E13C6E78E1A3C18256F54C4`
- `Tools/hakamiq-cso/win-x64/csokit.exe` SHA-256:
  `0A822E643F4B414F6F9CA53EE09B1DB49D3EA3AB68BC5A0145D394C86DA0E97E`
- `Tools/hakamiq-cso/win-x64/CsoKit.Native.dll` SHA-256:
  `BEFDAB67749B019F02C104F9297A47692C596CB3E6244D934C04AEA896631C55`
- Native ABI: 2.
- Build evidence: the official CsoKit "Build, test, native, and package",
  CodeQL ("Analyze C#") and "Publish GitHub release" jobs passed on the
  reviewed commit, which published the package above. The package digest
  matched the GitHub Releases API `digest` field, `gh attestation verify`
  accepted the release build-provenance attestation, and both runtime digests
  matched the packaged `SHA256SUMS.txt`. `csokit --version` reported
  `0.6.5+872cd68455acbbf37414b07cc9e76bf5130b9af6`; `native-info` reported
  native 0.6.5 ABI 2.
- Changes from 0.6.4: numbers are formatted culture-invariantly (ratios no
  longer print as `??` on Arabic Windows); `compress`, `--measure` and
  `repair` warn when `CsoKit.Native.dll` is missing. `decompress`, `detect`
  and `verify` arguments used by the app and the native ABI are unchanged.
- 0.6.4 added ZSO `decompress`, large-block and >2 GB ZSO fixes, and
  damaged-header errors instead of crashes.
- Integration policy: the application accepts CsoKit 0.6.1 or newer for CSO
  and requires 0.6.4 or newer for ZSO. Both bundled runtime files are pinned
  before every execution.

## Verification policy

The files above were extracted without running the downloaded installers. Their
versions were executed with information-only commands, and their exact SHA-256
digests are pinned here for reproducible review. The downloaded package digests
matched the `digest` fields returned by the official GitHub Releases API. The
upstream Windows artifacts were not Authenticode-signed when reviewed, so release
maintainers must download only from the linked official release pages and compare
the resulting file digests during future updates.
