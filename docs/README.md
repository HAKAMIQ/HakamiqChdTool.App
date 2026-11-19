# Documentation

Start with the [main README](../README.md) for download and quick start. The
pages below go into more detail.

## Using the app

| Page | What it covers |
| --- | --- |
| [User guide](USER_GUIDE.md) | Installing, the queue workflow and safe habits. |
| [Supported formats](FORMATS.md) | What can be converted, verified, extracted, detected or rejected. |
| [Conversion options](CONV_OPTS.md) | Convert, Verify and Extract, output names and temporary files. |
| [Errors and logs](ERRORS.md) | Common failures and what to include in a report. |
| [PS3 notes](PS3_EXP.md) | Experimental detection for PS3-related input. |

## Library organizer and igir

| Page | What it covers |
| --- | --- |
| [Library organizer design](LIB_ORGANIZER.md) | Goals, safety rules and target layout. |
| [Organizer status](ORGANIZER_STATUS.md) | What is implemented and how it was checked. |
| [igir integration](IGIR_INTEGRATION.md) | The optional, report-only igir provider. |

## Architecture and development

| Page | What it covers |
| --- | --- |
| [Architecture](ARCHITECTURE.md) | App structure and main components. |
| [Architecture boundaries](architecture/ARCH_BOUND.md) | Rules for `Core/Workflow` and layering. |
| [Security boundaries](architecture/SECURITY_BOUNDARIES.md) | Trust boundaries for files, paths and external tools. |
| [chdman integration](CHDMAN.md) | How chdman jobs are prepared and run. |
| [Contributing](../CONTRIBUTING.md) | Build, test and change rules. |
| [Smoke test checklist](SMOKE.md) | Manual checks before publishing a release. |

## Security and audit records

| Page | What it covers |
| --- | --- |
| [Security policy](../SECURITY.md) | How to report a vulnerability. |
| [Security assurance](security/SECURITY_ASSURANCE.md) | Implemented controls and remaining work. |
| [Independent audit scope](security/INDEPENDENT_AUDIT_SCOPE.md) | Scope and exit criteria for an external review. |
| [CHD/CSO corpus guide](security/CORPUS_GUIDE.md) | Testing with user-owned media kept outside the repository. |
| [Deferred TOCTOU note](SECURITY_TODO_TOCTOU.md) | Known low-severity race in input validation. |
| [Symlink test verification](SYMLINK_VERIFICATION.md) | Running the tests that need symbolic-link rights. |
| [Intake validation (P0)](P0-Unified-Intake-Validation.md) | Validation record for the unified input path. |
| [Audit coverage](AUDIT_COVERAGE.md) and [file ledger](AUDIT_FILES.md) | What the code audit read and which tests cover it. |

## Releases and legal

- [Changelog](release-notes/CHANGELOG.md)
- [Bundled tool versions](../Tools/VERSIONS.md)
- [Legal notice](legal/LEGAL.md)
- [Third-party notices](legal/3P_NOTICE.txt)
- [SBOM (CycloneDX)](sbom.cdx.json)
