# igir integration — implementation brief

Status: optional read-only report integration implemented in source; release packaging and manual UI operation remain unverified.
Related design: [Library Organizer](LIB_ORGANIZER.md).
Audience: the developer or coding agent implementing the change.

Read this whole file, [Architecture](ARCHITECTURE.md),
[Architecture boundaries](architecture/ARCH_BOUND.md) and
[Security boundaries](architecture/SECURITY_BOUNDARIES.md) before writing code.

---

## 1. Goal

Use igir as a **read-only identification provider**: given game files and DAT
files, it says which DAT entry each file is, so the Library Organizer can sort
games by platform and region with verified names.

igir reads `.chd`, `.cso`, `.rvz`, `.wia`, `.gcz`, `.nkit.iso`, `.7z`, `.zip`
without temporary files and keeps its own checksum cache. That makes it faster
and lighter than the current `chdman`-extract path in `RedumpV2Engine` for bulk
library scans.

## 2. Non-goals (hard rules)

- igir never writes to the user's library. Only the `report` command is allowed.
- No `move`, `copy`, `link`, `extract`, `zip`, `clean`, `playlist`, `fixdat`,
  `dir2dat`, `--overwrite`, `--overwrite-invalid`, `--move-delete-dirs`,
  `--clean-backup`, `--patch`.
- No igir source code inside this repository. Not copied, not ported, not
  translated to C#. (License reason, see §3.)
- No Node.js or Bun requirement for users. Ship the official standalone exe.
- No change to queue, run, cancel, retry, or conversion defaults.
- The existing Redump engine stays. igir is an additional provider, selectable,
  off by default until stage 4 is verified.
- The igir source tree must not be placed inside this repository or the agent's
  working tree. It may be read only as reference (for example
  `src/models/datStatus.ts` for report columns), from a separate folder.

## 3. License compliance (do this first)

igir is **GPL-3.0-or-later**. This app is **MIT**. Running an unmodified igir
executable as a separate program over its command line is aggregation, the same
arrangement the app already has with `chdman` (MAME, GPL-2.0). Crediting igir is
required but not sufficient. The release must:

1. Ship `igir.exe` unmodified, exactly as published in the official release.
2. Add `docs/legal/IGIR_NOTICE.md` (template in §11) naming the project, author,
   version, license, exact source URL and tag, and what the app uses it for.
3. Add the full GPL-3.0 text as `docs/legal/IGIR_GPL-3.0.txt`.
4. Add igir to `docs/legal/3P_NOTICE.txt` and `docs/legal/LEGAL.md`.
5. Include the notice and license text inside the release ZIP next to the tool.
6. Link the upstream project in the About window credits.

If the HAKAMIQ fork is ever modified and its build is shipped instead of
upstream, that fork's source for the shipped version must be public under
GPL-3.0 with the change described. Prefer shipping the **upstream** release.

Not legal advice. Confirm before the first public release.

## 4. Architecture decision to record

Add to `docs/architecture/ARCH_BOUND.md`, under "Work rules", right after
rule 4:

> 4a. Exception: igir is allowed as a bundled external tool, used only for
> read-only DAT matching through `igir report`. It is invoked as a pinned,
> hash-verified executable like `chdman` and 7-Zip. No igir code, no Node.js,
> and no Bun runtime enter the app. The command allow-list lives in
> `Services/ExternalTools/Igir/IgirArgs.cs` and is covered by tests.

## 5. Packaging

| Item | Value |
|---|---|
| Source of binary | `https://github.com/emmercm/igir/releases` |
| Asset | `igir-<version>-Windows-x64.zip` |
| Path in app | `Tools\igir\win-x64\igir.exe` |
| Pinning | SHA-256 of the ZIP and of `igir.exe` in `Tools/VERSIONS.md` |
| Verification | before **every** run, constant-time compare, same as `CsoToolLocator` |
| Version policy | pinned; upgrades are a separate reviewed patch |

Add the `Tools/VERSIONS.md` entry in the same format as chdman and CsoKit.

## 6. Code layout

```
Services/ExternalTools/Igir/
  IgirToolLocator.cs     find + SHA-256 verify igir.exe (mirror CsoToolLocator)
  IgirArgs.cs            allow-list argument builder; the only place args are made
  IgirRunner.cs          runs via ExternalToolProcessRunner (ToolProcRun.cs)
  IgirReportParser.cs    strict CSV → IgirReportRow
  IgirReportRow.cs       DatName, GameName, Status, RomFiles, flags
  IgirMatchProvider.cs   IgirReportRow → Core/Library evidence

Core/Library/Rules/
  DatMatchRule.cs        shared by Redump engine and igir: DAT name → platform,
                         game name tags → region, title key

HakamiqChdTool.App.Tests/
  IgirArgsTests.cs
  IgirReportParserTests.cs
  IgirMatchProviderTests.cs
  DatMatchRuleTests.cs
  Fixtures/igir/         small real report CSVs captured from the pinned version
```

Dependency direction: `Services/ExternalTools/Igir → Core/Library`. Nothing in
`Core` references igir.

## 7. Command contract

The only command line the app may produce:

```
igir.exe report
  --dat <dat path>            (repeatable; DAT files or zipped DATs)
  --input <file path>         (repeatable; or one directory)
  --input-exclude <glob>      (optional; emulator data trees)
  --report-output <work dir>\report.csv
  --cache-path <app data>\igir\igir.cache
  --temp-dir <app work dir>\igir-temp
  --input-checksum-max SHA1
  --reader-threads <1..8>
  --dat-threads <1..4>
```

Rules for `IgirArgs`:

- Build an argument **list** (`ProcessStartInfo.ArgumentList`), never a joined
  string.
- Every path passes `FilePathGate` / `ConvPathVal` checks and is absolute.
- `--report-output`, `--cache-path`, `--temp-dir` must resolve under app-owned
  folders (`AppPaths`), never inside the user's library.
- Reject any command or flag not listed above. A unit test enumerates every
  known igir write command and flag and asserts each is rejected.
- Thread counts are clamped; default `reader-threads` = 2 for USB/HDD, 6 for SSD
  (use `StoreDevice` / `StoreTopology` to decide).

## 8. Report contract

Columns read (from igir `src/models/datStatus.ts`, pinned version):
`DAT Name`, `Game Name`, `Status`, `ROM Files`, plus the boolean flag columns
(`BIOS`, `Retail Release`, `Unlicensed`, `Demo`, `Beta`, `Prototype`, `Bad`, …).

`IgirReportParser` rules:

- RFC 4180 CSV parsing (quoted fields, embedded commas and newlines).
- Header row must contain the required columns; unknown extra columns are
  allowed and logged once.
- Unknown `Status` values produce a parse issue, never a guess.
- `ROM Files` may hold several paths; split exactly as the pinned version writes
  them (capture this from a real fixture, do not assume).
- Size cap on the CSV and row count cap, like other tool output caps.

Status mapping:

| igir status | Organizer evidence |
|---|---|
| `FOUND` | DAT match, confidence 100; platform from DAT name, region from game name |
| `DUPLICATE` | DAT match + duplicate flag → review: Duplicates |
| `UNUSED` | no DAT match → fall back to metadata and name rules |
| `MISSING` | ignored for organizing; available for a "missing games" report |

Reference source inspected: HAKAMIQ/igir commit
`e6eea84ca8c9a5005c4922068766f22013633b30` (`../igir-ref`, read-only sibling
checkout).

Region from game name: parse the parenthesised tags with one shared parser in
`DatMatchRule` (same code for Redump engine results). `(USA, Europe)` and
`(World)` map to `World`. Keep the raw tag string as evidence.

## 9. Runtime behavior

- Cancellable: cancel kills the igir process tree (existing runner behavior).
- Progress: igir does not expose structured progress. Show an indeterminate bar
  with elapsed time and the current phase parsed from stderr lines only if the
  format is stable in the pinned version; otherwise show phase "Matching".
- Exit code ≠ 0 or missing report → the run fails with a clear message; nothing
  is moved.
- Free-space check for `--temp-dir` before start.
- Logs: store igir stdout/stderr (capped) in the app log folder for each run.
- Cache: keep `igir.cache` between runs; add "Clear igir cache" in Options.

## 10. UI and settings

Organizer window, "Identification" section:

- Provider: `Built-in Redump` (default) / `igir` / `Both (igir first)`.
- DAT folder: defaults to the Redump sync folder; user can add a No-Intro folder.
- Badge in preview and review: "DAT verified (igir)" or "DAT verified (Redump)".
- A "Missing games" button that opens the `MISSING` rows for the selected DATs.

Localization: add `LocLib_Igir_*` keys to both `ArabicStrings.xaml` and
`EnglishStrings.xaml`. No hard-coded UI text.

Matching engine setting (Options → Redump):

| Value | Single item (queue verify) | Folder / library scan |
|---|---|---|
| Auto (default) | built-in engine | igir if available and verified, else built-in |
| Built-in only | built-in engine | built-in engine |
| igir only | igir | igir |

Settings: `AppSettings.Library.Igir` with `Enabled`, `DatFolders`,
`ReaderThreads`, `DatThreads`.

## 11. `docs/legal/IGIR_NOTICE.md` template

```
# igir notice

CHD Tool can use igir to match game files against DAT files.

## Included tool

- Tool: igir.exe
- Project: igir — https://igir.io/
- Author: Christian Emmer and contributors
- Version: <pinned version>
- License: GPL-3.0-or-later (full text: IGIR_GPL-3.0.txt)
- Source: https://github.com/emmercm/igir/tree/v<pinned version>
- Path: Tools\igir\win-x64

## What it is used for

- read game files and compare their checksums with DAT files
- write a CSV report to the app's work folder

## What it does not do

CHD Tool runs only `igir report`. igir does not move, copy, rename,
or delete files in your library. igir is a separate program and is
shipped unmodified.
```

## 12. Stages

Each stage is one patch with its own tests. Do not merge stages.

0. **Speed up the built-in engine (independent of igir).**
   - Replace the byte-by-byte `UpdateCrc32` in `RedumpV2Engine.cs` and
     `DeepHash.cs` with `System.IO.Hashing.Crc32` (add the package, update
     `packages.lock.json`).
   - Compute MD5, SHA1 and CRC32 in one read pass with the three hashers fed in
     parallel per buffer, or skip MD5 when SHA1 + size is enough for the lookup
     (`IX_RomHashes_SHA1_Size` exists).
   - Add a hash cache table keyed by full path + size + last-write UTC; skip
     hashing when unchanged.
   - Tests: identical MD5/SHA1/CRC32 to the old implementation on fixed test
     vectors and on random buffers of several sizes; cache hit/miss/invalidate.
   - Record before/after timings on one PS1 CHD and one PS2 CHD in this file.

1. **Legal + packaging + ADR.** Notice files, VERSIONS.md entry, ARCH_BOUND rule
   4a, `IgirToolLocator` with hash verification. Tests: missing exe, wrong hash,
   correct hash.
2. **Args + runner.** `IgirArgs`, `IgirRunner`. Tests: every write command and
   flag rejected; paths outside app folders rejected; argument list never
   contains a joined string.
3. **Report parser + provider.** `IgirReportParser`, `IgirMatchProvider`,
   `DatMatchRule`. Tests from captured fixtures: FOUND, DUPLICATE, UNUSED,
   multi-disc, multi-region tags, quoted paths with commas, Arabic file names.
4. **Parity check.** For the same PS1 CHD set, compare igir results with the
   built-in Redump engine. Any disagreement is a bug to explain before
   continuing. Record the result in this file.
5. **Organizer wiring + UI.** Provider option, badges, missing-games view,
   localization.

## 13. Definition of done

- All tests pass in CI, including new allow-list tests.
- A full organizer run on a test library with igir enabled moves files only
  through the organizer's journal, and Undo restores the exact tree.
- Release ZIP contains `igir.exe`, `IGIR_NOTICE.md`, `IGIR_GPL-3.0.txt`;
  `3P_NOTICE.txt` and About credits mention igir.
- No igir source file exists anywhere in this repository.

## Verified implementation checkpoint (2026-10-17)

- Official v5.7.2 Windows x64 release was downloaded and inspected. ZIP SHA-256:
  `7E448AE8E34377A8829E33C949B07A09613CB3A0DAD2B4325CA9BB52CF8D7584`.
- `igir.exe` SHA-256: `716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511`;
  size: 92,256,256 bytes. The binary is not committed; `scripts/FetchIgir.ps1`
  verifies both digests and refuses to overwrite an existing executable.
- The official executable reported `v5.7.2 (Bun v1.4.2)`. Its help output
  confirmed `report`, repeatable `--dat`, `--input`, `--report-output`,
  `--cache-path`, `--temp-dir`, checksum bounds, and reader/dat thread options.
  The app passes arguments through `ArgumentList` using only `report`, writes
  work/cache files outside the selected library, and verifies the executable
  SHA-256 before each invocation.
- A bounded RFC-style CSV parser preserves `ROM Files` as emitted and rejects
  unknown statuses. The Library window includes an optional report command.
- End-to-end igir execution from the WPF app and release ZIP packaging remain
  unverified; the 92 MB executable must be fetched separately before runtime
  use. DAT match evidence is shown in the report and is not wired as an
  automatic move authorization.
