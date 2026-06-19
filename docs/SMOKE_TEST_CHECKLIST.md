# Smoke test checklist

Run this after a successful Release build and before publishing a public ZIP.

## Startup

- [ ] App launches from local Release output
- [ ] Main window opens without crash
- [ ] Options window opens
- [ ] About window opens
- [ ] No XAML, resource, or binding errors appear in logs

## UI

- [ ] Light theme is readable
- [ ] Dark theme is readable
- [ ] HAKAMIQ theme is readable
- [ ] Arabic UI is RTL where expected
- [ ] Technical paths remain LTR and readable
- [ ] Advanced Options is not clipped at normal scaling

## Intake

- [ ] Add a supported ISO
- [ ] Add a supported CUE with its referenced tracks
- [ ] Add a supported GDI with its referenced tracks
- [ ] Add a CHD for verify/extract actions
- [ ] Add an unsupported file and confirm it is blocked clearly
- [ ] Add an archive and confirm preview/staging behavior is understandable

## Workflow

- [ ] Convert a small supported image to CHD
- [ ] Verify a CHD
- [ ] Extract a CHD through one supported profile
- [ ] Cancel a running job
- [ ] Confirm failure and cancellation are reported differently
- [ ] No orphan `chdman`, `7z`, `7za`, or approved helper process remains after close

## Packaging

- [ ] Release ZIP does not contain source files
- [ ] Release ZIP does not contain scripts or CI folders
- [ ] Legal notices are present
- [ ] No games, ROMs, BIOS files, disc images, Redump databases, keys, or private data are present

## Performance

- [ ] Queue scrolling remains responsive
- [ ] CPU is not oversubscribed under normal queue settings
- [ ] RAM returns near baseline after clearing queue
- [ ] Logs do not grow while idle
