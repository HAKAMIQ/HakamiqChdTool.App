# Supported formats

Support is not one thing here. A file might be convertible, verifiable, extractable, detected only, or rejected before any tool runs.

That distinction matters. Detecting a file type does not always mean conversion is safe.

## Support levels

Convertible means the app can prepare a CHD creation job.

Verifiable means the app can check an existing CHD.

Extractable means the app can copy data out of a supported CHD type.

Detected only means the app can recognize the input but may still stop before conversion.

Unsupported means the app should fail early with a clear reason.

## ISO

ISO is the simplest input. It is usually used for DVD-style images and other single-file disc dumps.

The app checks readability, path safety, and output location before it starts chdman. If the file is on a slow or unreliable drive, copy it locally first.

## CUE/BIN

Add the `.cue` file. The `.bin` files are track data; the CUE tells the app how they belong together.

The app checks referenced files and blocks unsafe references that point outside the disc folder. That is intentional — CUE files are text and can point to surprising places.

## GDI and TOC

GDI and TOC are descriptor-based inputs, similar in idea to CUE.

Keep the descriptor and all track files in the same folder. Renaming one track file by hand is enough to break the set.

## BIN and RAW

Standalone BIN or RAW files are not blindly treated as complete discs.

If you have a CUE, use it. Without a descriptor, the app needs enough evidence to choose a safe workflow.

## NRG and CDI

NRG is accepted only where the current workflow can handle it safely.

CDI is not supported in the current workflow.

## Hard disk images (IMG/HDD)

Raw hard-disk images (`.img`, `.hdd`) are converted with `chdman createhd`, but only when the "Hard disk image" CHD profile is selected in Options. A raw image has no signature, so Auto mode never guesses: it stops with a message that names the profile to choose.

An `.img` is kept out of the queue, and refused even with the profile selected, when it looks like part of a CD/DVD:

- a `.cue`, `.ccd`, `.toc`, `.gdi` or `.mds` with the same name sits next to it,
- a CUE, GDI or TOC in the same folder references it,
- it starts with a CD sync header or carries an ISO9660 volume descriptor.

The file size must be a non-zero multiple of 512 bytes. Compression and hunk size are left to chdman's hard-disk defaults. Extraction back to `.img` uses `extracthd`.

## CHD

Existing CHD files can be verified.

Extraction depends on the CHD type:

- CD or GD-ROM CHD normally extracts to CUE/BIN.
- DVD CHD normally extracts to ISO.
- Hard disk CHD normally extracts to IMG.

The app does not re-convert CHD files just because they appear in the queue.

## Archives

ZIP, RAR, and 7Z are containers. The app opens the archive, stages a candidate image, checks it, and then uses the normal workflow.

Password-protected, damaged, incomplete, or deeply nested archives may stop early. Good. A broken archive should fail before conversion starts.

## CSO

CSO input is prepared first, then converted through the normal CHD path.

If CSO preparation fails, the job stops there. The app should not pretend a CHD was created.

## ZSO

ZSO (LZ4-compressed ISO, used by OPL for PS2 and by some PSP tools) is accepted only when the file starts with the `ZISO` signature. It is expanded to a temporary ISO with CsoKit 0.6.4 or newer, then converted with `createdvd`. The original ZSO is not modified.

CsoKit's `info` and shallow `verify` are CSO-only, so the app reads the declared size with `detect`, lets `decompress` reject damaged data, and requires the temporary ISO to be exactly the declared size. A damaged or truncated ZSO stops before chdman runs. An external CsoKit older than 0.6.4 is refused for ZSO with a message.

ZSO inside archives and source cleanup after conversion are not supported yet.

## PS2 disc images

PS2 guidance is based on disc evidence where possible, including `SYSTEM.CNF` inside supported ISO or CUE/BIN layouts.

This does not turn `SYSTEM.CNF` into a standalone input type. It is a scanner signal used to explain the disc better.

## PS3-related input

PS3 support is experimental and mostly detection-oriented.

The app may detect PS3 folders, PS3 ISO evidence, package-style input, or existing CHD files for information and verification paths. Detection does not mean the selected action is valid.

See [PS3 experimental support](PS3_EXP.md).

## Redump data

Redump data is optional and user-provided.

The release does not include Redump databases, DAT files, games, or disc images.
