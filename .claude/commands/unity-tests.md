---
description: Run the Valkur Unity test suite via MCP, poll until done, report pass/fail summary. Defaults to EditMode; pass "play" to run PlayMode, "all" for both, or a namespace/fixture filter to run just those.
argument-hint: "[optional: 'edit' (default) | 'play' | 'all' | 'full' | a fixture or namespace filter]"
---

Run the Unity tests for Valkur and report results.

## Cost, measured — read this before running the whole suite

| Run | Tests | Wall clock |
|---|---|---|
| Full EditMode suite | 7,236 | **~2–2.3 min** (126 s and 138 s over two runs — ±10 s is normal run-to-run noise; was 343 s before the Items table was virtualised and the JSON parser stopped hanging) |
| One fixture group (e.g. 4 fixtures) | 29–47 | **1.5–3 s** |
| Compile of the test assembly after an edit | 664 files | 7–30 s |

That is still a ~50x difference for the same compile. **Iterate with a filter; run the full
suite before a commit or a merge, not between edits.**

When the suite DOES get slow, do not guess which tests — bisect it by namespace with
`test_names=[prefix]` and read only `durationSeconds` (the per-test detail payload for the
whole suite does not survive the bridge). That is how 52 % of a 343 s suite was traced to
37 tests, one method and one number: `ItemsRuntimeEditor.RefreshTable` building 6,840 widgets —
and how a single 20-second test (`SaveSets_RefusesWrite_AfterMalformedSetsJson`) turned out to
be a JSON parser looping to OutOfMemory on any corrupted file. Read per-test durations for a
suspect group with `include_details=true`; a test that passes in 20 s is a bug report.

## Workflow

1. **Parse `$ARGUMENTS`**:
   - empty or "edit" → `mode="EditMode"`, ask the user for a filter if the change was local
   - "full" → `mode="EditMode"`, no filter, the whole suite
   - "play" → `mode="PlayMode"`
   - "all" → EditMode first, then PlayMode
   - anything else → a filter for `test_names=[...]`; a NAMESPACE PREFIX works and is usually
     what you want (e.g. `Valkur.Tests.EditMode.Game.Input`)

2. **Refresh and verify the compile actually landed.**

   ```text
   mcp_unity_refresh_unity(scope="all", mode="force", compile="request", wait_for_ready=true)
   mcp_unity_read_console(types=["error","warning"], format="detailed")
   ```

   `mode` accepts only `if_dirty` or `force` — `"normal"` throws a ValidationError. Use
   `scope="all"`, not `"scripts"`, after any `.asset` edit: `scope="scripts"` does not reimport
   an asset changed on disk and leaves Unity holding the stale object.

   **A clean console is not a successful compile.** Confirm the assembly is newer than the
   newest source file before trusting anything:

   ```csharp
   var asm = AppDomain.CurrentDomain.GetAssemblies()
       .First(a => a.GetName().Name == "Valkur.Tests.EditMode");
   var newest = Directory
       .GetFiles(Path.Combine(Application.dataPath, "Tests"), "*.cs", SearchOption.AllDirectories)
       .Select(File.GetLastWriteTimeUtc).Max();
   return File.GetLastWriteTimeUtc(asm.Location) >= newest;
   ```

3. **Check the editor is out of Play Mode.** `run_tests` refuses outright with
   *"Cannot start a test run while the Editor is in or entering Play Mode"* — and that refusal
   reaches this side DISGUISED as `no_unity_session` or `disconnected while awaiting
   command_result`, which reads like a transport problem and is not. Stop Play Mode first
   (`manage_editor(action="stop")`, allowed without asking) and confirm `isPlaying == false`.

4. **Run**:

   ```text
   job = mcp_unity_run_tests(mode=<mode>, include_failed_tests=true [, test_names=[...]])
   ```

   **A failed `run_tests` may have started a runner anyway.** Treat any error on it as UNKNOWN,
   never as "did not happen" — measured, `no_unity_session; please retry` had already started
   one. Before retrying, probe the Unity-side runner read-only through `execute_code`: reflect
   `UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder` out of the
   `UnityEditor.TestRunner` assembly, reach live instances with
   `Resources.FindObjectsOfTypeAll`, and read `TestRuns` for `guid` / `startTime` / `isRunning`.
   Two details are load-bearing: reflect with `BindingFlags.FlattenHierarchy`, or inherited
   statics do not resolve and the probe reports a confident, wrong nothing; and never touch
   `ScriptableSingleton.instance`, which CREATES the asset and turns the probe into a write.

   Two runners in one editor cross each other's log windows and turn green tests red.

5. **Poll** until `job.status` is `succeeded` or `failed`, using `wait_timeout` (30–120 s) rather
   than a tight loop:

   ```text
   result = mcp_unity_get_test_job(job_id=job.job_id, include_failed_tests=true, wait_timeout=90)
   ```

   `get_test_job` is a READ, so retrying it after a dropped connection is safe.

6. **VERIFY `total` IS THE NUMBER YOU EXPECTED.** A filter that matches nothing returns
   `total: 0` and `status: succeeded` — a green result over zero tests. This has already
   happened once: `Valkur.Tests.EditMode.Editors.Tile.TileEditorColliderTests` matched 0 because
   the real namespace is `...Editors.TileEditor.UI`. If `total` is 0 or far below what the filter
   should match, the filter is wrong and the run proved nothing.

7. **Report** in this exact shape:

   ```text
   Suite: <EditMode | PlayMode | both>   Filter: <none | ...>
   Passed: X / Y      (Y must match what the filter should have selected)
   Duration: Z s

   Failures:
   - <full test name>
     <one-line root cause>
     <file:line if known>

   Console after run: clean | N warnings (listed)
   ```

8. **If failures** → investigate every one. Never label a red test "pre-existing" or "unrelated"
   without confirming it: check whether the file is in `git status`, and whether the data it
   asserts on is actually wrong on disk. A red test whose data is correct on disk is usually a
   memory/disk divergence, not a data defect — see the `MonoScript.GetClass()` note in CLAUDE.md.
   Suggest `unity-tester` for fixes; do not fix inside this command.

If MCP is unavailable, fall back to the CLI runner (and say so):

```bash
"$UNITY_EDITOR" -batchmode -nographics -silent-crashes \
  -projectPath unity/Valkur \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml -logFile -
```

Argument: `$ARGUMENTS`
