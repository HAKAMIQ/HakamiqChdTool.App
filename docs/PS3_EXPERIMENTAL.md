# PS3 experimental support

PS3 handling is experimental.

The app can detect some PS3-related inputs and show useful information.
That does not mean every PS3 source should be converted to CHD.

## What the app may detect

The app may recognize:

- PS3 folders
- PS3 ISO or Blu-ray-style ISO evidence
- PS3_DISC.SFB
- PARAM.SFO
- EBOOT.BIN
- .pkg as a PS3-related package input
- existing .chd files for verify or info-style handling

Detection is not the same as full conversion support.

## Current limits

The app does not provide:

- automatic PS3 update merging
- guaranteed folder-to-CHD conversion
- decryption
- keys or firmware
- game, patch, DLC, or PKG redistribution
- a guarantee that a PS3 CHD works in every emulator

These limits are intentional until the workflow is proven safer.

## Recommended use

Use PS3 handling for inspection, planning, and cautious testing.

Start with a small legal sample when possible. Do not treat a detected
PS3 layout as proof that conversion is the right next step.

## Issue reports

When reporting PS3 behavior, describe:

- input type
- folder layout
- selected action
- short error text
- whether the source is folder, ISO, PKG, or CHD

Do not upload copyrighted game content, decrypted files, keys, firmware,
or package contents.
