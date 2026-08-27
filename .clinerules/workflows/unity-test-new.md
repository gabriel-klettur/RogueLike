Scaffold a new Unity test for the system named in the argument (text appended after `/unity-test-new.md`), in the correct folder + namespace per the canonical Valkur test map.

Create a new test file for the named system following Valkur conventions.

## Workflow

Adopt the `unity-tester` role (read `.clinerules/agents/unity-tester.md` first) and:

1. Read `.github/skills/unity-testing/SKILL.md` for the canonical folder→namespace map.
2. Locate the production class for the named system — `search_codebase` under `unity/Valkur/Assets/_Project/Scripts/`.
3. Decide:
   - Editor-only (lives under `Scripts/Editor/`) → place test under `Assets/Tests/EditMode/Editors/<Subsystem>/`.
   - Runtime under Gameplay → `Assets/Tests/EditMode/Game/<Domain>/` for headless logic, `Assets/Tests/PlayMode/<Domain>/` for scene-dependent tests.
4. Use the canonical template from the skill:
   - `[Test]` for sync, `[UnityTest]` + `yield return null` for async.
   - `[TearDown]` to destroy any GameObjects created.
   - `LogAssert.ignoreFailingMessages = true` if the test creates UI in EditMode.
   - `Assert.IsTrue(go != null, "...")` (not `IsNotNull`) for Unity-fake-null safety.
5. Test names: `SystemUnderTest_Condition_ExpectedResult`.
6. Namespace = `Valkur.Tests.` + path segments below `Tests/` joined with `.`
7. Create the file with `unityMCP__create_script` (it handles the `.meta`), or with `editor` + a matching `.meta` if the former is unavailable.
8. After scaffolding, run the new test once via MCP and report pass/fail:
   ```
   unityMCP__refresh_unity(scope="scripts", mode="if_dirty", wait_for_ready=true)
   job = unityMCP__run_tests(mode="EditMode", test_names=["<NewTestClass>"])
   unityMCP__get_test_job(job_id=job.job_id, wait_timeout=60)
   ```

## Output

```
Created: <path/to/NewSystemTests.cs>
Namespace: <full namespace>
Run result: PASSED / FAILED
Console: clean | N warnings
```
