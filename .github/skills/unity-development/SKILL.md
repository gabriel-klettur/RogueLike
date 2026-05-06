---
name: unity-development
description: "General Unity development for the Valkur project. Use when writing or refactoring Unity C# scripts, designing systems, working with MCP for Unity tools, debugging runtime issues, configuring assemblies/layers/ScriptableObjects, or troubleshooting domain-reload, Cinemachine, URP 2D, Tilemap, UI Toolkit/uGUI, or input issues. Covers MCP workflow, console verification, performance patterns, hot-reload safety, and project conventions."
argument-hint: "Describe the Unity feature, bug, or system you are working on"
---

# Unity Development for Valkur

## When to Use
- Writing any new C# script under `unity/Valkur/Assets/_Project/Scripts/`
- Refactoring or fixing existing Unity systems
- Working with Unity MCP (`mcp_unity_*` tools / `mcpforunity://` resources)
- Designing a new ScriptableObject catalog, MonoBehaviour, or service
- Debugging Cinemachine, URP 2D rendering, Tilemap, Input System, or UI issues
- Configuring assemblies, physics layers, sorting layers, or tags
- Setting up editor tools, inspectors, gizmos, or property drawers
- Performance tuning (allocations, draw calls, physics queries)

> **NOT for:** asset import policies (see `asset-pipeline` skill) or test conventions (see `unity-testing` skill).

---

## 1. MCP for Unity Workflow

### Golden Rule: Resources for state, Tools for actions
| Need | Use |
|------|-----|
| Read editor state, project info, tags, layers, tests | `mcpforunity://...` resources |
| Read scene hierarchy, GameObject components | `mcpforunity://scene/...` resources |
| Mutate scene (create/modify/delete GameObjects) | `mcp_unity_manage_gameobject` |
| Mutate scene (load/save scenes) | `mcp_unity_manage_scene` |
| Compile + check console | `mcp_unity_refresh_unity` + `mcp_unity_read_console` |
| Find GameObjects | `mcp_unity_find_gameobjects` (returns IDs only — page) |
| Edit C# scripts | `apply_text_edits` (small) or `script_apply_edits` (structured) |

### Mandatory Post-Edit Verification
After **every** C# change (create/edit/refactor/delete):

1. `mcp_unity_refresh_unity` with `compile=request`, `mode=force`, `scope=scripts`, `wait_for_ready=true`
2. `mcp_unity_read_console` with `types=["error","warning"]`, `page_size=50`, `format=detailed`, `include_stacktrace=true`
3. Fix any error before continuing.
4. Acceptable warnings only:
   - MCP WebSocket reconnect (benign post domain reload)
   - `Default GameObject Tag: X already registered`
5. **Never** report "done" with errors in console.

### Payload Sizing
- `manage_scene(action="get_hierarchy")` → always paginate, `page_size=50`, follow `next_cursor`.
- `manage_gameobject(action="get_components")` → start `include_properties=false`, then `page_size=3-10` if you need values.
- `manage_asset(action="search")` → `page_size=25-50`, keep `generate_preview=false`.

### Multi-Instance Targeting
- List instances: `mcpforunity://instances` resource.
- Pin one for the whole session: call `set_active_instance` with `Name@hash`.
- One-shot routing: pass `unity_instance="ProjectName@abc123"` (or hash prefix, or stdio port).

### When NOT to use MCP
- Pure file edits (use `replace_string_in_file` / `multi_replace_string_in_file`)
- Reading source files (use `read_file`)
- Running CLI tests (use `run_in_terminal` with Unity's `-runTests`)

---

## 2. Project Conventions

### Assembly Placement
| Path | Assembly | Purpose | Can Reference |
|------|----------|---------|---------------|
| `Scripts/Core/` | `Valkur.Core` | Services, bootstrap, ServiceLocator, base classes | — |
| `Scripts/Data/` | `Valkur.Data` | ScriptableObjects, DTOs | Core |
| `Scripts/Infrastructure/` | `Valkur.Infrastructure` | Audio, persistence, save | Core, Data |
| `Scripts/Gameplay/` | `Valkur.Gameplay` | Combat, AI, world, entities | Core, Data, Infrastructure |
| `Scripts/UI/` | `Valkur.UI` | Menus, HUD | Core, Data, Infrastructure |
| `Scripts/Editor/` | `Valkur.Editor` | Editor tools (`#if UNITY_EDITOR`) | All above |

**Forbidden:** `Valkur.Gameplay → Valkur.UI` (would create circular ref).

### Code Style
```csharp
// ✅ CORRECT
public class EnemyController : MonoBehaviour
{
    [SerializeField, Tooltip("Movement speed in world units/sec.")]
    private float _moveSpeed = 3.75f;

    [SerializeField, Tooltip("Damage dealt on contact.")]
    private int _contactDamage = 10;

    private IAudioService _audio;

    private void Awake() => _audio = ServiceLocator.Get<IAudioService>();
}

// ❌ WRONG
public class EnemyController : MonoBehaviour
{
    public float moveSpeed = 3.75f;                // public field
    public static EnemyController Instance;        // raw singleton
    private AudioService _audio = new();           // hard dependency
}
```

Rules:
- **Never** `public` fields. Use `[SerializeField] private` + `[Tooltip]`.
- **Never** raw singletons in gameplay code. Use `ServiceLocator.Get<T>()` or `SingletonMonoBehaviour<T>` for genuine scene-wide systems only.
- **Never** hardcode tuning values that designers should change. Use `ScriptableObject` catalogs (see `Scripts/Data/`).
- Prefer `_camelCase` for private fields, `PascalCase` for properties/methods.
- One class per file; filename matches class.

---

## 3. Domain-Reload Safety (CRITICAL)

The project has **Enter Play Mode Options enabled** (`m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 3` = Disable Domain Reload + Disable Scene Reload). Play Mode entry is ~50 ms instead of ~10 s, but **static state persists between sessions**.

Every script with mutable static state **must** add a reset method:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStaticsOnPlayModeEnter()
{
    Instance = null;          // singleton instance
    _cachedSprite = null;     // cached UnityEngine.Object references
    _someList?.Clear();       // mutable collections
    _someEvent = null;        // static C# events / Action delegates
}
```

What needs reset:
- **Manually-coded singletons** (`public static MyClass Instance { get; private set; }`)
- **Cached `UnityEngine.Object`** (Sprite, Texture2D, TileBase, Material, Mesh…) — they point to assets destroyed in the prior session.
- **Static `event` and `Action`** fields — stale subscribers cause NullRef.
- **`static readonly` collections** — don't reassign, but `.Clear()` their contents.

What does NOT need reset:
- Pure value statics (`const`, immutable `static readonly int`)
- Classes inheriting from `SingletonMonoBehaviour<T>` (the base already resets `_instance`)

Symptoms of missing resets:
- "MissingReferenceException: The object of type 'X' has been destroyed but you are still trying to access it"
- NullRef on a previously-valid Instance
- Dead listeners on static events
- "Tag X already registered" loops

---

## 4. Common Patterns

### ServiceLocator
```csharp
// Register (in bootstrap):
ServiceLocator.Register<IAudioService>(audioService);

// Consume (in any MonoBehaviour):
private IAudioService _audio;
private void Awake() => _audio = ServiceLocator.Get<IAudioService>();
```

### SingletonMonoBehaviour<T>
Only for **true** scene-wide managers (TileEditor, GameEditor, etc.). Inherit and you get safe `Instance`, `HasInstance`, plus automatic domain-reload reset:
```csharp
public class MyManager : SingletonMonoBehaviour<MyManager> { }
```

### ScriptableObject Catalogs
For all designer-tunable data:
```csharp
[CreateAssetMenu(fileName = "NewSpell", menuName = "Valkur/Spells/Spell")]
public class SpellDefinition : ScriptableObject
{
    [SerializeField] private string _id;
    [SerializeField] private float _cooldownDuration;
    [SerializeField] private int _damage;
    public string Id => _id;
    public float CooldownDuration => _cooldownDuration;
    public int Damage => _damage;
}
```
- `_id` is a stable string identifier used for serialization and lookups.
- All fields exposed via read-only properties.
- Catalog `ScriptableObject` holds a `List<T>` of definitions.

### Object Pooling
For anything spawned frequently (projectiles, VFX, hit numbers): use `Scripts/Core/ObjectPool.cs`. Never `Instantiate` in hot loops.

```csharp
private ObjectPool<Projectile> _pool;
void Start() => _pool = new ObjectPool<Projectile>(_prefab, 32);
void Fire() => _pool.Get().Launch(direction);  // returns to pool on disable
```

### Coroutines vs Async
- **Coroutines** for time-based gameplay (`yield return new WaitForSeconds(0.5f)`).
- **`async UniTask`** if available, else `Task` only for non-Unity APIs (file I/O, networking).
- **Never** `async void` except for event handlers.

---

## 5. Physics & Layers

| Layer | Index | Purpose |
|-------|-------|---------|
| Player | 8 | The player |
| NPC | 9 | Enemies & friendly NPCs |
| Projectile | 10 | Bullets, spells, thrown items |
| World | 11 | Walls, terrain colliders |
| Pickup | 12 | Loot, items on ground |
| UIBlocker | 13 | World-space UI that blocks raycasts |
| Building | 14 | Doors, destructibles |
| Spawner | 15 | Spawn point triggers |

Use the `LayerMask` field in inspector (`[SerializeField] private LayerMask _hittable;`). Never hard-code `LayerMask.GetMask("Player")` in `Update()` — cache once.

### 2D Physics
- `Rigidbody2D` for moving objects (set `bodyType = Kinematic` for code-driven movement).
- `Collider2D.IsTouching` / `OverlapBox` over `OnCollisionEnter2D` for query-style detection.
- Always use `FixedUpdate` for physics writes (`rb.MovePosition`, `rb.velocity =`).

---

## 6. Sorting Layers (depth order)

```
Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities
  → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead
  → UI_World → Overlay
```

In code:
```csharp
spriteRenderer.sortingLayerName = "Entities";
spriteRenderer.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);
```

---

## 7. Cinemachine & Camera

**Critical gotcha:** `Camera.main.transform.position = ...` is overwritten every `LateUpdate` by the active CinemachineVirtualCamera's `Follow` target.

To pan freely (e.g., Tile Editor):
```csharp
// Detach
CameraSetup.Instance.DetachFollow();
var t = CameraSetup.Instance.GetDetachedTransform();  // vcam transform
t.position += panDelta;

// Reattach
CameraSetup.Instance.ReattachFollow();
```

For zoom: animate `vcam.m_Lens.OrthographicSize`, never `Camera.main.orthographicSize`.

---

## 8. Input

Project uses **both** legacy `InputManager` axes AND New Input System (transition state). When adding new input:
- Prefer **New Input System** `InputAction` assets.
- Bind in inspector via `PlayerInput` component or hard-coded `InputActionAsset`.
- Always guard player-action input with editor-mode checks:

```csharp
private void Update()
{
    if (GameEditorManager.AnyEditorActive) return;   // suppress in editors
    // ... player input
}
```

`GameEditorManager.AnyEditorActive` returns `true` when any modal editor (Tile, Map, Spells, Particles, Lighting) is open.

---

## 9. URP 2D Specifics

- The pipeline is `2D Renderer` (`Assets/_Project/Settings/Renderer2D.asset`).
- For custom drawing: use `RenderPipelineManager.endCameraRendering` event, **NOT** `OnRenderObject`. `Camera.current` is null during URP's SRP loop.
  ```csharp
  void OnEnable()  => RenderPipelineManager.endCameraRendering += Draw;
  void OnDisable() => RenderPipelineManager.endCameraRendering -= Draw;
  void Draw(ScriptableRenderContext ctx, Camera cam) { /* GL calls here */ }
  ```
- Use `Light2D` (Point/Freeform/Sprite/Global), not legacy 3D lights.
- Sprites must use a Sprite material with `Lit` shader for `Light2D` to affect them.

---

## 10. UI Conventions

### uGUI (Canvas)
- **Never** put `Image` and `TextMeshProUGUI` on the **same** GameObject. Causes `NullReferenceException` in TMP. Pattern:
  - Parent: `Image` + `Button` + layout
  - Child: `TextMeshProUGUI`
- Always use **TextMeshPro**, never legacy `Text`.
- Pivot at `(0.5, 0.5)` unless designing for stretch.
- For dynamic UI (dropdowns, lists): use object pooling via `Scripts/UI/Common/UIPool.cs`.

### Mouse + Keyboard Parity (MANDATORY for every menu)
**Every interactive UI control must be reachable with both mouse AND keyboard.** No exceptions for main menu, pause menu, options, load/save panels, modal overlays, in-game editors, dialogs, character selector, etc.

Required pattern for every list row, button, tab, slider, or modal action:

```csharp
// 1. Click → executes the action
var btn = hitGo.AddComponent<Button>();
btn.targetGraphic = hitImg;
int cap = i; // capture loop index
btn.onClick.AddListener(() =>
{
    int idx = scrollOffset + cap;
    if (idx < 0 || idx >= items.Count) return; // GUARD: empty hit zones
    selectedIndex = idx;
    UpdateVisuals();
});

// 2. Hover → moves the keyboard cursor (so Enter then operates on hovered row)
var trig  = hitGo.AddComponent<EventTrigger>();
var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
enter.callback.AddListener(_ =>
{
    int idx = scrollOffset + cap;
    if (idx < 0 || idx >= items.Count) return; // SAME GUARD
    selectedIndex = idx;
    UpdateVisuals();
});
trig.triggers.Add(enter);
```

**Modal overlays (rename, confirm-delete, prompt, etc.):** never rely on Enter/Esc only. Always include visible mouse-clickable buttons (typical pair: `Cancelar` left + `Aceptar`/`Borrar` right). Use `BuildOverlayButton` or equivalent helper.

**Common bugs to avoid:**
- Hit-areas covering empty rows (e.g. 8 fixed slots, only 3 saves) reassign selection to out-of-range indices on hover. Always guard `idx < items.Count`.
- Modal overlays without visible action buttons — keyboard-only flows are *never* acceptable.
- Forgetting `EventSystem` + `GraphicRaycaster` when bootstrapping a canvas at runtime — clicks silently die.
- Buttons on `Image+TMP` same GameObject — NRE (see rule above).
- Missing `targetGraphic` on `Button` — no click hit response.

### Editor UIs (Tile/Map/Spells)
Pattern: `XxxManager.cs` (logic) + `XxxUI.cs` (root canvas) + `XxxUIBuilder.cs` (programmatic construction).
- All pixel sizes in `XxxUIHelpers.cs` constants (`TOOLS_DROP_H = 150` etc.)
- Wire callbacks via `Action` delegates passed top-down.

---

## 11. Tilemap

- One `Grid` per scene. `cellSize = (1, 1, 1)`.
- One `Tilemap` per logical layer; `TilemapRenderer.sortingLayerName` = matching sort layer above.
- For wall collisions: `TilemapCollider2D` + `CompositeCollider2D` (set `usedByComposite=true` on the TilemapCollider2D).
- Tile assets cached statically must be reset on play mode entry (see Section 3).
- Use `tilemap.SetTile(cell, tileBase)` in batches; for huge updates use `SetTilesBlock`.

---

## 12. Performance Cheatsheet

| Anti-pattern | Fix |
|--------------|-----|
| `GameObject.Find` in `Update()` | Cache reference in `Awake` |
| `GetComponent<T>()` in `Update()` | Cache in `Awake`/`Start` |
| `Camera.main` in `Update()` | Cache once (it does a `FindWithTag`) |
| `string` concatenation per frame | `StringBuilder` or pre-built strings |
| `new List<T>()` in hot path | Reuse a field-level list, `Clear()` it |
| Allocating closures in `Update` | Hoist to fields |
| `Instantiate`/`Destroy` per frame | `ObjectPool<T>` |
| LINQ in hot loops | `for` loop over `List<T>` |
| `OnGUI` for HUD | uGUI Canvas |
| `Resources.Load` per call | Cache result, or move to Addressables |

Profile with:
- **Unity Profiler** (`Window → Analysis → Profiler`) — Deep Profile when in doubt.
- **Frame Debugger** for draw call investigation.
- **Memory Profiler** package for leak hunting.

---

## 13. Testing

### Edit Mode Tests
- Live in `Assets/_Project/Tests/EditMode/`.
- Reference `Valkur.Tests.EditMode.asmdef`.
- For tests that touch `renderer.material`: leaks warnings — wrap with `LogAssert.ignoreFailingMessages = true;`.

### Play Mode Tests
- `Assets/_Project/Tests/PlayMode/` — full scene loads.
- Use `[UnityTest]` + `yield return null` for frame-stepping.

### CLI Run
```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" `
  -batchmode -nographics -silent-crashes `
  -projectPath unity/Valkur `
  -runTests -testPlatform EditMode `
  -testResults TestResults.xml -logFile -
```

---

## 14. Editor Extensions

### Custom Inspector
```csharp
#if UNITY_EDITOR
using UnityEditor;
[CustomEditor(typeof(MyComponent))]
public class MyComponentEditor : Editor { ... }
#endif
```
File location: **must** be in `Scripts/Editor/` (auto-detected by `Valkur.Editor` asmdef).

### Menu Items
```csharp
[MenuItem("Valkur/Migration/Dry-Run All")]
public static void DryRunAll() { ... }
```
Convention: top-level menu = `Valkur`, then category, then action.

### Property Drawers
For custom serializable types, use `[CustomPropertyDrawer(typeof(MyType))]`. Always check `EditorGUI.BeginProperty`/`EndProperty`.

### Gizmos
```csharp
private void OnDrawGizmosSelected()
{
    Gizmos.color = Color.yellow;
    Gizmos.DrawWireSphere(transform.position, _detectionRadius);
}
```

---

## 15. Common Gotchas

| Bug | Cause | Fix |
|-----|-------|-----|
| `MissingReferenceException` after entering Play | Static field holds destroyed Object | Add `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset |
| Camera doesn't pan despite `transform.position +=` | Cinemachine `Follow` overrides each LateUpdate | Use `CameraSetup.DetachFollow()` |
| Player attacks while editor open | Missing input guard | Check `GameEditorManager.AnyEditorActive` |
| `InventorySlot == null` doesn't compile | `InventorySlot` is a `struct` | Use `.IsEmpty` |
| `SpellDefinition.cooldown` doesn't exist | Renamed | Use `cooldownDuration` |
| `Health.Current` doesn't exist | Renamed | Use `CurrentHp` |
| `DashAbility` not found in `Player` namespace | Lives in `Valkur.Gameplay.Combat` | Update `using` |
| Zone lookup fails on case mismatch | `ZoneManager` uses `OrdinalIgnoreCase` | Pass consistent casing anyway |
| Custom GL drawing invisible in URP | `OnRenderObject` + `Camera.current` is null | Use `RenderPipelineManager.endCameraRendering` |
| TMP NullRef on Buttons | `Image` + `TMP_Text` on same GO | Split into parent + child |
| EditMode test "leaked materials" | `.material` clones on use | `LogAssert.ignoreFailingMessages = true` in `[SetUp]` |

---

## 16. Hot Reload Workflow

The project is configured for fast iteration:
1. Edit C# in VS Code → save.
2. Tab to Unity → background recompile (~1-3 s).
3. Press Play → ~50 ms entry (no domain reload).

**Iteration loop for tuning:** edit ScriptableObject in Inspector → values apply live (no recompile). Use this for damage, speed, cooldowns.

**True in-Play C# hot patching:** not built-in. Paid options exist (Hot Reload by The Naughty Cult). Not recommended unless iteration cost justifies the license.

---

## 17. Saving & Persistence

- All saves go through `ISaveService` (see `Scripts/Infrastructure/Save/`).
- Schema versioned: each save has `schemaVersion`. On load, `SaveSchemaMigrator` walks `SaveMigrationChain` to upgrade.
- Use **JSON** via `JsonUtility` for simple cases, `Newtonsoft.Json` for polymorphic / dictionary keys.
- Never serialize `MonoBehaviour` directly — make a `[Serializable] class XxxState` DTO.
- Settings (key bindings, audio levels) → `PlayerPrefs` via `GameSettingsBindings` / `GameSettings`.

---

## 18. Audio

- All audio routed through `IAudioService` → `AudioManager` → `AudioMixer`.
- Three groups: `Master`, `Music`, `SFX`.
- SFX: `audioService.PlaySfx(SfxId.PlayerHurt)` with enum keys mapped via `AudioCatalog` ScriptableObject.
- Music: `audioService.PlayMusic(MusicId.Town, fadeSeconds: 1.5f)`.
- Never `AudioSource.PlayOneShot` directly in gameplay — go through service for volume/mixer routing.

---

## 19. Pixel-art conventions

| Concept | Value | Notes |
|---|---|---|
| World PPU (most assets) | 16 | 1 world unit = 16 px |
| Buildings PPU | 32 | Higher PPU for finer detail |
| Tiles PPU | 32 | Audited via `tools/atlas/audit_tile_sizes.py` |
| Tick rate | 60 Hz | Used by `MusicBeatClock` and status-effect ticks |

**Check existing scripts first** before creating new ones — duplication is the #1 source of regression in this codebase.

---

## 20. Quick MCP Recipes

### Add a component to a GameObject
```
mcp_unity_manage_components(
  action="add",
  target="Player",
  search_method="by_name",
  component_name="Valkur.Gameplay.Combat.DashAbility"
)
```

### Read what's on a GameObject
```
GET mcpforunity://scene/gameobject/{instanceId}/components
```

### Compile + check
```
mcp_unity_refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)
mcp_unity_read_console(types=["error","warning"], page_size=50, format="detailed", include_stacktrace=true)
```

### Run a menu item
```
mcp_unity_execute_menu_item(menu_path="Valkur/Audio/Validate Catalog")
```

### Search assets
```
mcp_unity_manage_asset(action="search", filter_type="ScriptableObject", search_pattern="*Spell*", page_size=25, generate_preview=false)
```

---

## Closing Reminders

- **Verify the console after every change.** No exceptions.
- **Check existing code before creating new code.** Duplication is the #1 source of regression here.
- **Game tuning lives in data, not code.** ScriptableObjects > hardcoded constants.
- **Static state needs a reset method.** Domain reload is OFF.
- **Game tuning belongs in data**, never hardcoded — touch ScriptableObjects, not numbers in `if` statements.
