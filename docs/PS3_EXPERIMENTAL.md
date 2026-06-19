# PS3 experimental support

PS3 handling is experimental and conservative.

The app can analyze some PS3-related inputs, but that does not mean every PS3 source can be converted safely or usefully to CHD.

## What the app may detect

- PS3 folders
- PS3 ISO or Blu-ray-style ISO evidence
- `PS3_DISC.SFB`
- `PARAM.SFO`
- `EBOOT.BIN`
- `.pkg` as a PS3-related package input
- existing `.chd` files that may need verify/info-style handling

## What this does not promise

- no automatic PS3 update merging
- no guaranteed folder-to-CHD pipeline
- no guarantee that a PS3 CHD is usable by every emulator
- no decryption, keys, firmware, or content acquisition
- no game, patch, DLC, or PKG redistribution

## Safe wording for issues

When reporting PS3 behavior, describe the input type and layout. Do not upload copyrighted game content, decrypted files, keys, firmware, or package contents.

If you are unsure, test with a small legal sample first.
