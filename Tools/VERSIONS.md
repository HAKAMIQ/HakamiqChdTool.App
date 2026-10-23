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

- Bundled version: 0.6.3
- Source repository: <https://github.com/HAKAMIQ/CsoKit>
- Upstream release: <https://github.com/HAKAMIQ/CsoKit/releases/tag/v0.6.3>
- Reviewed source commit:
  `e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d`
- Reviewed source tree:
  <https://github.com/HAKAMIQ/CsoKit/tree/e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d>
- Downloaded package: `csokit-0.6.3-win-x64.zip`
- Package SHA-256:
  `676315F8383FC1289436E7D254ED5B4B90E86F61BEE397593E276E1CB6AD3A7E`
- `Tools/hakamiq-cso/win-x64/csokit.exe` SHA-256:
  `77F0662A408BD5DA2241898E41C074B4E600DE9FA58D115C8DBBDF6FFF3EEFD9`
- `Tools/hakamiq-cso/win-x64/CsoKit.Native.dll` SHA-256:
  `E9991ADDF7FAFAEC400D124CEF7D0FC2B276CE7624261369A1EA1F8B7ECE57E0`
- Native ABI: 2.
- Build evidence: the official CsoKit CI and Release workflows passed on the
  reviewed commit, which published the package above. The CsoKit final release
  gate passed (224 + 1 tests, real and synthetic SHA-256 round trips, profile
  matrix, published executable smoke tests, native capability checks) on
  pre-release commit `82ea7aa4fba53053d01d1aa7fd3766dd5a6a8b08`, whose product
  source is identical to the reviewed commit. The bundled runtime files are the
  official release files, not the local gate build.
- Changes from 0.6.1: stricter CSO decompression (stored-block size, final index
  offset, exact raw-deflate/zlib block length and trailing data checks) and
  temporary output cleanup on unexpected errors. The CLI and native ABI are
  unchanged.
- Integration policy: the application accepts CsoKit 0.6.1 or newer and pins
  both bundled runtime files before every execution.

## Verification policy

The files above were extracted without running the downloaded installers. Their
versions were executed with information-only commands, and their exact SHA-256
digests are pinned here for reproducible review. The downloaded package digests
matched the `digest` fields returned by the official GitHub Releases API. The
upstream Windows artifacts were not Authenticode-signed when reviewed, so release
maintainers must download only from the linked official release pages and compare
the resulting file digests during future updates.
