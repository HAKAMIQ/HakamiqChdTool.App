# Library Organizer — design proposal

Status: proposal, not implemented.
UI name: **تنظيم الألعاب** / **Game Organizer**.

## Goal

Scan a game library, identify each game's platform and region, and move it into
a clean `Platform\Region\` layout. Nothing is ever deleted. Anything that needs
a human decision (duplicates, old updates, unknown files) is moved to a review
folder, and a review window explains what each item is and what the sensible
actions are.

## Non-goals

- No deletion, ever. The user deletes from `_Review` themselves.
- No change to queue, run, cancel, retry, or conversion defaults
  (see [Architecture boundaries](architecture/ARCH_BOUND.md)).
- No new `MediaInputKind` values. Library items are a separate model.
- No network, no Python, no external tools for the organizer itself.
- No 1G1R (keep one region only) in the first version. Regions are kept side by
  side; only *true* duplicates go to review.

## Where it lives

The organizer is a separate window opened from the main header, like the
orphan-cleanup and Redump dialogs. It is **not** a fourth operation mode,
because operation modes drive the conversion queue and the queue must stay
untouched.

```
Core/Library/            pure logic, no IO, fully unit-tested
  LibItem.cs             one detected game/unit (path, kind, platform, region, title key, version, size, evidence)
  LibRegion.cs           USA, Europe, Japan, Asia, Korea, World, Unknown
  LibEvidence.cs         how a field was decided (serial, product code, title ID, name tag, path hint) + confidence
  Rules/IRegionRule.cs   one rule per platform family
  Rules/SonyDiscRule.cs  PS1/PS2/PSP — delegates to existing DiscSerialCatalog
  Rules/Ps3Rule.cs       BLUS/BCUS/NPUB → USA, BLES/BCES/NPEB → Europe, BLJM/BCJS/BLJS/NPJB → Japan, BLAS/BCAS → Asia
  Rules/WiiURule.cs      product code last letter: E → USA, P → Europe, J → Japan
  Rules/GcWiiRule.cs     game ID 4th char: E → USA, P → Europe, J → Japan
  Rules/SwitchRule.cs    title ID from [0100…] — region World; groups base/update/DLC
  Rules/NameTagRule.cs   fallback: Redump / No-Intro tags such as (USA), (Europe), (Japan)
  LibGrouping.cs         groups items by title key → finds true duplicates and superseded updates
  LibPlanner.cs          turns items + options into a LibPlan (no IO)
  LibPlan.cs             list of LibAction: Organize, Review, Skip — each with reason key

Services/Library/
  LibScanSvc.cs          bounded tree walk; recognises "folder games" as one unit
  LibMetaProbe.cs        reads PARAM.SFO (reuse DiscMetaProbe / BdPs3Meta), Wii U meta.xml, NSP/XCI name IDs
  LibMoveSvc.cs          executes a plan with a journal; same-volume moves only
  LibJournal.cs          JSON journal per run, used for Undo
  LibUndoSvc.cs          reverses a run from its journal
  LibAdvisor.cs          item → explanation + suggested actions (reuses Core/Advisory)

ViewModels/Library/
  LibOrganizerVM.cs      root picker, options, scan, preview, apply, undo
  LibReviewVM.cs         review list, filters, per-item actions

Views/Library/
  LibOrganizer.xaml      main organizer window
  LibReview.xaml         review window shown after a run

Resources/ArabicStrings.xaml, EnglishStrings.xaml   LocLib_* keys
HakamiqChdTool.App.Tests/LibraryTests.cs
```

Dependency direction: `Views → ViewModels → Services/Library → Core/Library`.
`Core/Library` depends on nothing in Services except the existing
`DiscSerialCatalog`, which is static and IO-free.

## What counts as one item

The scanner must treat folder-based games as a single unit, otherwise a PS3 or
Wii U game becomes thousands of "files".

| Shape | Detected by | Unit |
|---|---|---|
| PS3 folder game | `PS3_GAME\PARAM.SFO` | the game folder |
| Wii U loadiine/extracted | `meta\meta.xml` + `code\` | the game folder |
| Wii U installable | `title.tmd` + `*.app` | the title folder |
| Single-file images | ISO, CHD, CSO, RVZ, WBFS, WUA, WUX, NSP, XCI, PKG, ZIP/7Z/RAR | the file |
| Multi-file disc | CUE + BINs, GDI + tracks, M3U + discs | the descriptor and its parts, moved together |

Never descend into emulator data trees (Cemu `mlc01`, RetroArch, Yuzu/Ryujinx
`nand`). The scanner skips folders that look like emulator installs and reports
them as "skipped: emulator data".

## Redump verification (strongest evidence)

The app already has the matching engine. The organizer reuses it instead of
adding igir or Node.js:

| Existing piece | Role in the organizer |
|---|---|
| `RedSysCatalog` + `RedumpSync` | download and refresh Redump DATs |
| `RedSqlMgr` (`RomHashes` table) | hash lookup: Size+SHA1, MD5, CRC |
| `RedumpV2Classifier` | decides the source format of an item |
| `RedumpV2Engine.ScanAsync` | normalizes the item (CHD through `chdman`, archives, CSO) and matches its tracks |

A Redump match gives the exact game name, and Redump names carry the region
tag, so `(USA)`, `(Europe)`, `(USA, Europe)` come from a verified source rather
than from a guess.

### New pieces

- `Core/Library/Rules/RedumpRule.cs` — maps a `RedumpV2ScanResult` to platform,
  region and title key. Highest priority, confidence 100.
- `Services/Library/LibHashCache.cs` — new SQLite table
  `LibHashCache(Path, Size, LastWriteUtc, Sha1, MatchedGame, MatchedSystem)`.
  An unchanged file is never hashed twice, so the second scan of a large drive
  is fast.
- `Services/Library/LibRedumpBatch.cs` — runs `RedumpV2Engine` over many items
  with one shared database connection, cancellation, progress and a temp-space
  check before every CHD.

### Evidence order

1. DAT match (igir report, or the built-in Redump engine)
2. Embedded metadata (PARAM.SFO, meta.xml, disc serial)
3. ID in the file name (Switch title ID, Wii U product code)
4. Name tag
5. Path hint (`ConsoleId`)

When sources disagree, the item goes to review with both values shown.

### Scope

Redump covers optical-disc systems: PS1, PS2, PSP, Saturn, Dreamcast, Sega CD,
GameCube, Wii, Xbox, Xbox 360 and others. It does not cover Switch, Wii U
digital titles, or PS3 folder games (Redump has IRD/disc keys only for PS3).
Those keep using metadata rules.

### Cost and the streaming option

`RedumpV2Engine` extracts a CHD to a temp folder before hashing. That is correct
but needs free space equal to the full disc and is slow for DVD systems.

Stage 2 of this feature can add a streaming CHD hasher (reading hunks and
hashing track data in memory, as `CHDSharpReference` does) so no temp files are
written. The `chdman` path stays as the fallback and as the reference: tests
must show both paths give identical track hashes before streaming is enabled.

### UI

Organizer option: **تحقق من Redump (أدق وأبطأ)**, off by default, with a time
and temp-space estimate before start. Matched items show a "Redump verified"
badge in the preview and review windows.

## Optional provider: igir (DAT matching for the whole library)

[igir](https://igir.io/) matches a collection against Redump, No-Intro and MAME
DATs. Since v5 it reads `.chd`, `.cso`, `.rvz`, `.wia`, `.gcz`, `.nkit.iso`,
`.7z` and `.zip` **without temporary files** and caches checksums, which removes
the main cost of the `chdman`-extract path above. It is offered as an optional
identification provider, not as a replacement for the organizer.

### Role: identify only, never move

igir runs only the read-only `report` command. Its CSV feeds the organizer as
evidence. All moves, review folders, journal and undo stay in the organizer, so
there is one move engine with one set of safety rules.

```
igir report --dat <dats> --input <item paths> --report-output <work>\report.csv
            --cache-path <app data>\igir.cache --input-checksum-max SHA1
```

Report columns used: `DAT Name` → platform, `Game Name` → title and region tags
(`(USA)`, `(Europe)`, `(USA, Europe)`), `Status` → `FOUND` / `DUPLICATE` /
`UNUSED`, `ROM Files` → which input path matched.

| igir status | Organizer action |
|---|---|
| FOUND | organize by DAT name and region tag, badge "DAT verified" |
| DUPLICATE | review: Duplicates |
| UNUSED (no DAT match) | fall back to metadata/name rules; if still unknown, review |

### Packaging (follows the existing external-tool pattern)

- `Tools/igir/igir.exe` — the official standalone Windows build, pinned by
  SHA-256 in [Bundled tool versions](../Tools/VERSIONS.md) and revalidated before
  every run, like CsoKit.
- `Services/ExternalTools/Igir/IgirTool.cs` — locate and verify the binary.
- `Services/ExternalTools/Igir/IgirArgs.cs` — builds arguments from an allow-list:
  only `report`, never `move`, `clean`, `zip` or `--overwrite`.
- `Services/ExternalTools/Igir/IgirRun.cs` — runs through the existing process
  runner with output caps and cancellation.
- `Services/ExternalTools/Igir/IgirReportParser.cs` — strict CSV parser; unknown
  columns or statuses are reported, not guessed.
- DATs come from the existing Redump sync folder; No-Intro DATs are user-provided
  (No-Intro has no public direct download endpoint).

### License

igir is GPL-3.0-or-later; this app is MIT. Shipping `igir.exe` unmodified as a
separate program called over its CLI is aggregation, the same arrangement as
`chdman`. Requirements: include igir's license text and a link to the exact
source version in the release's legal notices. igir source code must **not** be
copied or ported into this repository. (Not legal advice; confirm before
release.)

### Architecture rule impact

[Architecture boundaries](architecture/ARCH_BOUND.md) rule 4 forbids a parser
outside .NET. igir would be a second non-.NET external tool next to `chdman`
and 7-Zip. Adding it needs an explicit decision recorded in `ARCH_BOUND.md`
before stage 2 starts.

### Coverage on a typical library

| Covered by igir + DATs | Not covered (organizer rules only) |
|---|---|
| PS1, PS2, PSP, Saturn, Dreamcast, Sega CD, 3DO, Neo Geo CD (Redump) | PS3 folder games (IRD only) |
| GameCube, Wii incl. RVZ (Redump) | PS4 / PS5 PKG |
| Xbox, Xbox 360 ISO (Redump) | Switch NSP / XCI |
| Cartridge systems in ZIP/7Z: NES, SNES, N64, GB/GBC/GBA, DS, Genesis, Master System, Game Gear, Atari, PC Engine (No-Intro) | Wii U WUA/WUX and extracted folders |
| Arcade sets (MAME DATs) | PC games |

## Pipeline

```
Pick root ─► Scan (read-only) ─► Identify ─► Group ─► Plan ─► Preview ─► Apply ─► Review window
                                                                   │
                                                                   └─► Undo (journal)
```

1. **Scan** — read-only, cancellable, bounded, skips reparse points. Reuse the
   safety patterns from `RedLibScan`.
2. **Identify** — run rules in the evidence order above (Redump match first
   when enabled). Every field keeps its evidence and confidence.
3. **Group** — same platform + same title key. Inside a group:
   - same region, same content → duplicate
   - same title, several update versions → keep newest, older ones superseded
   - different regions → **not** a duplicate; each goes to its own region folder
4. **Plan** — pure function. Output is a list of actions with reason keys.
5. **Preview** — table of every planned move, with totals. Nothing has moved.
6. **Apply** — moves only after explicit confirmation.
7. **Review window** — opens automatically when the run put anything in
   `_Review`.

## Target layout

```
<Root>\<Platform>\<Region>\<game>
<Root>\_Review\<run date>\Duplicates\<Platform>\…
<Root>\_Review\<run date>\Old updates\<Platform>\…
<Root>\_Review\<run date>\Unknown region\<Platform>\…
<Root>\_Review\<run date>\Unknown platform\…
<Root>\_Organizer\journal-<run date>.json
```

Switch has no meaningful region split; its items go to `<Platform>\World\`, and
the base, its updates and DLC stay together.

Platform folder names come from the existing platform names in `PlatformSvc`,
so the organizer and the converter agree on naming.

## Safety rules

- Never delete. The only file operation is a move.
- Same volume only. Moves are renames and are instant. A cross-volume target is
  refused (use `StoreTopology` / `StoreDevice` to check).
- Never move outside the selected root. Every target path goes through the same
  path validation used by conversion output (`ConvPathVal` / `FilePathGate`).
- Never overwrite. On a name clash the item goes to `_Review\…\Conflicts` instead.
- Journal first: write the planned action to the journal, then move, then mark
  it done. A crash leaves a journal that Undo can still follow.
- Multi-part items move together or not at all.
- Low-confidence identification never auto-organizes; it goes to review.

## Review window (تنظيم الألعاب — مراجعة)

One row per item in `_Review`:

| Column | Example |
|---|---|
| Name | Resident Evil 6 [BLES01661] |
| Type | PS3 folder game |
| Why it is here | Same game and region as another copy |
| Size | 11.9 GB |
| Suggested actions | Keep, Restore, Open folder, Convert |

The explanation and action list come from `LibAdvisor`, keyed by item kind and
reason. Examples:

| Item | Explanation | Actions |
|---|---|---|
| Duplicate, same region | Another copy with the same ID and region exists. | Open both, Restore, Keep both |
| Older update (Wii U / Switch) | A newer update for the same title exists. Emulators only need the latest. | Restore, Open folder |
| PS2 game inside .7z | Emulators cannot run it without extracting first. | Send to CHD queue |
| PS1/PS2/DC/Saturn ISO or CUE/BIN | Can be converted to CHD to save space and run directly. | Send to CHD queue |
| Full-size Xbox / Xbox 360 ISO | Contains padding; can be trimmed with an external tool. | Open folder, Show how |
| GameCube / Wii ISO | Can be converted to RVZ in Dolphin. | Open folder, Show how |
| Unknown region | No serial, ID or tag was found. | Choose region, Restore |
| Unknown platform | Not recognised as a game. | Choose platform, Restore |

"Send to CHD queue" uses the existing intake path exactly as drag-and-drop does.
It does not add new queue behavior.

"Keep" and "Restore" move the item back to the organized tree (Keep) or to its
original location (Restore), using the journal.

## Options

- Root folder.
- Layout: `Platform\Region` (default) or `Platform` only.
- Region folder names: English (default) or Arabic.
- Platforms to include.
- Detect old updates: on by default.
- Dry run only: produces the preview and a report, never moves.

Stored in `AppSettings` under a new `Library` section.

## Tests

- Region rules: table tests per platform, including edge cases
  (`BLUS` vs `BLES`, Wii U `ALZP01`, Switch base vs update IDs, name tags with
  several regions such as `(USA, Europe)` → World).
- Grouping: same title different regions are not duplicates; update chains keep
  the newest.
- Planner: pure input → expected actions, no file system.
- Move service on a temp folder: plan → apply → undo returns the exact original
  tree; name clashes go to Conflicts; cross-volume target is refused; nothing is
  ever deleted.
- Scanner: PS3 and Wii U folder games are one item; emulator data trees are
  skipped; reparse points are skipped.

## Delivery stages

Each stage is a separate patch with its own tests, following the existing rule
of not mixing UI and service changes.

1. `Core/Library` models, region rules, grouping, planner, and tests. No UI.
2. Scanner and metadata probes; read-only report (dry run). Tests.
3. Move service, journal, undo. Tests on temp folders.
4. Organizer window, review window, localization.
5. Advisor actions wired to existing intake ("Send to CHD queue").

## Open questions

- Should a later version offer 1G1R (keep one preferred region and move the
  rest to review)? Not in v1.
- Should region folders be English or Arabic by default?
- Should the review window also open from the main header for older runs?
