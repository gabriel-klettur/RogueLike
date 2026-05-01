---
name: unity-testing
description: "Manage Unity tests for the Valkur project. Use when: creating new test files, fixing failing tests, reorganizing test folders, enforcing namespace conventions, diagnosing EditMode NRE/TMP issues, running the test suite via MCP, auditing test coverage, or deciding which folder/namespace a new test belongs to. Covers EditMode and PlayMode, NUnit patterns, MCP test workflow, folder→namespace mapping, and all known EditMode gotchas."
argument-hint: "Describe the test task: new tests, fix failures, reorganize, audit coverage, etc."
---

# Unity Testing — Valkur Project

## Folder → Namespace Map (canonical)

The Test Runner hierarchy is driven **entirely by C# namespace**, not by folder.
**Rule**: namespace = `Valkur.Tests.` + path segments below `Tests/` joined with `.`

| Folder (relative to `Assets/Tests/`) | Namespace |
|---------------------------------------|-----------|
| `EditMode/Editors/Buildings/` | `Valkur.Tests.EditMode.Editors.Buildings` |
| `EditMode/Editors/Entities/` | `Valkur.Tests.EditMode.Editors.Entities` |
| `EditMode/Editors/Inventory/` | `Valkur.Tests.EditMode.Editors.Inventory` |
| `EditMode/Editors/Items/` | `Valkur.Tests.EditMode.Editors.Items` |
| `EditMode/Editors/MapEditor/` | `Valkur.Tests.EditMode.Editors.MapEditor` |
| `EditMode/Editors/Modal/` | `Valkur.Tests.EditMode.Editors.Modal` |
| `EditMode/Editors/TileEditor/Brush/` | `Valkur.Tests.EditMode.Editors.TileEditor.Brush` |
| `EditMode/Editors/TileEditor/Catalog/` | `Valkur.Tests.EditMode.Editors.TileEditor.Catalog` |
| `EditMode/Editors/TileEditor/Diagnostics/` | `Valkur.Tests.EditMode.Editors.TileEditor.Diagnostics` |
| `EditMode/Editors/TileEditor/Input/` | `Valkur.Tests.EditMode.Editors.TileEditor.Input` |
| `EditMode/Editors/TileEditor/Overlay/` | `Valkur.Tests.EditMode.Editors.TileEditor.Overlay` |
| `EditMode/Editors/TileEditor/State/` | `Valkur.Tests.EditMode.Editors.TileEditor.State` |
| `EditMode/Editors/TileEditor/UI/` | `Valkur.Tests.EditMode.Editors.TileEditor.UI` |
| `EditMode/Editors/TileEditor/Undo/` | `Valkur.Tests.EditMode.Editors.TileEditor.Undo` |
| `EditMode/Game/AI/` | `Valkur.Tests.EditMode.Game.AI` |
| `EditMode/Game/Audio/` | `Valkur.Tests.EditMode.Game.Audio` |
| `EditMode/Game/Bootstrap/` | `Valkur.Tests.EditMode.Game.Bootstrap` |
| `EditMode/Game/Combat/` | `Valkur.Tests.EditMode.Game.Combat` |
| `EditMode/Game/Data/` | `Valkur.Tests.EditMode.Game.Data` |
| `EditMode/Game/Input/` | `Valkur.Tests.EditMode.Game.Input` |
| `EditMode/Game/Meta/` | `Valkur.Tests.EditMode.Game.Meta` |
| `EditMode/Game/Player/` | `Valkur.Tests.EditMode.Game.Player` |
| `EditMode/Game/UI/` | `Valkur.Tests.EditMode.Game.UI` |
| `EditMode/Game/VFX/` | `Valkur.Tests.EditMode.Game.VFX` |
| `EditMode/Game/World/` | `Valkur.Tests.EditMode.Game.World` |
| `PlayMode/Core/` | `Valkur.Tests.PlayMode.Core` |
| `PlayMode/Data/` | `Valkur.Tests.PlayMode.Data` |
| `PlayMode/Gameplay/` | `Valkur.Tests.PlayMode.Gameplay` |
| `PlayMode/Input/` | `Valkur.Tests.PlayMode.Input` |
| `PlayMode/UI/` | `Valkur.Tests.PlayMode.UI` |
| `PlayMode/World/` | `Valkur.Tests.PlayMode.World` |

> **No-go folders.** Do **not** create `EditMode/Gameplay/` (only `PlayMode/Gameplay/` exists) or
> `EditMode/Game/Core/` — TileEditor tests live under `Editors/TileEditor/<sub>/`, and
> Input / EventSystem / hotkey tests under `Game/Input/`.

### Where to place a new test

| System | Folder | Namespace |
|--------|--------|-----------|
| BuildingsEditor, ColliderBrush | `Editors/Buildings/` | `…Editors.Buildings` |
| EntitiesRuntimeEditor | `Editors/Entities/` | `…Editors.Entities` |
| InventoryRuntimeEditor | `Editors/Inventory/` | `…Editors.Inventory` |
| ItemsRuntimeEditor | `Editors/Items/` | `…Editors.Items` |
| MapEditor, EditorUIHelpers | `Editors/MapEditor/` | `…Editors.MapEditor` |
| EditorModal | `Editors/Modal/` | `…Editors.Modal` |
| TileEditor brush/paint | `Editors/TileEditor/Brush/` | `…TileEditor.Brush` |
| TileEditor catalog/registry | `Editors/TileEditor/Catalog/` | `…TileEditor.Catalog` |
| TileEditor diagnostics (TileEditorDiagnostics logger) | `Editors/TileEditor/Diagnostics/` | `…TileEditor.Diagnostics` |
| TileEditor input handler (mouse, zoom, hotkeys, input diagnose) | `Editors/TileEditor/Input/` | `…TileEditor.Input` |
| TileEditor overlay/persistence | `Editors/TileEditor/Overlay/` | `…TileEditor.Overlay` |
| TileEditor state, init, camera, integration | `Editors/TileEditor/State/` | `…TileEditor.State` |
| TileEditor UI (PanelChrome, Theme…) | `Editors/TileEditor/UI/` | `…TileEditor.UI` |
| TileEditor undo/redo | `Editors/TileEditor/Undo/` | `…TileEditor.Undo` |
| Monster/NPC AI, FSM | `Game/AI/` | `…Game.AI` |
| MusicBeat, BossBeat, audio clocks | `Game/Audio/` | `…Game.Audio` |
| Bootstrap, ServiceLocator, FKeys | `Game/Bootstrap/` | `…Game.Bootstrap` |
| Combat, spells, hitboxes, VFX rings | `Game/Combat/` | `…Game.Combat` |
| Save/load, settings, data migration | `Game/Data/` | `…Game.Data` |
| InputService, MouseInputManager, EventSystem, hotkey bindings | `Game/Input/` | `…Game.Input` |
| Source-tree regression / brace-balance / meta tests | `Game/Meta/` | `…Game.Meta` |
| Player (health, energy, inventory) | `Game/Player/` | `…Game.Player` |
| Menus, HUD, TabStrip | `Game/UI/` | `…Game.UI` |
| VFX, timed despawn | `Game/VFX/` | `…Game.VFX` |
| World, buildings, dungeon, zones | `Game/World/` | `…Game.World` |
| Core systems (ServiceLocator, Pool) | `PlayMode/Core/` | `…PlayMode.Core` |
| Data/catalog runtime | `PlayMode/Data/` | `…PlayMode.Data` |
| Combat/minimap runtime | `PlayMode/Gameplay/` | `…PlayMode.Gameplay` |
| Editor-hotkey dispatch (PlayMode) | `PlayMode/Input/` | `…PlayMode.Input` |
| Runtime mouse / menu input UI | `PlayMode/UI/` | `…PlayMode.UI` |
| Collision/physics/tiles runtime | `PlayMode/World/` | `…PlayMode.World` |

## Enforce Namespaces Script

Run [`scripts/enforce-namespaces.ps1`](./scripts/enforce-namespaces.ps1) from the workspace root
to scan all test `.cs` files and auto-correct any mismatched namespace declarations.

```powershell
# From workspace root (d:\Python\RogueLike):
.\\.github\\skills\\unity-testing\\scripts\\enforce-namespaces.ps1
```

---

## Running Tests via MCP

```
# Full EditMode suite
mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)

# Specific tests by name
mcp_unity_run_tests(mode="EditMode", test_names=["Valkur.Tests.EditMode.Editors.MapEditor.MapEditorTests.ApplyMenuBtnStyle_OpenState_HighlightsButton"])

# Poll until done
job = mcp_unity_get_test_job(job_id=...)
# Loop until job.status == "succeeded" or "failed"
```

Always check `failures_so_far` on each poll to detect failures early.
After running, verify `summary.failed == 0` before declaring success.

---

## Writing New Tests — Template

```csharp
using NUnit.Framework;
using UnityEngine;
// Add using for the system under test

namespace Valkur.Tests.EditMode.Game.Combat   // ← match folder per map above
{
    public class MySystemTests
    {
        // For tests that create GameObjects, track them for cleanup
        private readonly List<GameObject> _sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
        }

        [Test]
        public void FeatureName_Condition_ExpectedResult()
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
var refs = (UIRefs)GetFieldValue(ui, "_refs");
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

## Class Name Rules

- One `public class` per file, name matches filename exactly.
- If two test files in the same folder would produce the same class name, rename the
  less-specific one (e.g. `TileBrushTests` → `TileBrushExhaustiveTests` if an
  exhaustive variant exists in the same namespace).

---

## Audit Checklist — Running After Reorganization

1. `enforce-namespaces.ps1` reports 0 mismatches
2. `mcp_unity_refresh_unity(mode="force", scope="all")` → 0 compile errors
3. `mcp_unity_run_tests(mode="EditMode")` → `summary.failed == 0`
4. Test Runner tree shows expected hierarchy (namespace depth matches folder depth)
