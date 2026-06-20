# Architecture

Hakamiq CHD Tool is a Windows WPF app.

It helps users convert, check, and extract CHD files. The app uses
chdman for CHD work.

The app does not write CHD files by itself.

## Current app

The app stays on this path:

- WPF
- C#
- .NET 8
- Windows x64
- runs on the user's PC
- uses chdman for CHD work

## Job steps

A normal job uses these steps:

- add the source file
- check the source file
- choose the output path
- prepare the chdman command
- run chdman
- show progress
- show the final result
- clean temporary files

The app should catch clear problems before a long job starts.

## Main parts

### UI

WPF windows, dialogs, themes, layout, and user actions.

### ViewModels

Screen data, buttons, queue rows, options, and user actions.

### Core

Job rules, queue item data, output paths, result names, and cleanup
rules.

### Services

Code that runs app tasks, such as conversion, file checks, archive
opening, free-space checks, and starting tools.

### Tools

Tools are programs used by the app.

chdman handles CHD work.

7-Zip can open ZIP, RAR, and 7Z files.

CsoKit can prepare PSP CSO files before CHD conversion.

The CHD reader tool can show CHD file details. It must not change CHD
files.

### Scripts

Scripts are used for local checks, package checks, and release work.

Developer commands belong in CONTRIBUTING.md.

## chdman

chdman handles CHD format details.

Hakamiq CHD Tool prepares safe chdman commands and shows clear results
to the user.

The app does not need to show every chdman option.

## Queue results

Queue results should stay clear:

- success
- failed
- cancelled
- skipped
- not supported

The user should be able to see what happened to each item.

## Temporary files

Temporary files are not final user files.

They should stay in the app work folder and should be cleaned after the
job ends.

Keeping temporary files should be a developer choice, not normal app
behavior.

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

## Release packages

Release ZIP files should include the app, needed tools, and required
license files.

They should not include source files, scripts, CI folders, local test
files, or debug files.
