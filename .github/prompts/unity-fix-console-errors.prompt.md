---
description: "Check the Unity Console via MCP for errors/warnings and fix them using Valkur project conventions."
name: "Unity: Fix Console Errors"
argument-hint: "Optional: specific system or file to focus on (e.g. 'BuildingsRuntimeEditor')"
agent: "agent"
tools: ["read_file", "replace_string_in_file", "multi_replace_string_in_file", "grep_search", "file_search", "semantic_search", "get_errors"]
---

Load and follow the unity-development skill before doing anything:
[unity-development skill](../skills/unity-development/SKILL.md)

## Task

Check the Unity Console via MCP and fix every error and actionable warning found,
following Valkur project conventions to the letter.

## Step-by-step Workflow

### 1 — Trigger compilation and read the console

```
mcp_unity_refresh_unity  compile=request  mode=force  scope=scripts  wait_for_ready=true
mcp_unity_read_console   types=["error","warning"]  page_size=50  format=detailed  include_stacktrace=true
```

If no errors or warnings are reported → state clearly **"Console is clean. Nothing to fix."** and stop.

### 2 — Triage

For each log entry, extract:
| # | Type | Message summary | File : Line | Root cause hypothesis |
|---|------|-----------------|-------------|-----------------------|

Prioritise: **errors first**, then **warnings that indicate broken behaviour**.

Skip these benign warnings (do not attempt to fix):
- MCP WebSocket reconnect after domain reload
- `Default GameObject Tag: X already registered`
- `LogAssert.ignoreFailingMessages` in EditMode tests

### 3 — Research before touching code

For each error:
1. Open the offending file with `read_file` (±20 lines around the reported line).
2. If the symbol is unfamiliar, use `grep_search` / `semantic_search` to locate its definition.
3. Check [copilot-instructions](./../copilot-instructions.md) for the relevant "Key Gotchas" section.

### 4 — Fix

Apply all fixes using `multi_replace_string_in_file` for changes in the same file;
use parallel calls for independent files.

Rules:
- **Never use public fields** — use `[SerializeField] private` + `[Tooltip]`.
- **Never raw singletons** — use `ServiceLocator`.
- **Preserve game feel** — never change numeric constants unless the bug is the wrong value.
- **Minimal diff** — fix only what is broken; do not refactor unrelated code.
- **Assembly boundaries** — `Valkur.Gameplay` must not reference `Valkur.UI`.
- For `InventorySlot` comparisons use `.IsEmpty`, never `== null`.
- For `SpellDefinition` use `cooldownDuration`, not `cooldown`.

### 5 — Verify

After every fix cycle:

```
mcp_unity_refresh_unity  compile=request  mode=force  scope=scripts  wait_for_ready=true
mcp_unity_read_console   types=["error","warning"]  page_size=50  format=detailed  include_stacktrace=true
```

Repeat Steps 2-5 until the console shows 0 errors and 0 actionable warnings.

### 6 — Report

Produce a concise summary table:

| File | Error / Warning fixed | Fix applied |
|------|-----------------------|-------------|

End with: **"Console clean. N issues fixed."**

---

> If ${{ input:focus }}` is provided, restrict research and fixes to files related to that system.
> If not provided, fix all errors project-wide.
