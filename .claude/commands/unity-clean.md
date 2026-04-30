---
description: Force-recompile Unity, read the console, and (if requested) fix every error and actionable warning following Valkur conventions.
argument-hint: "[optional: focus area, e.g. 'BuildingsRuntimeEditor' or 'tests']"
---

Cardinal rule: the Unity MCP console must end clean — zero errors, zero actionable warnings.

## What to do

1. **Refresh and read**:
   ```
   mcp_unity_refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)
   mcp_unity_read_console(types=["error","warning"], page_size=50, format="detailed", include_stacktrace=true)
   ```

2. **If clean** → state "Console clean. Nothing to fix." and stop.

3. **If dirty** → invoke the `unity-mcp-guardian` agent with the console output and the focus area (if provided in `$ARGUMENTS`). The guardian will triage, fix, and re-verify.

## Skip these benign warnings

- MCP WebSocket reconnect after domain reload
- `Default GameObject Tag: X already registered`
- EditMode `LogAssert.ignoreFailingMessages` informational lines

## Report shape

```
| File | Issue | Fix |
|------|-------|-----|

Console clean. N issues fixed.
```

If you can't reach Unity (not running, MCP disconnected), say so explicitly — do not pretend.

Focus area (if any): `$ARGUMENTS`
