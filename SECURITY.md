# Security Policy

## Supported versions

Security reports are accepted for:

- the latest public release
- the current `main` branch

Older test builds, private ZIP files, and old release candidates are not
supported.

## Reporting a security issue

Do not open a public GitHub issue for security problems.

Use GitHub Security Advisories if available, or contact the maintainer
privately before sharing details in public.

Include:

- affected version, release, or commit
- clear steps to reproduce the issue
- what you expected to happen
- what actually happened
- relevant logs with private data removed
- whether chdman, 7-Zip, or CsoKit is involved

Remove usernames, private paths, tokens, file names, and unrelated logs before
sending a report.

Do not attach games, ROMs, BIOS files, disc images, CHD files, Redump files,
keys, firmware, or copyrighted media.

## In scope

Security reports may include:

- crashes caused by crafted local files
- unsafe archive extraction
- path traversal
- unsafe overwrite or deletion behavior
- command execution risk
- unsafe handling of bundled tools
- sensitive data written to logs

## Out of scope

The following are not handled as project security issues:

- requests for games, ROMs, BIOS files, disc images, or CHD files
- requests for Redump files, keys, or firmware
- reports about illegal media distribution
- issues caused by modified tools not shipped by this repository
- unsupported operating systems
- unofficial builds

## Legal note

This project does not provide games, ROMs, BIOS files, disc images, CHD files,
Redump files, keys, firmware, or copyrighted user files.

Users are responsible for processing only files they have the legal right to
use.
