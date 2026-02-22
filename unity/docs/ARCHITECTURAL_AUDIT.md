# Valkur — Architectural Audit Report

**Date:** 2026-02-22  
**Branch:** `feature/unity-migration-fase6-continuation`  
**Scope:** Full codebase audit per `/unity-refactor-pro` workflow  

---

## 0. Baseline Metrics

| Metric | Value |
|--------|-------|
| Total `.cs` files | 90 |
| Assemblies | 5 (`Core`, `Data`, `Gameplay`, `Infrastructure`, `UI`) |
| Singletons | 9 (`SaveService`, `TileEditorManager`, `PerformanceMonitor`, `CombatRangeVisualizer`, `InventoryUI`, `VFXManager`, `AudioManager`, `DeathScreenUI`, `HUDManager`) |
| Files >400 lines | 4 |
| Files 300–400 lines | 5 |
| `GetComponent` calls | 177 across 45 files |
| `FindObjectOfType` calls | 21 across 13 files |

### Files Exceeding Size Limits

| File | Lines | Verdict |
|------|-------|---------|
| `Editor/PythonDataMigrator.cs` | 942 | Editor-only, lower priority |
| `Gameplay/TileEditor/TileEditorUI.cs` | 941 | **CRITICAL** — pure UI construction, should split |
| `Gameplay/TileEditor/TileEditorManager.cs` | 734 | **CRITICAL** — God Object (input + state + undo + UI wiring) |
| `Gameplay/SaveService.cs` | 582 | **HIGH** — mixed IO + serialization + game state collection |
| `UI/HUD/DebugHUD.cs` | 397 | Borderline — recently rewritten, acceptable |
| `UI/MainMenu/MainMenuUI.cs` | 367 | Borderline — UI construction heavy |
| `UI/DeathScreenUI.cs` | 353 | Borderline — UI construction heavy |
| `Gameplay/Inventory/InventoryUI.cs` | 351 | Borderline — UI construction heavy |
| `Gameplay/Spells/SpellCaster.cs` | 326 | **MEDIUM** — mixed spell execution strategies |
| `Gameplay/EntitySetup.cs` | 297 | **MEDIUM** — static God Factory |

---

## 1. Architectural Issues Detected

### 1.1 God Objects

| Class | Responsibility Violations |
|-------|--------------------------|
| **`TileEditorManager`** (734 lines) | Input handling, tool state, undo/redo stack, UI creation, brush operations, grid cursor, layer management — at least 5 distinct responsibilities |
| **`EntitySetup`** (297 lines) | Player config, monster config, fireball prefab creation, spell definition creation, placeholder sprite creation, material management, UI singleton creation — static utility doing too much |
| **`SaveService`** (582 lines) | File IO, checksum, backup rotation, schema migration, game state collection (`CollectSaveData`), game state restoration (`ApplySaveData`), autosave timer — at least 4 responsibilities |
| **`SpellCaster`** (326 lines) | Phase FSM, cooldown management, projectile spawning, slash execution, area execution, dash execution — spell execution should use Strategy pattern |
| **`PlayerController`** (257 lines) | Input creation, movement, facing, combat polling, save/load polling — borderline but save/load doesn't belong here |

### 1.2 Domain Logic Inside MonoBehaviours

| MonoBehaviour | Domain Logic That Should Be Pure C# |
|---------------|--------------------------------------|
| `SaveService` | `CollectSaveData()`, `ApplySaveData()`, `MigrateSchema()`, checksum computation |
| `SpellCaster` | Spell execution strategies (`PerformSlash`, `PerformArea`, `PerformDash`), phase FSM |
| `PlayerController` | Save/load polling (should be in a dedicated input handler) |
| `TileEditorManager` | Undo/redo stack management, brush stroke logic |

### 1.3 Tight Coupling & Dependency Issues

- **`EntitySetup`** directly references 8 namespaces and creates singletons (`InventoryUI`, `CombatRangeVisualizer`) — acts as an implicit DI container
- **`SaveService.CollectSaveData()`** uses `GetComponent<>` chains on `EntityRegistry.Player` — tightly coupled to component layout
- **`PlayerController`** directly references `SaveService.Instance` — save/load input should be separate
- **`SpellCaster`** calls `GetComponent<Mana>()` in `TryCast()` every frame a spell is cast — should cache
- **FSM States** (`PatrolState`, `ChaseState`, `AttackState`, etc.) each call `GetComponent<>` 3-6 times — should receive cached references via context

### 1.4 Update Abuse

| Class | Issue |
|-------|-------|
| `SaveService.Update()` | Ticks autosave timer every frame — could use `InvokeRepeating` or coroutine |
| `SpellCaster.Update()` | Ticks all cooldown timers every frame — acceptable but could be event-driven |
| `TileEditorManager.Update()` | Large Update with input polling + mouse handling + grid cursor — acceptable for editor tool |

### 1.5 Singleton Overuse

9 singletons is high for a project this size. Candidates for replacement:
- **`VFXManager`** → could be a service registered in `ServiceLocator`
- **`AudioManager`** → already uses `IAudioService` interface, good candidate for `ServiceLocator`
- **`CombatRangeVisualizer`** → debug tool, singleton acceptable
- **`InventoryUI`** → UI singleton, acceptable but should be created by a UI bootstrap, not `EntitySetup`

### 1.6 Missing Patterns

| Area | Current | Recommended |
|------|---------|-------------|
| Spell execution | Switch statement in `SpellCaster` | **Strategy Pattern** — `ISpellExecutor` per spell type |
| Monster AI states | FSM exists (good) | Already using State Pattern ✓ |
| Damage/death events | Direct method calls | **Observer/EventBus** for `OnHit`, `OnDeath`, `OnXpGain` |
| Entity creation | `EntitySetup` static methods | **Factory Pattern** — `IEntityFactory` |
| Input handling | Inline `InputAction` creation | **Command Pattern** — centralized input map |
| Projectiles | `Instantiate` every cast | **Object Pooling** — `ObjectPool` exists but unused for projectiles |

---

## 2. Current Layer Structure vs. Ideal

### Current Structure
```
Scripts/
├── Core/           ← Singletons, utilities, config (OK)
├── Data/           ← ScriptableObjects (OK)
├── Editor/         ← Editor tools (OK)
├── Gameplay/       ← EVERYTHING: combat, spells, FSM, inventory, save, rendering, tile editor
├── Infrastructure/ ← Only AudioManager
└── UI/             ← HUD, menus, death screen
```

### Problems
1. **`Gameplay/`** is a dumping ground — 60+ files, no feature separation
2. **No pure domain layer** — all logic lives in MonoBehaviours
3. **`Infrastructure/`** is nearly empty — services scattered across `Core/` and `Gameplay/`
4. **UI construction code** (700-900 line files) mixed with UI logic

---

## 3. Proposed Target Architecture

```
Scripts/
├── Core/                    ← Pure C# domain (no Unity deps where possible)
│   ├── Combat/              ← Damage calculation, hit resolution
│   ├── Spells/              ← Spell execution strategies, phase FSM
│   ├── Stats/               ← Health, Mana, Experience logic
│   └── Save/                ← Save data collection, schema migration
│
├── Data/                    ← ScriptableObjects, configs (unchanged)
│
├── Features/                ← Feature-based MonoBehaviour adapters
│   ├── Combat/              ← MeleeCombat, DashAbility, CombatFeedback
│   ├── Enemies/             ← FSM, MonsterSpawner, brain
│   ├── Inventory/           ← Inventory, PickupSystem, WorldPickup
│   ├── Player/              ← PlayerController, FacingIndicator
│   ├── Spells/              ← SpellCaster (adapter), Projectile, FireballVisual
│   ├── TileEditor/          ← TileEditorManager, UI, GridCursor
│   └── World/               ← WorldGridBuilder, TilemapLayerSetup, SpatialHash
│
├── Systems/                 ← Cross-cutting services
│   ├── Save/                ← SaveService (IO only), backup rotation
│   ├── Events/              ← EventBus (future)
│   └── Pooling/             ← ObjectPool (already exists)
│
├── Presentation/            ← All UI
│   ├── HUD/                 ← DebugHUD, HUDManager, TargetHUD
│   ├── Menus/               ← MainMenu, DeathScreen
│   └── Shared/              ← UI helpers, style constants
│
├── Infrastructure/          ← Wiring, factories, audio
│   ├── Audio/               ← AudioManager
│   ├── Bootstrap/           ← GameBootstrap, GameplaySceneSetup, EntitySetup
│   └── ServiceLocator.cs
│
└── Editor/                  ← Editor tools (unchanged)
```

---

## 4. Prioritized Refactor Plan

### Phase 1: Extract Domain Logic (HIGH IMPACT, LOW RISK)
1. **Split `SaveService`** → `SaveFileManager` (IO/backup/checksum) + `GameStateCollector` (pure C#) + `GameStateRestorer` (pure C#)
2. **Split `SpellCaster`** → Extract `ISpellExecutor` strategy per spell type (`ProjectileExecutor`, `SlashExecutor`, `AreaExecutor`, `DashExecutor`)
3. **Split `EntitySetup`** → `PlayerFactory`, `MonsterFactory`, `PrefabFactory` (fireball prefab)

### Phase 2: Split Large Files (HIGH IMPACT, MEDIUM RISK)
4. **Split `TileEditorManager`** → `TileEditorInputHandler`, `TileEditorUndoSystem`, `TileEditorBrushEngine` + slim `TileEditorManager` coordinator
5. **Split `TileEditorUI`** → `TileEditorLeftPanel`, `TileEditorRightPanel`, `TileEditorUIStyles` (shared design tokens)

### Phase 3: Decouple Systems (MEDIUM IMPACT, MEDIUM RISK)
6. **Move save/load input** out of `PlayerController` into dedicated `SaveLoadInputHandler`
7. **Cache GetComponent** in FSM states — pass context object with pre-resolved references
8. **Register services** in `ServiceLocator` instead of singleton access (`VFXManager`, `AudioManager`)

### Phase 4: Introduce Events (MEDIUM IMPACT, HIGH RISK)
9. **Create `GameEvents`** static event bus for `OnPlayerDamaged`, `OnMonsterDeath`, `OnXpGained`, `OnItemPickup`
10. **Wire CombatFeedback** to events instead of direct calls

### Phase 5: Folder Reorganization (LOW RISK)
11. Move files to `Features/`, `Systems/`, `Presentation/` structure
12. Update assembly definitions

---

## 5. Risk Assessment

| Refactor | Risk | Regression Area |
|----------|------|-----------------|
| Split SaveService | LOW | Save/load — test with F5/F9 |
| Split SpellCaster | MEDIUM | Combat — test all spell types |
| Split EntitySetup | MEDIUM | Entity spawning — test player + monsters |
| Split TileEditorManager | LOW | Tile editor — test F6 |
| Split TileEditorUI | LOW | Tile editor UI — visual only |
| Move save input | LOW | F5/F9 still work |
| Cache GetComponent in FSM | LOW | Monster AI behavior |
| Event bus | HIGH | All combat interactions |

---

## 6. Acceptance Criteria

- [ ] No file exceeds 400 lines (Editor files exempt)
- [ ] No method exceeds 30 lines
- [ ] MonoBehaviours are thin adapters — no domain logic
- [ ] Zero `FindObjectOfType` in runtime code (Editor exempt)
- [ ] `GetComponent` calls cached in `Awake`/`Initialize`, not in Update loops
- [ ] Each class has a single clear responsibility
- [ ] All existing functionality preserved: combat, spells, AI, save/load, tile editor, inventory

---

## 7. Recommended Execution Order

Start with **Phase 1, Item 1 (SaveService split)** — it's the safest, most impactful refactor:
- Clear separation of concerns
- Easy to test (F5/F9)
- No combat regression risk
- Establishes the pattern for subsequent splits

**Estimated effort per phase:**
- Phase 1: ~2 sessions
- Phase 2: ~2 sessions  
- Phase 3: ~1 session
- Phase 4: ~2 sessions
- Phase 5: ~1 session

**Current maturity level:** Mid → targeting Senior/Production-ready after Phase 3.
