# Supported formats

This page explains how Hakamiq CHD Tool handles input files.

A file can be convertible, verifiable, extractable, detected only, or
unsupported. Detection does not always mean conversion.

## Support levels

Convertible means the app can prepare a CHD creation job.

Verifiable means the app can check an existing CHD.

Extractable means the app can run a supported extraction path.

Detected only means the app can recognize the input, but may not convert
it.

Unsupported means the app should stop before conversion.

## ISO

ISO is the normal single-file input.

The app can convert it when the source passes the current media and
safety checks.

## CUE, GDI, and TOC

These are descriptor-based inputs.

Add the descriptor file, not only the track files. The referenced files
must stay beside it and keep their expected names.

## BIN and RAW

BIN and RAW are usually track files.

They are not treated as blindly safe standalone inputs. The app needs a
valid descriptor or enough evidence to choose a safe workflow.

## NRG and CDI

NRG is accepted only where the current workflow can handle it safely.

CDI is not supported in the current workflow.

## CHD

Existing CHD files can be verified.

Extraction is available only when the CHD type and selected profile are
supported.

The app should not re-convert an existing CHD just because it is in the
queue.

## Archives

ZIP, RAR, and 7Z are treated as containers.

The app stages a candidate image from the archive, checks it, then runs
the normal workflow.

Encrypted, incomplete, nested, or damaged archives may fail before
conversion starts.

## CSO

CSO is handled as input preparation.

The app prepares a temporary ISO, then sends that through the normal CHD
workflow.

If CSO preparation fails, the job stops there.

## PS3-related input

PS3 support is experimental.

The app may detect PS3 folders, PS3 ISO evidence, package-style input,
or existing CHD files for verify or info-style handling.

Detection does not guarantee that conversion is the right action.

See [PS3 experimental support](PS3_EXPERIMENTAL.md).

## Redump data

Redump data is optional and user-provided.

The release package does not include Redump databases, DAT files, games,
or disc images.
