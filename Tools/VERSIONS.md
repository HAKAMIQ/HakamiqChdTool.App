# Bundled Tool Versions

Last reviewed: 2026-11-19.

## igir

- Version: 5.8.1 (optional; fetched by `scripts/FetchIgir.ps1` because the executable exceeds the 50 MB repository binary limit)
- Upstream release: <https://github.com/emmercm/igir/releases/tag/v5.8.1>
- ZIP SHA-256: `48025BACCB4EE564B6DDA4B3AA65C9B90AB01340C2A43773FA506DC880D013F2`
- `Tools/igir/win-x64/igir.exe` SHA-256: `F3BCD0173C5E79A3DCEC8CC65707007035849920AB362B51F09E133BDBB63DD1`
- Observed executable size: 121,495,552 bytes.
- Command verification: executable reported `v5.8.1 (Bun v1.4.4)`; help lists `report`, `--dat`, `--input`, `--report-output`, `--cache-path`, `--temp-dir`, `--input-checksum-min/max`, `--reader-threads`, and `--dat-threads`.

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

- Version: 26.06 x64
- Upstream release: <https://github.com/ip7z/7zip/releases/tag/26.06>
- Downloaded package: `7z2606-x64.exe`
- Package SHA-256:
  `9B697F65ECC2D9B0152F5EE96D72DDE522A7827690349D9307935DACF40F42EC`
- `Tools/7zip/7z.exe` SHA-256:
  `DC0493429FE28136FCB177A0E77C9875CBB3B0BFF5F2F8EEBA4DF302E98CEF76`
- `Tools/7zip/7z.dll` SHA-256:
  `500DB9EB17F3C21B729BA41DD02ECFE1823DA05028C7E4BA129084CC74CC364E`

## CsoKit

- Bundled version: 0.6.6
- Source repository: <https://github.com/HAKAMIQ/CsoKit>
- Upstream release: <https://github.com/HAKAMIQ/CsoKit/releases/tag/v0.6.6>
  (immutable release)
- Reviewed source commit:
  `2f4da904e1420bbb9da2e508415841108414ab0c`
- Reviewed source tree:
  <https://github.com/HAKAMIQ/CsoKit/tree/2f4da904e1420bbb9da2e508415841108414ab0c>
- Downloaded package: `csokit-0.6.6-win-x64.zip`
- Package SHA-256:
  `A5A20C173F8DBEA7CBD73E719D2975F7BF6FF6589CCA90E14E3EBB252D32E9BC`
- `Tools/hakamiq-cso/win-x64/csokit.exe` SHA-256:
  `AE8404614FC44B4917F0662FB7FABEC603351086F4BB2FF89FD4B53E589A0F36`
- `Tools/hakamiq-cso/win-x64/CsoKit.Native.dll` SHA-256:
  `F37AED7918AA1CFFE68BEDF04128292EFF6607F0E4E0386801C700707F2F388E`
- Native ABI: 2.
- Build evidence: the official CsoKit "Build, test, native, and package",
  CodeQL ("Analyze C#") and "Publish GitHub release" jobs passed on the
  reviewed commit, which published the package above. The package digest
  matched the GitHub Releases API `digest` field, `gh attestation verify`
  accepted the release build-provenance attestation, and both runtime digests
  matched the packaged `SHA256SUMS.txt`. `csokit --version` reported
  `0.6.6+2f4da904e1420bbb9da2e508415841108414ab0c`; `native-info` reported
  native 0.6.6 ABI 2. `detect --json` still reports `format`, `magic` and
  `uncompressedSize` for ZSO, and `decompress` still rejects a truncated ZSO
  without leaving an output file.
- Changes from 0.6.5: CSO1, CSO2, ZSO and DAX files written by maxcso with
  16 KB blocks for images of 2 GB and more (for example PS2 games) are no
  longer reported as damaged by `verify --deep`, `decompress` and `repair`
  (a full-size compressed final block is accepted); `decompress` also reads
  CSO2 and DAX. Arguments used by the app and the native ABI are unchanged.
- 0.6.5 made number output culture-invariant and warns when
  `CsoKit.Native.dll` is missing. 0.6.4 added ZSO `decompress`.
- Integration policy: the application accepts CsoKit 0.6.1 or newer for CSO
  and requires 0.6.4 or newer for ZSO. Both bundled runtime files are pinned
  before every execution.

## xdvdfs

- Version: 0.8.3 (MIT)
- Upstream release: <https://github.com/antangelo/xdvdfs/releases/tag/v0.8.3>
- Downloaded package: `xdvdfs-windows-1cc850bf1b3487fad7ec7c9eed01d83e8fc75ba4.zip`
- Package SHA-256:
  `008B1B3A63F1ECB00D1ED51AB154053FD0F7C41C68E973455BBE77AB989A7CC5`
  (the release publishes no digest; recorded at download on 2026-11-20)
- `Tools/xdvdfs/win-x64/xdvdfs.exe` SHA-256:
  `DF5F19954EF706C130C256546BF67CF7725611BDB30D1815577187007A6F907A`
- Command verification: `--version` reported `xdvdfs-cli 0.8.3`. A Redump Xbox
  ISO packed to an XISO with the same `checksum` and per-file MD5s, and an
  Xbox 360 XGD2 image unpacked with every file matching `md5`.
- Used for: Xbox ISO or game folder to XISO, and unpacking Xbox 360 images
  before ZAR packing.

## iso2god-rs

- Version: 1.8.1 (MIT); the executable reports `iso2god 1.8.0`
- Upstream release: <https://github.com/iliazeus/iso2god-rs/releases/tag/v1.8.1>
- Downloaded file: `iso2god-x86_64-windows.exe`
- `Tools/iso2god/win-x64/iso2god.exe` SHA-256:
  `1459FCF8FF5A89E039B02C68C06B006B7A036F3B1CA237C2D7E03D61BD211D47`
  (matches the `digest` field of the GitHub Releases API)
- Command verification: an Xbox 360 ISO converted to Games on Demand with a
  `LIVE` header, its title ID and name read by `--dry-run`.
- Used for: Xbox 360 ISO to GOD.

## ZArchive

- Version: 0.1.2 (MIT-0)
- Upstream release: <https://github.com/Exzap/ZArchive/releases/tag/v0.1.2>
- Downloaded file: `zarchive.exe`
- `Tools/zarchive/win-x64/zarchive.exe` SHA-256:
  `F9BBA11DF7EE120186E013214C14C210690F72AB2940ECC158FC40BF36E78B6C`
  (the release publishes no digest; recorded at download on 2026-11-20)
- Command verification: an unpacked Xbox 360 game packed to `.zar` and
  unpacked again with every file's SHA-1 unchanged.
- Used for: Xbox 360 game folder to ZAR for Xenia Canary.

## Verification policy

The files above were extracted without running the downloaded installers. Their
versions were executed with information-only commands, and their exact SHA-256
digests are pinned here for reproducible review. The downloaded package digests
matched the `digest` fields returned by the official GitHub Releases API. The
upstream Windows artifacts were not Authenticode-signed when reviewed, so release
maintainers must download only from the linked official release pages and compare
the resulting file digests during future updates.
