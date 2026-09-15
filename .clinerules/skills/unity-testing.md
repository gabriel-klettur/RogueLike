
# Unity Testing — Valkur

The full canonical knowledge base lives at:

**[.github/skills/unity-testing/SKILL.md](../../.github/skills/unity-testing/SKILL.md)**

Read it directly when you need:

| Need | Section in source |
|---|---|
| Where a test goes: root = production layer (one assembly each), feature = production folder, aliases | "Where a test goes" |
| Categories (`TestCategories`), `TestReflection`, ratchets and baselines, `TestRunRecorder` | "Categories" … "Measuring" |
| Namespace enforcement script (`enforce-namespaces.ps1`) | "Namespace script" |
| Running tests via MCP (full suite, by name, polling) | "Running Tests via MCP" |
| Test template (using namespace, TearDown, [Test] vs [UnityTest]) | "Writing New Tests" |
| EditMode gotchas (TMP NRE, [SerializeField] traps, Unity null, LogAssert, renderer.material, Input System) | "EditMode Gotchas" |
| File, fixture and test-name rules (one type per file, partials, `Subject_Scenario_Outcome`) | "Files and fixtures" |
| Audit checklist after reorganization | "Audit Checklist" |

## Quick reference (most-used)

### Namespace formula

```
namespace = "Valkur.Tests." + path-segments-below-Tests/-joined-with-"."
```

Example: `Assets/Tests/EditMode/Gameplay/Combat/Death/DeathSequenceControllerTests.cs` → `namespace Valkur.Tests.EditMode.Gameplay.Combat.Death`.

The ROOT (`Core`, `Data`, `Infrastructure`, `UIKit`, `Gameplay`, `UI`, `Editors`, `EditorTools`, `Project`) is the highest
production layer the test needs; each is its own assembly. `TestLayoutConventionTests` enforces all of it.

### MCP run + poll

```
job = unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
loop:
  result = unityMCP__get_test_job(job_id=job.job_id)
  inspect result.failures_so_far
  break when result.status in {succeeded, failed}

if result.summary.failed > 0: list each failed test
else: "All N tests passing ✓"
```

### Enforce namespaces

```powershell
.\.github\skills\unity-testing\scripts\enforce-namespaces.ps1
# add -Fix to auto-correct mismatches
```

### EditMode gotchas — quick fixes

- TMP NRE → use `CreateInitializedUI()` to get a fully-Unity-lifecycle initialized ref.
- `renderer.material` leak warning → use `sharedMaterial` or `LogAssert.ignoreFailingMessages = true`.
- Stale [SerializeField] refs → drop `[SerializeField]` from runtime-only private fields.
- Unity fake-null → `Assert.IsTrue(go != null, "...")` not `Assert.IsNotNull(go)`.
