# Security Policy

## Supported versions

Security reports are accepted for the latest public release and the current `main` branch.

Older test builds, private archives, and outdated release candidates are not supported.

## Reporting a vulnerability

Do not open a public GitHub issue for security problems.

Use GitHub private vulnerability reporting / Security Advisories if available, or contact the maintainer privately before sharing details in public.

Please include:

- affected version, release, or commit
- clear steps to reproduce the issue
- what you expected to happen
- what actually happened
- relevant logs with usernames, personal file paths, tokens, and private data removed
- whether the issue involves bundled tools such as `chdman.exe` or 7-Zip components

Do not attach games, ROMs, BIOS files, disc images, Redump databases, decryption keys, platform firmware, or copyrighted user media to a security report.

## In scope

Security reports may include:

- crashes caused by crafted local files
- unsafe archive extraction
- path traversal
- unsafe overwrite or deletion behavior
- command execution risks
- bundled-tool handling issues
- sensitive data written to logs

## Out of scope

The following are not handled as project security issues:

- requests for games, ROMs, BIOS files, disc images, Redump databases, keys, or platform firmware
- reports about illegal media distribution
- issues caused by modified third-party binaries not shipped by this repository
- unsupported operating systems
- unofficial builds

This project does not provide games, ROMs, BIOS files, copyrighted disc images, decryption keys, platform firmware, or Redump databases.

Users are responsible for processing only files they have the legal right to use.