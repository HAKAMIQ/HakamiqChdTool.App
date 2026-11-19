# igir notice

CHD Tool can optionally use the official, unmodified igir executable to match
game files against user-provided DAT files and write a CSV report.

## Included tool

- Tool: `igir.exe`
- Project: igir — <https://igir.io/>
- Author: Christian Emmer and contributors
- Version: 5.8.1
- License: GPL-3.0-or-later (full text: `IGIR_GPL-3.0.txt`)
- Source: <https://github.com/emmercm/igir/tree/v5.8.1>
- Path: `Tools\igir\win-x64`
- ZIP SHA-256: `48025BACCB4EE564B6DDA4B3AA65C9B90AB01340C2A43773FA506DC880D013F2`
- Executable SHA-256: `F3BCD0173C5E79A3DCEC8CC65707007035849920AB362B51F09E133BDBB63DD1`
- Modified: no. The executable is byte-identical to the upstream release asset.

## Corresponding Source (GPL-3.0 section 6)

The complete Corresponding Source for the included `igir.exe` is the upstream
v5.8.1 source tree, including `package.json` and `package-lock.json`, which pin
the third-party packages bundled into the executable.

- File: `igir-5.8.1-source.tar.gz`, published next to the CHD Tool release ZIP
  on the same release page, at no extra charge.
- Source archive SHA-256:
  `18742ACA59AAD2D562B25645EC6DAE728A9BF234AE597E2BD162ABCC992BEF46`
- Upstream copy: <https://github.com/emmercm/igir/archive/refs/tags/v5.8.1.tar.gz>
- Written offer: for at least three years after the last CHD Tool release that
  includes igir v5.8.1, anyone who received that release may request this same
  source archive, at no charge beyond the cost of physically performing the
  transfer, by opening an issue at
  <https://github.com/HAKAMIQ/HakamiqChdTool.App/issues>.

## Your rights

igir is a separate program distributed with CHD Tool as part of an aggregate.
CHD Tool runs it as an external process and does not link to it. The MIT license
of CHD Tool does not apply to igir and does not restrict your rights under the
GPL-3.0: you may copy, modify, and redistribute igir under that license. igir
comes with NO WARRANTY, to the extent permitted by law; see sections 15 and 16 of
`IGIR_GPL-3.0.txt`.

## What it is used for

- Read game files and compare their checksums with DAT files.
- Write a CSV report under the app's local application data folder.

CHD Tool invokes only `igir report`. igir does not move, copy, rename, or
delete files in the user's library. The executable is downloaded unchanged
from the official upstream release and verified before use.
