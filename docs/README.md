# Documentation

The main README is for normal use. This folder goes deeper: supported inputs, conversion behavior, chdman integration, logs, and project structure.

If you only want to convert a few files, start with the root README. Come here when something is unclear or when you need to report a problem.

## User guides

- [Supported formats](SUPPORTED_FORMATS.md) — what the app can convert, verify, extract, detect, or reject.
- [Conversion options](CONVERSION_OPTIONS.md) — Convert, Verify, Extract, queue results, output names, and temporary files.
- [Errors and logs](ERRORS_AND_LOGS.md) — common failures and what to include in a useful report.
- [PS3 experimental support](PS3_EXPERIMENTAL.md) — limited detection notes for PS3-related input.

## Technical notes

- [chdman integration](CHDMAN_INTEGRATION.md) — how the app prepares and runs chdman without exposing command lines to normal users.
- [Architecture](ARCHITECTURE.md) — current app structure and boundaries.
- [Architecture boundaries](architecture/ARCHITECTURE_BOUNDARIES.md) — the current decision around `Core/Workflow`.

## Release and legal

- [Changelog](release-notes/CHANGELOG.md)
- [Legal notice](legal/LEGAL.md)
- [Third-party notices](legal/THIRD_PARTY_NOTICES.txt)

## Development

- [Contributing](../CONTRIBUTING.md)
- [Smoke test checklist](SMOKE_TEST_CHECKLIST.md)
