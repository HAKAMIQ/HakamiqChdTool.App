# Smoke test checklist

Run this after a successful Release build and before publishing a public
ZIP.

This is a manual checklist. It is not a substitute for build or CI
checks.

## Startup

- [ ] App launches from local Release output.
- [ ] Main window opens without a crash.
- [ ] Options window opens.
- [ ] About window opens.
- [ ] No XAML, resource, or binding errors appear.

## UI

- [ ] Light theme is readable.
- [ ] Dark theme is readable.
- [ ] HAKAMIQ theme is readable.
- [ ] Arabic UI is RTL where expected.
- [ ] Technical paths remain LTR and readable.
- [ ] Advanced Options is not clipped at normal scaling.

## Intake

- [ ] Supported ISO can be added.
- [ ] Supported CUE can be added with its track files.
- [ ] Supported GDI can be added with its track files.
- [ ] CHD can be added for verify or extract.
- [ ] Unsupported input is blocked clearly.
- [ ] Archive preview or staging is understandable.

## Workflow

- [ ] Small supported image converts to CHD.
- [ ] Existing CHD verifies.
- [ ] Existing CHD extracts through one supported profile.
- [ ] Running job can be cancelled.
- [ ] Failed and cancelled jobs are shown differently.
- [ ] No orphan chdman, 7z, 7za, or helper process remains.

## Packaging

- [ ] Release ZIP opens normally.
- [ ] App launches from the extracted ZIP.
- [ ] Legal notices are present.
- [ ] Source files are not included.
- [ ] Scripts and CI folders are not included.
- [ ] Games, ROMs, BIOS files, disc images, keys, and private data are
      not included.

## Performance

- [ ] Queue scrolling remains responsive.
- [ ] CPU usage is reasonable under normal queue settings.
- [ ] RAM returns near baseline after clearing the queue.
- [ ] Logs do not grow while the app is idle.
