# Tooling map — Claude Code names → Cline names

The agents, skills and workflows in this repo were written against Claude Code
tool names. They have already been translated to Cline names where they appear;
this table is the reference for anything that slipped through or for reading
older docs.

## File / code tools

| Claude Code | Cline |
|---|---|
| `Read` | `read_files` |
| `Grep`, `Glob` | `search_codebase` (or `find_in_file` for one file) |
| `Edit`, `Write` | `editor` |
| `Bash` | `run_commands` (PowerShell; use `cmd /c` when stderr noise matters) |

## MCP for Unity tools

The Claude config used `mcp_unity_*` / `mcp__unity__*` prefixes. Cline exposes
the same server as `unityMCP__*`. Common ones:

| Claude Code | Cline |
|---|---|
| `mcp_unity_refresh_unity` | `unityMCP__refresh_unity` |
| `mcp_unity_read_console` | `unityMCP__read_console` (`action="get"`, `page_size` is a string) |
| `mcp_unity_run_tests` | `unityMCP__run_tests` (PlayMode: pass `init_timeout=120000`) |
| `mcp_unity_get_test_job` | `unityMCP__get_test_job` (supports `wait_timeout` long-poll) |
| `mcp_unity_execute_code` | `unityMCP__execute_code` |
| `mcp_unity_manage_profiler` | `unityMCP__manage_profiler` |
| `mcp_unity_manage_asset` | `unityMCP__manage_asset` |
| `mcp_unity_manage_gameobject` | `unityMCP__manage_gameobject` |
| `mcp_unity_manage_components` | `unityMCP__manage_components` |
| `mcp_unity_manage_script` | prefer `unityMCP__script_apply_edits` / `unityMCP__apply_text_edits` for edits |
| any other `mcp_unity_<x>` | `unityMCP__<x>` (same suffix) |

## Known Cline-side quirks in this environment

- PowerShell treats git's stderr (`LF will be replaced by CRLF` warnings) as a
  command failure. The command usually succeeded — re-check state instead of
  retrying blindly, or wrap with `cmd /c "..."`.
- Git commit messages must be written as **UTF-8 without BOM** or the title
  gains an invisible BOM character.
- `.claude/scheduled_tasks.lock` and similar runtime files belong to Claude
  Code; leave them alone.
