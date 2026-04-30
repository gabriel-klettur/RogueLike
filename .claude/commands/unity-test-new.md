---
description: Scaffold a new Unity test for the system named in the argument, in the correct folder + namespace per the canonical Valkur test map.
argument-hint: "<system or class name, e.g. 'TileBrush', 'BuildingsRuntimeEditor', 'MeleeCombat'>"
---

Create a new test file for `$ARGUMENTS` following Valkur conventions.

## Workflow

Invoke the `unity-tester` agent with:
- System name from `$ARGUMENTS`.
- Instructions:
  1. Read [.github/skills/unity-testing/SKILL.md](../../.github/skills/unity-testing/SKILL.md) for the canonical folder→namespace map.
  2. Locate the production class for `$ARGUMENTS` (`Grep` `Assets/_Project/Scripts/`).
  3. Decide:
     - Editor-only (lives under `Scripts/Editor/`) → place test under `Tests/EditMode/Editors/<Subsystem>/`.
     - Runtime under Gameplay → `Tests/EditMode/Game/<Domain>/` for headless logic, `Tests/PlayMode/<Domain>/` for scene-dependent tests.
  4. Use the canonical template from the skill:
     - `[Test]` for sync, `[UnityTest]` + `yield return null` for async.
     - `[TearDown]` to destroy any GameObjects created.
     - `LogAssert.ignoreFailingMessages = true` if the test creates UI in EditMode.
     - `Assert.IsTrue(go != null, "...")` (not `IsNotNull`) for Unity-fake-null safety.
  5. Test names: `SystemUnderTest_Condition_ExpectedResult`.
  6. Namespace = `Valkur.Tests.` + path segments below `Tests/` joined with `.`
- After scaffolding, run the new test once via MCP and report pass/fail.

## Output

```
Created: <path/to/NewSystemTests.cs>
Namespace: <full namespace>
Run result: PASSED / FAILED
Console: clean | N warnings
```

System to test: `$ARGUMENTS`
