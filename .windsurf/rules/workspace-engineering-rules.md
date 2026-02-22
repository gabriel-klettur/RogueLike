---
trigger: manual
---
# Workspace Engineering Rules (All Stacks)

## Operating model
- Work through Windsurf + MCP tools.
- Prefer deterministic, minimal, traceable edits.
- Never assume runtime state; verify from code, scene config, or logs.

## Architecture discipline
- Keep strict separation: UI/Presentation, Gameplay/Domain, Data, Infrastructure.
- Fix root causes upstream before downstream patches.
- Avoid hidden side effects and global mutable coupling.

## Quality and safety
- Treat all external input as untrusted.
- Handle errors explicitly; do not swallow exceptions silently.
- Add validation/testing for non-trivial logic.
- Document intent for complex logic and migration-sensitive decisions.

## Workflow discipline
- Before coding: map current flow and impacted modules.
- During coding: keep changes focused and reviewable.
- After coding: validate happy path, edge path, and regression path.
- Summarize final changes with exact file references.
