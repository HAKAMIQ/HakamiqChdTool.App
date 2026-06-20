# Project direction

Hakamiq CHD Tool is a Windows desktop app.

It helps users convert, check, and extract CHD files using chdman.

## Current app

The app stays on this path:

- WPF
- C#
- .NET 8
- Windows x64
- runs on the user's PC
- uses chdman for CHD work

## What belongs here

This project should focus on:

- adding files and folders
- checking source files before conversion
- opening supported ZIP, RAR, and 7Z files before conversion
- running chdman with safe choices
- showing clear queue results
- building clean release ZIP files

## What does not belong here

These are not part of this app:

- Android version
- web version
- cloud conversion
- server conversion
- Avalonia rewrite
- C++ rewrite of the WPF app
- CHD encoder written inside the app
- emulator features
- game, ROM, BIOS, or Redump file downloads

## Small native tools

A small C or C++ tool can be added only for a clear local task.

Example: reading file details that are hard to read from C#.

It must not replace the WPF app or chdman.

## Product limits

The app manages CHD file work.

It is not an emulator, downloader, BIOS provider, Redump file provider,
or general archive manager.
