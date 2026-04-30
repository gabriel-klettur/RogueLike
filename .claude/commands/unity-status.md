---
description: Quick Valkur project status — git state, Unity console health, last test summary, migration progress at a glance.
argument-hint: "(no arguments)"
---

Produce a one-screen status report. Be concise.

## Gather

In parallel:

1. **Git state**
   - `git status` (snapshot of changes)
   - `git log -5 --oneline` (recent commits)

2. **Unity console**
   - `mcp_unity_refresh_unity(scope="scripts", mode="normal", wait_for_ready=true)`
   - `mcp_unity_read_console(types=["error","warning"], page_size=20, format="summary")`

3. **Migration roadmap snapshot**
   - Read first 60 lines of [.github/MIGRATION_GUIDE.md](../../.github/MIGRATION_GUIDE.md) for phase totals.
   - Read [.github/MIGRATION_GUIDE.md](../../.github/MIGRATION_GUIDE.md) "Open Work" section.

4. **Test artifacts** (best-effort)
   - Read tail of `unity/Valkur/unity_batch_editmode.log` if present.
   - Read tail of `unity/Valkur/unity_batch_playmode.log` if present.

## Report

```markdown
## Valkur Status — <date>

### Git
- Branch: <name>
- Modified: N files | Untracked: M
- Recent: <last 3 commit subjects>

### Unity
- Console: ✅ clean | ⚠️ X warnings | ❌ Y errors
- Top issues: <brief list if dirty>

### Tests (last run)
- EditMode: <pass/total> | last run <when>
- PlayMode: <pass/total> | last run <when>

### Migration
- Phase totals: <copy from guide>
- Open work (top 3): <bullet list>

### Next sensible step
<one-line recommendation>
```

If MCP / Unity is not running, say so and report only the file-based info.
