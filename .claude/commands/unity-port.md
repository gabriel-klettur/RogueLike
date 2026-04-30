---
description: Walk the canonical Python → Unity porting workflow for the file or system named in the argument. Coordinates `python-analyst` → optional `data-migrator` → `unity-architect` → `unity-tester` → `unity-mcp-guardian`.
argument-hint: "<python source path or system name, e.g. 'roguelike_engine/map/pathfinding.py' or 'pathfinding'>"
---

Port the system named in `$ARGUMENTS` from Python to Unity, end to end. Do not skip steps.

## Workflow

### 1. Analyze (python-analyst)

Invoke the `python-analyst` agent. Brief it with:
- The exact Python path or system name from `$ARGUMENTS`.
- The deliverable: structured analysis (Purpose, Algorithm, Key values, Dependencies, Unity equivalent if any, Migration notes).

Wait for the analysis. Do not proceed without it.

### 2. (Conditional) Data conversion (data-migrator)

If the system has JSON/SQLite data under `python/data/` that maps to ScriptableObjects, invoke `data-migrator` with:
- The Python data files listed by the analyst.
- The target Unity ScriptableObject class (or "needs creation").
- Run dry-run first. Get the field mapping table back.

### 3. Implement (unity-architect)

Invoke the `unity-architect` agent. Brief it with:
- The analyst's output.
- The data-migrator's mapping (if applicable).
- Explicit numerical values to preserve (Python → Unity unit conversions).
- Target assembly + folder.
- Hand-off rule: if the system is the Buildings Editor or Tile Editor, route instead to `buildings-editor` or `tile-editor`.

### 4. Test (unity-tester)

Invoke `unity-tester`. Brief it to:
- Create EditMode tests for the new public surface in the canonical folder + namespace.
- For combat / spell / formula systems, write parity tests that load both the Python JSON (if present in `Resources` or `StreamingAssets`) and the C# ScriptableObject, asserting numerical equality.
- Run the suite and report.

### 5. Verify (unity-mcp-guardian)

Invoke `unity-mcp-guardian` to confirm:
- `mcp_unity_refresh_unity` returns clean.
- `mcp_unity_read_console` shows zero errors and zero actionable warnings.
- Tests pass.

### 6. Final report

```
Ported: <system>
Files created: <list>
Files modified: <list>
ScriptableObjects added: <list>
Tests added: <list>
Numerical parity: ✅ verified | ⚠️ approximated (with delta) | ❌ missing
Console: clean
```

## Constraints

- Never modify Python source.
- Never copy Udemy_Inspiration code wholesale. If you need a pattern from there, route through `udemy-inspiration` agent.
- Hand off to specialist agents — do not do everything yourself in this command.

System to port: `$ARGUMENTS`
