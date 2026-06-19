# Project direction

Hakamiq CHD Tool is a Windows desktop CHD workflow app.

The current application direction is fixed:

- WPF
- C#
- .NET 8
- Windows x64
- local processing
- chdman-based CHD workflows

## Out of direction

The following are not part of the current application direction:

- Android builds
- Avalonia rewrite
- web UI
- cloud conversion
- server-side conversion
- C++ rewrite of the WPF app
- custom CHD encoder inside the app

A small native helper can be considered only when it solves a specific read-only probing problem. It should not replace the C# WPF architecture.

## Product boundary

The app is for media preparation and CHD workflow management. It is not an emulator, a ROM downloader, a BIOS provider, a Redump database mirror, or a general-purpose archive manager.
