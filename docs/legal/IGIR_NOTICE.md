# igir notice

CHD Tool can optionally use the official, unmodified igir executable to match
game files against user-provided DAT files and write a CSV report.

## Included tool

- Tool: `igir.exe`
- Project: igir — <https://igir.io/>
- Author: Christian Emmer and contributors
- Version: 5.7.2
- License: GPL-3.0-or-later (full text: `IGIR_GPL-3.0.txt`)
- Source: <https://github.com/emmercm/igir/tree/v5.7.2>
- Path: `Tools\igir\win-x64`
- ZIP SHA-256: `7E448AE8E34377A8829E33C949B07A09613CB3A0DAD2B4325CA9BB52CF8D7584`
- Executable SHA-256: `716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511`
- Modified: no. The executable is byte-identical to the upstream release asset.

## Corresponding Source (GPL-3.0 section 6)

The complete Corresponding Source for the included `igir.exe` is the upstream
v5.7.2 source tree, including `package.json` and `package-lock.json`, which pin
the third-party packages bundled into the executable.

- File: `igir-5.7.2-source.tar.gz`, published next to the CHD Tool release ZIP
  on the same release page, at no extra charge.
- Source archive SHA-256:
  `EA5341ED3A40054BFBF2F6D26F6C76902C6D5EC949D66D38692218656245875B`
- Upstream copy: <https://github.com/emmercm/igir/archive/refs/tags/v5.7.2.tar.gz>
- Written offer: for at least three years after the last CHD Tool release that
  includes igir v5.7.2, anyone who received that release may request this same
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
