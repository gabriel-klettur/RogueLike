---
description: Run the Valkur Unity test suite via MCP, poll until done, report pass/fail summary. Defaults to EditMode; pass "play" to run PlayMode or "all" for both.
argument-hint: "[optional: 'edit' (default) | 'play' | 'all' | specific test name filter]"
---

Run the Unity tests for Valkur and report results.

## Workflow

1. **Parse `$ARGUMENTS`**:
   - empty or "edit" → `mode="EditMode"`
   - "play" → `mode="PlayMode"`
   - "all" → run EditMode first, then PlayMode
   - anything else → treat as a test name filter for `test_names=[...]`

2. **Refresh first** if it's been a while or files changed:
   ```
   mcp_unity_refresh_unity(scope="scripts", mode="normal", wait_for_ready=true)
   ```

3. **Run**:
   ```
   job = mcp_unity_run_tests(mode=<mode>, include_failed_tests=true [, test_names=[...]])
   ```

4. **Poll** until `job.status` is `succeeded` or `failed`:
   ```
   result = mcp_unity_get_test_job(job_id=job.job_id)
   ```
   On each poll, also check `result.failures_so_far` for early signals.

5. **Report** in this exact shape:

   ```
   Suite: <EditMode | PlayMode | both>
   Passed: X / Y
   Duration: Z s

   Failures:
   - <full test name>
     <one-line root cause>
     <file:line if known>

   Console after run: clean | N warnings (listed)
   ```

6. **If failures** → suggest invoking `unity-tester` to fix them; do not attempt fixes inside this command.

If MCP is unavailable, fall back to the CLI runner (and say so):

```bash
"$UNITY_EDITOR" -batchmode -nographics -silent-crashes \
  -projectPath unity/Valkur \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml -logFile -
```

Argument: `$ARGUMENTS`
