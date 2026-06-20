# Project direction

Hakamiq CHD Tool is a Windows desktop app for CHD workflows.

The current direction is:

- WPF
- C#
- .NET 8
- Windows x64
- local processing
- chdman-based convert, verify, and extract workflows

## In scope

The app focuses on practical CHD workflow tasks:

- adding files and folders
- checking input before conversion
- staging supported archive input
- running safe chdman workflows
- showing clear queue results
- keeping release packages predictable

## Out of scope

These are not part of the current direction:

- Android builds
- web UI
- cloud conversion
- server-side conversion
- Avalonia rewrite
- C++ rewrite of the WPF app
- custom CHD encoder inside the app
- emulator features
- ROM, BIOS, or Redump database distribution

## Native helpers

Small native helpers are acceptable only when they solve a specific
local task.

A helper should be narrow, documented, and packaged deliberately. It
should not replace the WPF app or the chdman workflow.

## Product boundary

The app prepares and manages CHD work.

It is not an emulator, ROM downloader, BIOS provider, Redump mirror, or
general archive manager.
