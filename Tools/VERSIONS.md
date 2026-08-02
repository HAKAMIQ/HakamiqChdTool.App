# Bundled Tool Versions

Last reviewed: 2026-08-02.

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

- Bundled version: 0.6.1
- Source repository: <https://github.com/HAKAMIQ/CsoKit>
- Reviewed source commit:
  `9e2a93d5502fa651f9a21d9dd97269e7c4912c48`
- Reviewed source tree:
  <https://github.com/HAKAMIQ/CsoKit/tree/9e2a93d5502fa651f9a21d9dd97269e7c4912c48>
- `Tools/hakamiq-cso/win-x64/csokit.exe` SHA-256:
  `FB1BF1E6BD0C51CAB54F505E7E44404F1E5CBFBFF3CB0FFC7EEC159D7D9254C0`
- `Tools/hakamiq-cso/win-x64/CsoKit.Native.dll` SHA-256:
  `B396B0CA41BE7F905E8EA73C285C1F5089C8DA4FB1E4C157775BF198B1F70589`
- Native ABI: 2.
- Build evidence: the CsoKit final release gate passed Debug and Release builds,
  201/201 automated tests, published executable smoke tests, native capability
  checks, and a native ISO/CSO/ISO SHA-256 round trip.
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
