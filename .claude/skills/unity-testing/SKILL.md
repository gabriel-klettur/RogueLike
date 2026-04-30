---
name: unity-testing
description: Valkur Unity test conventions — folder→namespace map, EditMode gotchas (TMP NRE, Unity null vs C# null, `renderer.material` leaks, `LogAssert.ignoreFailingMessages`), test template, MCP run/poll pattern, namespace enforcement script. Load before creating, fixing, or reorganizing tests under `unity/Valkur/Assets/Tests/`.
---

# Unity Testing — Valkur

The full canonical knowledge base lives at:

**[.github/skills/unity-testing/SKILL.md](../../../.github/skills/unity-testing/SKILL.md)**

Read it directly when you need:

| Need | Section in source |
|---|---|
| Folder → Namespace map (canonical, drives Test Runner hierarchy) | top |
| Where to place a new test (system → folder + namespace) | "Where to place" |
| Namespace enforcement script (`enforce-namespaces.ps1`) | "Enforce Namespaces" |
| Running tests via MCP (full suite, by name, polling) | "Running Tests via MCP" |
| Test template (using namespace, TearDown, [Test] vs [UnityTest]) | "Writing New Tests" |
| EditMode gotchas (TMP NRE, [SerializeField] traps, Unity null, LogAssert, renderer.material, Input System) | "EditMode Gotchas" |
| Class name rules (one public class per file; collision rename) | "Class Name Rules" |
| Audit checklist after reorganization | "Audit Checklist" |

## Quick reference (most-used)

### Namespace formula

```
namespace = "Valkur.Tests." + path-segments-below-Tests/-joined-with-"."
```

Example: `Assets/Tests/EditMode/Game/Combat/MeleeCombatTests.cs` → `namespace Valkur.Tests.EditMode.Game.Combat`.

### MCP run + poll

```
job = mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)
loop:
  result = mcp_unity_get_test_job(job_id=job.job_id)
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
