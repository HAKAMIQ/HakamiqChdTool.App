# Hakamiq CHD Tool v1.0.3

Windows x64 · Self-contained .NET 8 · WPF · Arabic RTL UI

## Highlights

- Added controlled PS3 / Blu-ray ISO analysis integration derived from Hakamiq.BluRayAnalyzer logic.
- Improved raw ISO detection for PS3 and Blu-ray/UDF media before CHD planning.
- Added metadata-oriented checks for PS3 disc markers such as `PS3_DISC.SFB`, `PARAM.SFO`, and `EBOOT.BIN`.
- Kept the unified public build with Free/Premium/licensing removed.
- Kept the existing v102 queue, workflow, Redump, 7-Zip, and `chdman` runtime behavior intact.
- About window version remains aligned with assembly metadata and the GitHub release tag.
- Convert supported disc images to CHD.
- Extract and verify CHD files.
- Queue-based workflow with per-item status.
- Light, Dark, and HAKAMIQ themes.
- Optional Redump-assisted validation using user-provided local data.
- Bundled chdman runtime.

## Requirements

- Windows 10 1809 or later
- Windows 11 recommended
- x64 CPU

## Notice

This release does not include games, ROMs, BIOS files, disc images, Redump databases, keys, or copyrighted media.

See:

- `docs/legal/CHDMAN_NOTICE.md`
- `docs/legal/MAME_COPYING.txt`
- `docs/legal/MAME_GPL-2.0.txt`
- `docs/legal/THIRD_PARTY_NOTICES.txt`
