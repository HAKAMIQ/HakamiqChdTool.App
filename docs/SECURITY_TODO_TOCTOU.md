# Security TODO — TOCTOU in media intake (deferred)

Status: POTENTIAL RISK — reproduced, NOT exploited. Severity: LOW.
Date: 2026-11-05

## What
Race window between path validation and file open in Core/Input/MediaClass.cs
(validation at ~line 111, probe re-open at ~line 407). Reproduced in 4 scenarios
(regular file, parent junction, file symlink, missing-path-then-symlink): original
handle keeps reading file A while the classifier reads file B's header via the same
path. chdman later re-opens by path too, so securing the probe alone is insufficient.

## Why deferred
- Local single-user desktop tool; attacker needs local write access to the working
  folder and must win the race mid-conversion. Low real-world exploitability.
- No data leak / code exec / privilege bypass demonstrated.
- Proper fix needs handle-based validation + Windows native APIs (NtCreateFile /
  OBJ_DONT_REPARSE), which is complex and out of current scope.

## Future fix (own scoped task)
- Handle-based open: validate the opened object, not the path string.
- Keep the reproduction test (branch test/p0-toctou-reproduction) and convert it
  to a security-regression test that REJECTS the swapped target after the fix.
- P0 security status stays PENDING until fixed or risk formally accepted.
