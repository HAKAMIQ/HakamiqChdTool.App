# Smoke test checklist

Run after a successful release build.

## Startup

- [ ] App launches from Debug build
- [ ] App launches from Release output
- [ ] Main window opens without crash
- [ ] Advanced Options opens
- [ ] About window opens
- [ ] No XAML, resource, or binding errors appear in logs

## UI

- [ ] Light theme works
- [ ] Dark theme works
- [ ] HAKAMIQ theme works
- [ ] RTL caption buttons are ordered correctly
- [ ] Advanced Options is readable and not clipped

## Workflow

- [ ] Add supported ISO, CUE, or GDI file
- [ ] Convert to CHD
- [ ] Verify CHD
- [ ] Extract CHD
- [ ] Cancel a running job
- [ ] No orphan `chdman`, `7z`, or `7za` process remains after close

## Performance

- [ ] Queue scrolling remains responsive
- [ ] CPU is not oversubscribed
- [ ] RAM returns near baseline after clearing queue
- [ ] Logs do not grow while idle
