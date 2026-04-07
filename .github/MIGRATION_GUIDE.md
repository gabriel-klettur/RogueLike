# Valkur Migration Guide: Python (Pygame-CE) → Unity 2022.3 LTS

**Version:** 2.0  
**Date:** 2026-04-07  
**Status:** 82% complete (41/50 steps)  
**Primary audience:** Developer, Copilot agents, future contributors

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Architecture Comparison](#2-architecture-comparison)
3. [Available Agents & Skills](#3-available-agents--skills)
4. [Migration Status](#4-migration-status)
5. [Remaining Work (9 steps)](#5-remaining-work)
6. [Critical Issues](#6-critical-issues)
7. [System-by-System Mapping](#7-system-by-system-mapping)
8. [Data Migration Reference](#8-data-migration-reference)
9. [Asset Pipeline Reference](#9-asset-pipeline-reference)
10. [Testing Strategy](#10-testing-strategy)
11. [Workflow Guide](#11-workflow-guide)
12. [Governance Rules](#12-governance-rules)

---

## 1. Project Overview

**Valkur** is a 2D roguelike action game featuring:
- Real-time combat (melee + spells + projectiles)
- FSM-driven monster AI (9 states: Idle, Patrol, Chase, AlertChase, Attack, Damage, Death, Flee, Unconscious)
- Tilemap-based world with Y-sort depth rendering
- Inventory system with pickups, drops, equipment
- Save/load with schema versioning and corruption recovery
- In-game tile and map editors
- Day/night lighting cycle
- Minigames (Pylos, Soluna)

### Source (Python)
| Component | Technology |
|-----------|-----------|
| Engine | Pygame Community Edition |
| ECS | Custom Python (dict-based, 45+ components) |
| Data | JSON configs + SQLAlchemy/SQLite |
| Validation | Pydantic + JSON Schema |
| Testing | pytest with headless fixtures |

### Target (Unity)
| Component | Technology |
|-----------|-----------|
| Engine | Unity 2022.3.62f1 LTS |
| Rendering | Universal Render Pipeline (URP) 2D |
| Input | New Input System 1.7.0 |
| Camera | Cinemachine 2.9.7 |
| Data | ScriptableObjects + Newtonsoft JSON |
| Testing | Unity Test Framework (NUnit) |

---

## 2. Architecture Comparison

### Game Loop

```
Python:                              Unity:
─────────                           ──────
main.py                              Bootstrap.unity scene
  └─ Game.run()                        └─ GameBootstrap.cs
       ├─ handle_events()                   ├─ Initializes ServiceLocator
       ├─ update()                          ├─ Loads MainMenu.unity
       ├─ render()                          └─ → MainGameplay.unity
       ├─ run_ecs_phase()                        └─ GameDirector.cs
       │    ├─ update_systems                         ├─ GameplaySceneSetup
       │    └─ render_systems                         ├─ MonoBehaviour.Update()
       └─ post_frame()                               ├─ MonoBehaviour.LateUpdate()
            ├─ flip display                           └─ FixedUpdate() (physics)
            ├─ cap FPS
            └─ autosave
```

### Module Mapping

| Python Module | Unity Assembly | Status |
|---------------|---------------|--------|
| `roguelike_game/managers/core/` | `Valkur.Core` | ✅ Done |
| `roguelike_game/ecs/core/` | Component pattern (MonoBehaviours) | ✅ Done |
| `roguelike_game/ecs/systems/combat/` | `Valkur.Gameplay/Combat/` | ✅ Done |
| `roguelike_game/ecs/systems/spells/` | `Valkur.Gameplay/Spells/` | ✅ Done |
| `roguelike_game/ecs/systems/inventory/` | `Valkur.Gameplay/Inventory/` | ✅ Done |
| `roguelike_game/ecs/systems/fsm/` | `Valkur.Gameplay/Enemies/FSM/` | ✅ Done |
| `roguelike_game/ecs/systems/rendering/` | Unity SpriteRenderers + URP | ✅ Done |
| `roguelike_engine/map/` | `Valkur.Gameplay/World/` | ✅ Done |
| `roguelike_engine/tile/` | Unity Tilemaps | ✅ Done |
| `roguelike_engine/rendering/` | URP 2D + sorting layers | ✅ Done |
| `roguelike_engine/input/` | New Input System | ✅ Done |
| `roguelike_engine/db/` | ScriptableObjects | ✅ Done |
| `roguelike_ui/` | `Valkur.UI` | ✅ Done |
| `roguelike_editors/` | `Valkur.Gameplay/TileEditor/` + `MapEditor/` | ✅ Done |
| `roguelike_engine/rendering/day_night/` | — | ❌ Pending |
| `roguelike_engine/audio/` | `Valkur.Infrastructure/AudioManager` | ❌ Empty |
| `minigames/` | — | ❌ Not started |

---

## 3. Available Agents & Skills

### Agents (`.github/agents/`)

Use these specialized agents for focused migration tasks:

| Agent | Purpose | When to Use |
|-------|---------|-------------|
| **@python-analyst** | Analyzes Python source code | Before porting any system — understand the Python implementation first |
| **@unity-architect** | Designs and writes Unity C# code | When implementing new features or porting systems |
| **@data-migrator** | Handles data conversion | When migrating JSON configs to ScriptableObjects |
| **@migration-qa** | Tests and validates parity | After porting any system — verify correctness |
| **@asset-pipeline** | Manages sprite/audio import | When importing or organizing game assets |

### Skills (`.github/skills/`)

Invoke these as slash commands for specific workflows:

| Skill | Purpose | When to Use |
|-------|---------|-------------|
| `/python-to-csharp` | Python→C# translation reference | When translating specific code patterns |
| `/combat-migration` | Combat system porting guide | When working on combat, spells, damage |
| `/asset-pipeline` | Asset import and atlas reference | When handling sprites, audio, import rules |
| `/migration-testing` | Testing and validation procedures | When writing tests or checking parity |

### Recommended Workflow

```
1. @python-analyst    → Analyze the Python system to port
2. /python-to-csharp  → Reference the translation patterns
3. @unity-architect   → Implement the C# version
4. @migration-qa      → Validate parity and correctness
```

---

## 4. Migration Status

### Phase Summary

| Phase | Description | Steps | Done | Status |
|-------|-------------|-------|------|--------|
| 0 | Preparation & baseline | 1-6 | 5/6 | 🟡 Step 4 pending |
| 1 | Unity bootstrap | 7-12 | 6/6 | ✅ Complete |
| 2 | Assets & import pipeline | 13-22 | 2/10 | 🔴 Deferred |
| 3 | Data contracts & migrators | 23-30 | 8/8 | ✅ Complete |
| 4 | Vertical slice | 31-36 | 6/6 | ✅ Complete |
| 5 | Full gameplay port | 37-44 | 8/8 | ✅ Complete |
| 6 | Tools & editors | 45-47 | 3/3 | ✅ Complete |
| 7 | Persistence & release | 48-50 | 3/3 | ✅ Complete |

### What's Working in Unity

- ✅ Player movement + 8-directional animation
- ✅ Melee combat (slash arc, cooldown, damage)
- ✅ Spell system (projectile, area, dash, slash) with mana
- ✅ Monster AI (9-state FSM: idle, patrol, chase, attack, etc.)
- ✅ Monster spawning from ScriptableObject definitions
- ✅ Inventory (grid UI, pickup, drop, stacking)
- ✅ Save/load (checksum, backup rotation, schema migration)
- ✅ Tilemap world building with Y-sort
- ✅ HUD (HP/MP bars, target info, floating damage)
- ✅ Experience/leveling system
- ✅ Tile editor + map editor
- ✅ Object pooling (projectiles, VFX)
- ✅ Entity culling (off-screen optimization)
- ✅ Performance monitoring (F3 overlay)
- ✅ Build validation pipeline

---

## 5. Remaining Work

### Step 4 — Record Python Baseline Evidence
**Priority:** Low (documentation)  
**Effort:** Manual  
**Action:** Run Python game, capture video + screenshots + performance logs  

### Steps 14-22 — Asset Pipeline Completion (Phase 2)
**Priority:** High  
**Effort:** Large  

| Step | Task | Agent/Skill |
|------|------|-------------|
| 14 | Create `asset_map.csv` master file | @asset-pipeline |
| 15 | Define asset naming convention | @asset-pipeline |
| 16 | Define pivot policy per category | /asset-pipeline |
| 17 | Define PPU policy per category | /asset-pipeline |
| 18 | Define SpriteAtlas groups | @asset-pipeline |
| 20 | Migrate 5-10% sample batch | @asset-pipeline |
| 21 | Validate and adjust import rules | @migration-qa |
| 22 | Execute full asset migration | @asset-pipeline |

### Additional Pending Items (not in roadmap)

| Item | Priority | Agent/Skill |
|------|----------|-------------|
| Fix Physics2D Collision Matrix (P2.1) | 🔴 CRITICAL | @unity-architect |
| Fix material leaks (P1.4) | ⚠️ Medium | @unity-architect |
| Audio pipeline integration | ⚠️ Medium | @unity-architect |
| Day/night cycle | Low | @python-analyst → @unity-architect |
| NPC spell casting | Low | /combat-migration |
| Patrol waypoints | Low | @unity-architect |
| Vendor buy/sell UI | Low | @unity-architect |
| Minigames (Pylos, Soluna) | Low | @python-analyst → @unity-architect |
| Clean up legacy InputManager axes | Low | @unity-architect |

---

## 6. Critical Issues

### P2.1 — Physics2D Layer Collision Matrix (🔴 ACTIVE)

**Problem:** All layers collide with all layers. Projectiles hit the player who cast them, NPCs collide with pickups, spawners interfere with movement.

**Required matrix:**

| | Player | NPC | Projectile | World | Pickup | UIBlocker | Building | Spawner |
|---|---|---|---|---|---|---|---|---|
| **Player** | — | ✅ | — | ✅ | ✅ | — | ✅ | — |
| **NPC** | ✅ | ✅ | ✅ | ✅ | — | — | ✅ | — |
| **Projectile** | — | ✅ | — | ✅ | — | — | ✅ | — |
| **World** | ✅ | ✅ | ✅ | — | — | — | — | — |
| **Pickup** | ✅ | — | — | — | — | — | — | — |
| **UIBlocker** | — | — | — | — | — | — | — | — |
| **Building** | ✅ | ✅ | ✅ | — | — | — | — | — |
| **Spawner** | — | — | — | — | — | — | — | — |

**Fix:** Edit via Unity menu `Edit > Project Settings > Physics 2D > Layer Collision Matrix`

### P1.4 — Material Leaks (⚠️ ACTIVE)

**Problem:** `WorldGridBuilder.cs` and `TileEditorGridCursor.cs` create `new Material()` at runtime without `Destroy()` on cleanup.

**Fix:** Cache materials and destroy in `OnDestroy()`.

---

## 7. System-by-System Mapping

### Combat

| Python System | Unity Script | Parity |
|---------------|-------------|--------|
| `combat_system.py` | `Health.cs` + `CombatFeedback.cs` | ✅ |
| `melee_system.py` + `slash_system.py` | `MeleeCombat.cs` | ✅ |
| `death_system.py` | `CombatFeedback.cs` (death fade) | ✅ |
| `hitbox_system.py` | `MeleeCombat.OverlapCircle` + `Projectile.OnTriggerEnter2D` | ✅ |
| `explosion_system.py` | `AreaExecutor.cs` | ✅ |
| `burn_system.py` | — | ❌ Not ported |
| `combat_sfx.py` | — | ❌ No audio |

### Spells

| Python System | Unity Script | Parity |
|---------------|-------------|--------|
| `spells_config.py` | `SpellDefinition.cs` (ScriptableObject) | ✅ |
| Fireball (projectile) | `ProjectileExecutor.cs` + `Projectile.cs` | ✅ |
| Laser beam | — | ❌ Not ported |
| Teleport | — | ❌ Not ported |
| Slash | `SlashExecutor.cs` | ✅ |
| Area | `AreaExecutor.cs` | ✅ |
| Dash | `DashExecutor.cs` + `DashAbility.cs` | ✅ |

### AI/FSM

| Python State | Unity State | Parity |
|-------------|------------|--------|
| `idle_state.py` | FSMMonsterBrain Idle | ✅ |
| `attack_state.py` | FSMMonsterBrain Attack | ✅ |
| `cast_state.py` | FSMMonsterBrain (partial) | ⚠️ No NPC spells |
| `damage_state.py` | FSMMonsterBrain Damage | ✅ |
| `death_state.py` | FSMMonsterBrain Death | ✅ |
| `unconscious_state.py` | FSMMonsterBrain Unconscious | ✅ |
| Patrol state | FSMMonsterBrain Patrol | ⚠️ No waypoints |
| Chase state | FSMMonsterBrain Chase | ✅ |
| Flee state | FSMMonsterBrain Flee | ✅ |

### World/Map

| Python System | Unity Script | Parity |
|---------------|-------------|--------|
| `map_model.py` | Tilemap system | ✅ |
| `chunked_map_view.py` | `WorldGridBuilder.cs` | ✅ |
| `tile_model.py` | Unity Tile assets | ✅ |
| `spatial_index.py` | `SpatialHash.cs` | ✅ |
| `pathfinding.py` | — | ❌ Not ported |
| `day_night.py` | — | ❌ Not ported |
| `zone/` | `ZoneManager.cs` | ✅ |

### Inventory

| Python System | Unity Script | Parity |
|---------------|-------------|--------|
| `inventory_ui_system.py` | `InventoryUI.cs` | ✅ |
| `inventory_transfer_system.py` | — | ⚠️ No vendor UI |
| `inventory_pickup_system.py` | `PickupSystem.cs` | ✅ |
| `inventory_drop_system.py` | `DropSystem.cs` | ✅ |
| `item_factory.py` | — | ❌ No procedural items |

### Persistence

| Python System | Unity Script | Parity |
|---------------|-------------|--------|
| `shutdown_manager.py` | `SaveService.cs` | ✅ |
| Save/load state | `GameStateCollector.cs` + `GameStateRestorer.cs` | ✅ |
| Schema migration | `SaveSchemaMigrator.cs` | ✅ |
| File I/O | `SaveFileManager.cs` | ✅ |

---

## 8. Data Migration Reference

### JSON Sources → ScriptableObjects

| Python Source | Unity ScriptableObject | Migration Tool |
|--------------|----------------------|----------------|
| `data/entities/new_hostiles.json` | `MonsterDefinition` (11 assets) | `PythonDataMigrator` |
| `data/entities/new_players.json` | `PlayerDefinition` | `PythonDataMigrator` |
| `data/spells/spells.json` | `SpellDefinition` | `PythonDataMigrator` |
| `data/items/` | `ItemDefinition` | `PythonDataMigrator` |
| `data/spawners/` | `SpawnerDefinition` | `PythonDataMigrator` |
| `data/config/input_bindings.json` | Input System `.inputactions` | Manual |
| `data/config/lighting.json` | — | Not migrated |
| `data/config/audio.json` | — | Not migrated |
| `data/config/combo_rules.json` | — | Not migrated |

### Migration Commands

- **Full migration:** Unity menu → `Valkur > Migration > Import All`
- **Dry-run (validate only):** Unity menu → `Valkur > Migration > Dry-Run All (Validate Only)`
- **Reports:** Check Unity Console for OK/Warning/Error counts per domain

---

## 9. Asset Pipeline Reference

### Current Status

| Category | Python Count | Unity Imported | Status |
|----------|-------------|---------------|--------|
| Tiles | ~hundreds | Partial | ⚠️ Phase 2 |
| Characters | ~dozens sheets | Partial | ⚠️ Phase 2 |
| NPC | ~dozens sheets | Partial | ⚠️ Phase 2 |
| Spells/VFX | ~dozens | Partial | ⚠️ Phase 2 |
| Items | ~dozens | Partial | ⚠️ Phase 2 |
| UI | ~dozens | Partial | ⚠️ Phase 2 |
| Audio | ~dozens WAV/OGG | 0 | ❌ Empty |

### Import Settings (ValkurAssetPostprocessor.cs)

| Path Contains | PPU | Filter | Compression |
|--------------|-----|--------|------------|
| `Tiles/` | 16 | Point | None |
| `Characters/` | 16 | Point | None |
| `NPC/` | 16 | Point | None |
| `Spells/` | 16 | Point | None |
| `VFX/` | 16 | Point | None |
| `Items/` | 16 | Point | None |
| `UI/` | 100 | Bilinear | None |

---

## 10. Testing Strategy

### Test Layers

| Layer | Tool | Location | Status |
|-------|------|----------|--------|
| Python unit tests | pytest | `python/tests/` | ✅ Active |
| Data migration validation | PythonDataMigrator dry-run | Unity Editor menu | ✅ Active |
| Content validation | ContentValidator | Unity Editor menu | ✅ Active |
| Build validation | BuildValidator | Pre-build hook | ✅ Active |
| Unity EditMode tests | NUnit | `Assets/Tests/EditMode/` | ⚠️ Minimal |
| Unity PlayMode tests | NUnit | `Assets/Tests/PlayMode/` | ⚠️ Minimal |
| Performance monitoring | PerformanceMonitor | F3 in-game overlay | ✅ Active |
| Parity testing | Manual comparison | — | ❌ Needed |

### Run Commands

```bash
# Python tests
cd python && python -m pytest tests/ -v

# Unity tests (CLI)
"C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" -runTests -testPlatform EditMode -projectPath "d:\Python\RogueLike\unity\Valkur"

# Data validation (in Unity Editor)
# Menu: Valkur > Migration > Dry-Run All (Validate Only)
# Menu: Valkur > Validation > Run All Validators
```

---

## 11. Workflow Guide

### Porting a New System

```
Step 1: ANALYZE
  └─ Use @python-analyst to understand the Python implementation
  └─ Read the Python source files thoroughly
  └─ Read any existing Python tests for that system
  └─ Document: algorithm, key values, dependencies

Step 2: CHECK EXISTING
  └─ Search unity/Valkur/Assets/_Project/Scripts/ for existing code
  └─ Check if system is partially ported already
  └─ Review migration roadmap for status

Step 3: DESIGN
  └─ Use /python-to-csharp for API translation patterns
  └─ Determine which assembly the code belongs to
  └─ Design C# class structure following project conventions

Step 4: IMPLEMENT
  └─ Use @unity-architect to write the C# code
  └─ Place in correct assembly folder
  └─ Use SerializeField, Tooltip, ServiceLocator
  └─ Preserve all numerical values exactly

Step 5: VALIDATE
  └─ Use @migration-qa to verify parity
  └─ Write Unity tests if applicable
  └─ Run content validators
  └─ Document any intentional differences
```

### Migrating Data

```
Step 1: Read Python JSON source
Step 2: Read Unity DTO/ScriptableObject class
Step 3: Use @data-migrator to map fields
Step 4: Run dry-run validation
Step 5: Execute migration
Step 6: Verify counts and spot-check values
```

### Migrating Assets

```
Step 1: Inventory source assets (count, dimensions)
Step 2: Use @asset-pipeline to plan import
Step 3: Copy to Unity Art/ folder with correct structure
Step 4: Verify ValkurAssetPostprocessor applied settings
Step 5: Build sprite atlases
Step 6: Update asset_map.csv
```

---

## 12. Governance Rules

1. **Never modify Python source** — it is the reference implementation
2. **Always check existing Unity scripts** before creating new ones
3. **Preserve numerical parity** — damage, speed, timing must match Python
4. **Data-driven design** — game tuning in ScriptableObjects/JSON, not hardcoded
5. **No UI in game logic** — keep presentation separate from domain
6. **No raw JSON in gameplay** — go through DTOs/ScriptableObjects
7. **No cross-layer imports** — Infrastructure cannot import Core
8. **Migrate by capability, not by file** — complete features, not partial ports
9. **Every closed step needs evidence** — tests, profiles, or documentation
10. **Document architectural decisions** in migration docs

---

## Quick Reference: File Locations

```
Repository Root: d:\Python\RogueLike\

Python Source:     python/src/roguelike_game/    (game logic)
                   python/src/roguelike_engine/  (engine systems)
                   python/data/                  (JSON configs)
                   python/assets/                (sprites, audio)
                   python/tests/                 (pytest suite)

Unity Project:     unity/Valkur/
Unity Scripts:     unity/Valkur/Assets/_Project/Scripts/
Unity Data:        unity/Valkur/Assets/_Project/Data/
Unity Art:         unity/Valkur/Assets/_Project/Art/
Unity Scenes:      unity/Valkur/Assets/_Project/Scenes/

Migration Docs:    unity/docs/Migration_python_to_unity/
Roadmap:           unity/docs/Migration_python_to_unity/01_execution/roadmap_50_steps.md
Agents:            .github/agents/
Skills:            .github/skills/
Instructions:      .github/copilot-instructions.md
```
