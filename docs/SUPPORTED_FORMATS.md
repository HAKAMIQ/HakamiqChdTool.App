# Supported formats

Hakamiq CHD Tool works with disc-image workflows built around `chdman`. A file can be detected, convertible, verifiable, extractable, or unsupported; those are different states.

## Support levels

- **Convertible**: the tool can prepare a CHD creation job for this input.
- **Verifiable**: the tool can run a CHD verification job.
- **Extractable**: the tool can run a supported CHD extraction profile.
- **Detected only**: the tool can identify or inspect the input, but conversion stays conservative.
- **Unsupported**: the input is blocked before conversion.

## Direct disc image input

| Input | Current behavior |
| --- | --- |
| `.iso` | Convertible when the ISO passes the safety and media-profile checks. The app chooses the safe `createcd` or `createdvd` path from evidence. |
| `.cue` | Convertible through `createcd` when referenced track files are present and readable. |
| `.gdi` | Convertible through `createcd` when descriptor dependencies are valid. |
| `.toc` | Convertible through `createcd` when descriptor dependencies are valid. |
| `.nrg` | Convertible as a single-file CD-style input where the current workflow allows it. |
| `.bin` | Not treated as blindly safe. The app looks for a usable adjacent CUE or builds a temporary CUE only when the sector layout and console evidence are acceptable. |
| `.raw` | Treated as a dependent track file, not a standalone conversion input. |
| `.cdi` | Unsupported in the current workflow. |

## CHD input

| Input | Current behavior |
| --- | --- |
| `.chd` | Verifiable. Also extractable through the supported `chdman` extraction profiles when the CHD media type and selected action match. Existing CHD files are not blindly re-converted. |

Extraction profiles may output `.cue`, `.iso`, `.img`, or `.raw` depending on the detected/selected CHD media type. Raw extraction is safety-gated for console-disc cases where a structured CD/DVD path is the safer choice.

## Archive intake

| Input | Current behavior |
| --- | --- |
| `.zip` | Archive container. The app can stage the first valid convertible disc image where supported. |
| `.rar` | Archive container. Uses the available archive backend and safety checks. |
| `.7z` | Archive container. Uses 7-Zip when present, with managed fallback paths where supported. |

Archives are intake containers, not final media formats. The app stages a candidate image, converts that candidate, then cleans up according to the workflow result.

## PS3-related input

| Input | Current behavior |
| --- | --- |
| PS3 folder | Detected/analyzed by the experimental PS3 intake layer. Conversion remains conservative. |
| PS3 ISO / Blu-ray-style ISO | Quick analysis can identify PS3 or Blu-ray-style evidence before planning. |
| `.pkg` | Detected/inspected by PS3 intake code. It is not a general promise of safe CHD conversion. |
| PS3 `.chd` | Treated cautiously; verify/info-style actions are safer than assuming a valid playable target. |

PS3 support is intentionally narrow. See [PS3 experimental support](PS3_EXPERIMENTAL.md).

## Redump data

Redump support is optional and uses user-provided local data or configured metadata sources. The release package does not include Redump databases, DAT files, games, or disc images.
