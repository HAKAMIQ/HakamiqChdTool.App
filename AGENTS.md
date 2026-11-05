# Agent rules

Read before any change:
- docs/ARCHITECTURE.md
- docs/architecture/ARCH_BOUND.md
- docs/architecture/SECURITY_BOUNDARIES.md
- docs/LIB_ORGANIZER.md and docs/IGIR_INTEGRATION.md for organizer/igir work

Hard rules:
- Never change queue run/cancel/retry behavior or conversion defaults.
- Never mix UI and service changes in one commit.
- Every behavior change ships with tests in HakamiqChdTool.App.Tests.
- No igir source code in this repository: no copied, ported or translated code
  from igir (GPL-3.0). ../igir-ref is read-only reference for formats only.
- igir is used only through `igir report`. No other igir command or write flag.
- The organizer never deletes files. Moves only, journaled, undoable.
- All UI text goes through Resources/ArabicStrings.xaml and EnglishStrings.xaml.
- Do not push, tag or release unless the maintainer asks.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost). The graphify-out/ folder is git-ignored and stays local; it is not committed.