# Architecture

Hakamiq CHD Tool is a WPF orchestration app around local CHD workflows.

The app does not try to hide every technical detail, but it keeps the dangerous parts away from the user: command-line arguments, temporary work paths, descriptor dependencies, and process cleanup.

## Main areas

```text
Views / Ui
  WPF windows, dialogs, styles, and layout behavior.

ViewModels
  UI state, commands, queue projection, options, and user-facing flow.

Core
  queue state, workflow orchestration, workflow paths, preparation, execution, cleanup, and result shaping.

Services
  chdman integration, conversion, verification, archive intake, Redump, storage, safety, platform detection, PS3 intake, and helper tooling.

Tools
  bundled or source-side runtime tools such as chdman, 7-Zip, Hakamiq.CsoKit helpers, or read-only helper binaries.

scripts
  local verification, release publishing, release validation, and repository convention checks.
```

## Workflow shape

A typical conversion moves through:

1. intake and classification
2. safety/preflight checks
3. output and pending path planning
4. command preparation
5. external process execution
6. progress reporting
7. verification/post-processing where applicable
8. cleanup and final result mapping

The important part is separation. A bad input should fail in intake or preflight, not halfway through a long conversion.

## Queue model

Queue items keep explicit terminal states: success, failure, cancellation, skipped/existing-output cases, and unsupported inputs. Avoid folding these into one generic result.

## Native helpers

Native tools are allowed only when they serve a narrow purpose and their release packaging is explicitly approved. The WPF app remains C#/.NET 8, and CHD conversion remains `chdman`-based.
