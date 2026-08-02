# User guide

This guide covers the normal local workflow for Hakamiq CHD Tool. For format-specific details, see [Supported formats](FORMATS.md).

## Before you start

Extract the release ZIP to a normal local folder before running the app. Do not run it from inside the ZIP, from a repository `bin` or `obj` folder, or from a protected Windows folder.

A short writable path such as `C:\Tools\HakamiqChdTool` is a good choice. Start `HakamiqChdTool.exe` from the extracted release folder.

The runtime-required package needs the Microsoft .NET 10 Desktop Runtime for Windows x64.

## Basic workflow

1. Open Hakamiq CHD Tool.
2. Add a supported source file or folder.
3. Review the detected input and any safety warning.
4. Choose an output folder with enough free space.
5. Select Convert, Verify, or Extract as appropriate.
6. Start the queue and review the final result for every item.

Start with one file before processing a large batch. It is easier to confirm the output location and behavior with a small first run.

## Multi-file disc images

Keep descriptor files and their tracks together. For example, add the CUE file for a CUE/BIN image and keep every referenced BIN beside it. Do not rename or move only part of a disc set.

The app refuses missing or unsafe references instead of guessing the disc layout.

## Output and queue results

Use a stable local drive when possible. Avoid protected, cloud-synced, failing, or nearly full storage for large conversions.

Each queue item ends with a distinct result such as success, failure, skip, or cancellation. An existing destination may be skipped to protect it from accidental replacement.

Canceling a running job can leave a partial temporary or output file. Review the destination before retrying the same item.

## Verification and metadata

Redump or DAT information is optional and user-provided. It can help identify or compare a disc, but it does not prove that a damaged or incomplete source is safe to convert.

PS3-related detection remains experimental. Recognizing an input does not mean that conversion is supported.

## After a run

Review the result of every queue item. If an item fails repeatedly at the same point, check the source file, companion files, free space, storage health, and the displayed log before retrying.

See [Errors and logs](ERRORS.md) for common failures and the information to include in a report.
