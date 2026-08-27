Run the Valkur Unity test suite via MCP, poll until done, report pass/fail summary. Optional argument after the workflow name: `edit` (default) | `play` | `all` | a specific test name filter.

Run the Unity tests for Valkur and report results.

## Workflow

1. **Parse the argument** (text the user appended after `/unity-tests.md`):
   - empty or "edit" → `mode="EditMode"`
   - "play" → `mode="PlayMode"`
   - "all" → run EditMode first, then PlayMode
   - anything else → treat as a test name filter for `test_names=["<filter>"]`

2. **Refresh first** if it's been a while or files changed:
   ```
   unityMCP__refresh_unity(scope="scripts", mode="if_dirty", wait_for_ready=true)
   ```

3. **Run**:
   ```
   job = unityMCP__run_tests(mode=<mode>, include_failed_tests=true [, test_names=[...]])
   ```
   For PlayMode runs pass `init_timeout=120000` (domain reload makes PlayMode slow to start).

4. **Poll** until the job reaches a terminal state:
   ```
   result = unityMCP__get_test_job(job_id=job.job_id, wait_timeout=45, include_failed_tests=true)
   ```
   The `wait_timeout` parameter blocks until completion, so usually one or two calls suffice. On each poll, also check `result.failures_so_far` for early signals.

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

6. **If failures** → suggest adopting the `unity-tester` role (`.clinerules/agents/unity-tester.md`) to fix them; do not attempt fixes inside this workflow.

If MCP is unavailable, fall back to the CLI runner (and say so). The Unity editor path is in `.clinerules/settings.json` under `env.UNITY_EDITOR`:

```powershell
& "$env:UNITY_EDITOR" -batchmode -nographics -silent-crashes `
  -projectPath unity/Valkur `
  -runTests -testPlatform EditMode `
  -testResults TestResults.xml -logFile -
```
