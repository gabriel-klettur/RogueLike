Force-recompile Unity, read the console, and fix every error and actionable warning following Valkur conventions. Optional argument after the workflow name: focus area, e.g. `BuildingsRuntimeEditor` or `tests`.

Cardinal rule: the Unity MCP console must end clean — zero errors, zero actionable warnings.

## What to do

1. **Refresh and read**:
   ```
   unityMCP__refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)
   unityMCP__read_console(action="get", types=["error","warning"], page_size="50", format="detailed", include_stacktrace=true)
   ```

2. **If clean** → state "Console clean. Nothing to fix." and stop.

3. **If dirty** → adopt the `unity-mcp-guardian` role (read `.clinerules/agents/unity-mcp-guardian.md`) with the console output and the focus area (if one was appended to the workflow invocation). The guardian triages, fixes, and re-verifies in a loop.

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
