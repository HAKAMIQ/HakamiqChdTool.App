# Supported formats

Hakamiq CHD Tool is built for CHD workflows, not for converting every disc-like file it can see. A file may be convertible, verifiable, extractable, detected only, or blocked.

That difference matters. Bad input should fail before conversion, not halfway through a long job.

## Support levels

Convertible means the app can prepare a CHD creation job. Verifiable means an existing CHD can be checked. Extractable means the app can use a supported extraction profile. Detected only means the input can be recognized, but conversion stays conservative. Unsupported means the app should stop before conversion.

## Direct disc images

ISO is the normal single-file path. The app can convert it when the source passes media and safety checks.

CUE, GDI, and TOC are descriptor-based inputs. Add the descriptor file, not only the track files. The referenced files must stay beside it and keep their expected names.

BIN and RAW are not blindly safe as standalone inputs. They are usually track files, and the app needs a valid descriptor or enough evidence to build a safe workflow.

NRG is accepted only where the current workflow can treat it safely as a CD-style source. CDI is not supported in the current workflow.

## CHD input

Existing CHD files can be verified. Extraction is available only when the CHD type and selected profile are supported.

The app should not blindly re-convert a CHD just because it is a file in the queue.

## Archives

ZIP, RAR, and 7Z are intake containers. The app stages a candidate image from the archive, checks it, then runs the normal conversion workflow.

Encrypted, incomplete, nested, or damaged archives should fail early. That is the safer outcome.

## CSO input

CSO is not a CHD format. The app treats it as an input preparation step: CSO becomes a temporary ISO, then `chdman` handles the CHD creation.

If the bundled helper rejects the CSO, the job stops there. Better a clear input failure than a questionable output.

## PS3-related files

PS3 support is experimental and conservative. The app may inspect some PS3 folders, package-style inputs, or disc evidence, but it does not promise that every PS3 source should become CHD.

See [PS3 experimental support](PS3_EXPERIMENTAL.md).

## Redump data

Redump data is optional and user-provided. The release package does not include Redump databases, DAT files, games, or disc images.
