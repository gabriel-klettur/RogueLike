---
name: unity-testing
description: "Manage Unity tests for the Valkur project. Use when: creating new test files, fixing failing tests, reorganizing test folders, deciding which folder/namespace/assembly a new test belongs to, adding categories, running the test suite via MCP, auditing coverage, or diagnosing EditMode NRE/TMP issues. Covers the layered test layout (one assembly per root), TestLayoutConventionTests, TestCategories, TestReflection, the ratchets, TestRunRecorder, the MCP workflow and all known EditMode gotchas."
argument-hint: "Describe the test task: new tests, fix failures, reorganize, audit coverage, etc."
---

# Unity Testing — Valkur Project

The layout is **enforced by a test**, not by this page: `TestLayoutConventionTests`
(`Assets/Tests/EditMode/Project/Code/`) fails on every rule below with a message saying what to
do. Read its failure before editing anything else. Reorganised 2026-09-15 from a 109-folder tree
with a meaningless `Game/` level; the audit that motivated it is the artifact
"El árbol de tests de Valkur".

## Where a test goes

```text
Assets/Tests/<Mode>/<Root>/<Feature>/<Subject>Tests.cs      namespace = path
                                                            Valkur.Tests.<Mode>.<Root>.<Feature...>
```

### 1. Root = the highest production layer the test needs

Each EditMode root is its own assembly (`Valkur.Tests.EditMode.<Root>`) and references ONLY the
production layers listed, so a test in the wrong root does not compile. That is deliberate: the
compiler is what keeps the placement honest.

| Root | Sees | Put here |
|---|---|---|
| `Core` | Core | Input helpers, services, boot plumbing, coordinates, market cycle |
| `Data` | Core, Data | ScriptableObject definitions, catalogues, chunk/biome data, save DTOs |
| `Infrastructure` | Core, Data, Infrastructure | Repositories, profile DB, migrations |
| `UIKit` | Core, Data, UIKit | Reusable UI widgets (`Gameplay/UIKit`) |
| `Gameplay` | the four above + Gameplay | Runtime systems: combat, spells, player, world, save, chat… |
| `UI` | everything runtime | HUD, menus, loading screen (`Scripts/UI`) |
| `Editors` | everything | The in-game runtime editors (`Gameplay/Editors/<Name>`) |
| `EditorTools` | everything | `Valkur.Editor`: importers, bakers, menu tools |
| `Project` | everything | Rules about the whole project — `Code/` (source guards), `Assets/` (asset conventions), `Baselines/` |

A data test that has to spin up a `ProjectileExecutor` is a Gameplay test: put it in
`Gameplay/Spells/`, not `Data/Spells/`. PlayMode uses the same roots in one assembly
(`Valkur.Tests.PlayMode`); there are few PlayMode tests and each needs Play Mode anyway.

### 2. Feature = the production folder the test is about

Strip the assembly prefix from the production folder and mirror it:
`Scripts/Gameplay/Combat/Death/DeathSequenceController.cs` → `EditMode/Gameplay/Combat/Death/`.
`Scripts/Data/Spells/SpellDefinition.cs` + Gameplay needed → `EditMode/Gameplay/Spells/`.
Files at an assembly's root (`Core/ServiceLocator.cs`) → the test root itself (`EditMode/Core/`).

- **One aspect folder is allowed below a real feature folder** when a folder would get too big to
  scan: `Editors/TileEditor/{Brush,Catalog,History,Input,Overlay,Picker,Select,Session,ToolModes,UI}`,
  `Gameplay/Player/{Animations,Locomotion}`.
- **Editors are `Editors/<Name>Editor/`** for `Scripts/Gameplay/Editors/<Name>/` (`TileEditor`,
  `MapEditor`, `CameraEditor`…); shared editor infrastructure is `Editors/_Shared/`.

### 3. A folder may never share a name with a type

A folder is a namespace segment, and a namespace segment **hides every type of the same name**
for all the tests in the sibling namespaces: a folder `World/Camera` turns every `Camera` in
`World/*` tests into `CS0118 'Camera' is a namespace but is used like a type`. The reorganisation
hit this on its first compile. Use the alias:

| Production feature | Test folder | Hides |
|---|---|---|
| `Combat/Resources` | `Combat/Vitals` | `UnityEngine.Resources` |
| `Combat/Collision` | `Combat/Hitboxes` | `UnityEngine.Collision` |
| `Inventory` | `InventorySystem` | Valkur's `Inventory` |
| `World/Camera` | `World/CameraRig` | `UnityEngine.Camera` |
| `HUD/Debug` | `HUD/DebugHud` | `UnityEngine.Debug` |
| `Editors/DungeonNodeGraph` | `Editors/NodeGraphEditor` | class `DungeonNodeGraphEditor` |
| `Editors/TimeWeather` | `Editors/TimeAndWeatherEditor` | class `TimeWeatherEditor` |
| (tile editor aspects) | `Session`, `ToolModes`, `History` | `State`, `UnityEditor.Tools`, `UnityEditor.Undo` |

A new collision is added to the `Aliases` / `ReservedSegments` tables in
`TestLayoutConventionTests`, with the type it avoids.

### 4. Files and fixtures

- `<Subject>Tests.cs`, one top-level type per file named like the file. A long fixture is split
  into partials `<Subject>Tests.<Aspect>.cs` (SetUp/TearDown/helpers stay in the main file) —
  **no file over 1000 lines**. Non-test helpers get their own file (`WorldBarTestHelper.cs`).
- No two fixtures share a simple name anywhere in the suite.
- Test names: `Subject_Scenario_Outcome`, two or three PascalCase parts
  (`TakeDamage_WhenInvincible_LeavesHpUnchanged`). Existing names that break this are a ratchet
  (below): rename when you are changing the test anyway, never in bulk — a test's name is the key
  of every filter, history entry and note that mentions it.
- Shared cross-root helpers go in `Assets/Tests/Support/` (`Valkur.Tests.Support`, referenced by
  every test assembly). Keep it free of Gameplay types so every root can use it.

## Categories

Use `[Category(TestCategories.X)]` — string literals are refused by the convention test.

| Constant | Meaning | Applied |
|---|---|---|
| `Guard` | whole-project rule (source scan, asset convention) | every fixture under `Project/` |
| `ShippedData` | reads data the game ships | every `Shipped*Tests` + the old `DataIntegrity` tests |
| `Integration` | several systems composed | fixtures named `*Integration*` / `*EndToEnd*` |
| `Slow` | measured: test ≥ 0.5 s or fixture ≥ 2 s on the reference run | from `TestRunRecorder` output |

```text
mcp_unity_run_tests(mode="EditMode", category_names=["Guard"])     # only the guards
Unity -runTests -testPlatform EditMode -testCategory "!Slow"       # quick loop without the slow ones
```

## Reflection: `TestReflection`, never a private helper

`Valkur.Tests.Support.TestReflection` — `GetField/GetField<T>/SetField`, `GetStaticField`,
`GetProperty<T>/SetProperty`, `Invoke/Invoke<T>/InvokeStatic`, `InvokeUnwrapped`, `FindField`,
`FindMethod`. Lookups walk base types, instance + static, public + non-public; a missing member
throws `MissingMemberException` naming type and member (not an NRE later). Exceptions from
`Invoke` are NOT unwrapped (same as `MethodInfo.Invoke`); use `InvokeUnwrapped` to assert on the
method's own exception. Prefer `internal` + `InternalsVisibleTo` (granted to every test assembly
that can see the production assembly) over reflection when you own the production code.

## Ratchets and baselines (`EditMode/Project/Baselines/`)

A ratchet is a per-file count that may fall and may never rise. When one fails, the live counts
for every file are written to `Library/ValkurTestResults/<baseline>.proposed.txt`.

| Baseline | Counts | Owner |
|---|---|---|
| `test-method-names.txt` | test names that are not `Subject_Scenario_Outcome` | `TestLayoutConventionTests` |
| `reflection-helpers.txt` | private reflection helpers not yet moved to `TestReflection` | `TestLayoutConventionTests` |
| `editor-raw-colors.txt` | raw `new Color(` in the runtime editors | `EditorRawColorRatchetTests` |
| `unreset-statics.txt` | static mutable fields without a reset hook | `DomainReloadStaticResetTests` |

## Measuring: `TestRunRecorder`

`Valkur.Editor.Testing.TestRunRecorder` writes every run (Test Runner window, MCP or CLI) to
`unity/Valkur/Library/ValkurTestResults/last-<Mode>.tsv` plus a timestamped copy:
`result<TAB>seconds<TAB>fullName`, headed by the pass/fail/skip counts. It answers "which tests
are slow" and "was this already red before my change" without bisecting through the MCP bridge,
which cannot carry per-test detail for nine thousand tests. A filtered run writes a partial file.

## Namespace script

```powershell
# From the workspace root (d:\Python\RogueLike):
.\.github\skills\unity-testing\scripts\enforce-namespaces.ps1        # report
.\.github\skills\unity-testing\scripts\enforce-namespaces.ps1 -Fix   # rewrite mismatches
```

It fixes the namespace only; placement is the convention test's job.

---

## Running Tests via MCP

```text
# Full EditMode suite (~9 400 tests, ~3.5 min)
mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)

# One root, feature or fixture: group_names takes a regex over full names
mcp_unity_run_tests(mode="EditMode", group_names=["^Valkur\\.Tests\\.EditMode\\.Gameplay\\.Combat\\."])

# One assembly
mcp_unity_run_tests(mode="EditMode", assembly_names=["Valkur.Tests.EditMode.Editors"])

# Poll until done
mcp_unity_get_test_job(job_id=..., wait_timeout=60)
```

A bare namespace prefix in `test_names` does not start a run (the job times out initialising);
use `group_names` with an anchored regex. Always check `failures_so_far` on each poll and
`summary.failed == 0` before declaring success.

---

## Writing New Tests — Template

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Tests.Support;
// + the using for the system under test

namespace Valkur.Tests.EditMode.Gameplay.Combat.Death   // = the folder
{
    [TestFixture]
    public class DeathSequenceControllerTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
        }

        [Test]
        public void Revive_AtAnAltar_RestoresFullHealth()
        {
            // Arrange
            // Act
            // Assert
        }
    }
}
```

---

## EditMode Gotchas (Critical)

### 1. TextMeshProUGUI — NRE without Canvas
**Problem**: Adding `TextMeshProUGUI` to a bare `GameObject` in EditMode and immediately
reading/writing `color` or `fontStyle` throws `NullReferenceException` because
`CanvasUpdateRegistry` can't initialize without a Canvas ancestor.

**Wrong**:
```csharp
var go = new GameObject();
var tmp = go.AddComponent<TextMeshProUGUI>();
tmp.fontStyle = FontStyles.Bold;   // NRE!
```

**Right — use a fully initialized UI ref**:
```csharp
LogAssert.ignoreFailingMessages = true;
var ui = CreateInitializedUI();  // full MonoBehaviour setup
var refs = TestReflection.GetField<UIRefs>(ui, "_refs");
// refs.SomeTmp is already valid — Unity lifecycle ran on it
refs.SomeTmp.fontStyle = FontStyles.Bold;  // safe
```

**Or — add Canvas parent before adding TMP** (only works if TMP is not read/written inline):
```csharp
var canvas = new GameObject("Canvas");
canvas.AddComponent<Canvas>();
var child = new GameObject("TMP");
child.transform.SetParent(canvas.transform, false);
var tmp = child.AddComponent<TextMeshProUGUI>();
// Minimal Canvas still may not fully init TMP; prefer initialized UI refs
```

### 2. `[SerializeField]` on private fields in MonoBehaviour
**Problem**: Unity's serialization writes stale destroyed-object references from a prior
test instance into a freshly created component (via the serialization system).

**Solution**: Remove `[SerializeField]` from any field set only at runtime (not needed
in Inspector). Add a comment explaining it's runtime-only.

```csharp
// NOT:  [SerializeField] private Canvas _canvasRoot;
// YES:  private Canvas _canvasRoot;  // Runtime-only; set by BuildUI()
```

### 3. Unity null vs C# null
`Assert.IsNotNull(destroyedGO)` will **pass** even though the object is destroyed —
because `destroyedGO` is not C# null, it's a Unity fake-null.

Use instead:
```csharp
Assert.IsTrue(destroyedGO != null, "Object should not be destroyed");
// or
Assert.That((bool)destroyedGO, Is.True);
```

### 4. `LogAssert.ignoreFailingMessages = true`
Required for any test that creates UI (Image, TMP, Canvas) in EditMode.
Unity logs warnings/errors from renderer initialization that would fail the test
even when the assertion passes. Place at the top of the test method.

### 5. `renderer.material` in EditMode tests
Accessing `renderer.material` (not `sharedMaterial`) creates a new material instance
and logs a "leak" warning. Use `renderer.sharedMaterial` in EditMode tests or
suppress with `LogAssert.ignoreFailingMessages = true`.

### 6. Input System actions in EditMode
`InputAction` can't be enabled/disabled in EditMode tests. Test the action's
configuration (path, type) via property inspection rather than simulation.

---

## Audit Checklist — after moving or adding tests

1. `enforce-namespaces.ps1` reports 0 mismatches
2. `mcp_unity_refresh_unity(mode="force", scope="all", compile="request")` → 0 compile errors, and
   every `Library/ScriptAssemblies/Valkur.Tests.*.dll` newer than the files you touched (one red
   assembly freezes the domain and the console can look clean)
3. `mcp_unity_run_tests(mode="EditMode", category_names=["Guard"])` → `TestLayoutConventionTests` green
4. `mcp_unity_run_tests(mode="EditMode")` → `summary.failed == 0`; compare against
   `Library/ValkurTestResults/last-EditMode.tsv` from before the change
5. A fixture that goes red only in the full run and green alone is order-dependent: something that
   now runs BEFORE it leaks scene objects or statics. Find the leak; do not reorder the tests
