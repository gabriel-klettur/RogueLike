# Valkur Migration Guide: Python (Pygame-CE) → Unity 2022.3 LTS

**Version:** 4.0
**Date:** 2026-05-03
**Status:** Core gameplay 100% migrated · Asset Pipeline Phase 2 reimport complete · Pylos/Soluna permanently deprecated · Meta-progression layer (IProfileDb) shipped on top of original 50-step plan
**Primary audience:** Developer, Copilot agents, future contributors

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Architecture Comparison](#2-architecture-comparison)
3. [Available Agents & Skills](#3-available-agents--skills)
4. [Migration Status](#4-migration-status)
5. [Remaining Work (5 steps)](#5-remaining-work)
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

- Real-time combat (melee + spells + projectiles, NPC casting via NPCAutoCast + NPCCastState)
- FSM-driven monster AI (9 states + NPCCastState for boss phases)
- Tilemap-based world with Y-sort depth rendering
- Inventory system with pickups, drops, equipment, weighted loot tables
- Save/load with schema versioning and corruption recovery
- In-game tile, map, buildings, and entities editors (F6 / F11 / F10 / runtime)
- Day/night lighting cycle (procedural Light2D ramp)
- Quest system (IObjective + KillCountObjective + Quest aggregator + QuestManager + QuestLogHUD)
- Skill tree progression (SkillNode + SkillTree + LearnedSkills + SkillEffectApplicator + AuraRegistry + SkillTreeHUD)
- Boss encounters (BossPhaseController + BossDefinition + BossConfigurator + BossHealthBarHUD)
- Procedural worlds (chunk streaming with 8 biomes incl. GraphRoom dungeons with doors / T-junctions / boss rooms)
- Permadeath mode + meta-progression telemetry (IProfileDb / JsonProfileDb / StatisticsHUD)

### Source (Python)

| Component | Technology |
| ----------- | ----------- |
| Engine | Pygame Community Edition |
| ECS | Custom Python (dict-based, 45+ components) |
| Data | JSON configs + SQLAlchemy/SQLite |
| Validation | Pydantic + JSON Schema |
| Testing | pytest with headless fixtures |

### Target (Unity)

| Component | Technology |
| ----------- | ----------- |
| Engine | Unity 2022.3.62f1 LTS |
| Rendering | Universal Render Pipeline (URP) 2D |
| Input | New Input System 1.7.0 |
| Camera | Cinemachine 2.9.7 |
| Data | ScriptableObjects + Newtonsoft JSON |
| Testing | Unity Test Framework (NUnit) |

---

## 2. Architecture Comparison

### Game Loop

```text
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
| --------------- | --------------- | -------- |
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
| `roguelike_editors/` | `Valkur.Gameplay/Editors/Tile/` + `Editors/Map/` | ✅ Done |
| `roguelike_engine/rendering/day_night/` | `Valkur.Gameplay/World/Lighting/DayNightCycle.cs` | ✅ Done |
| `roguelike_engine/audio/` | `Valkur.Infrastructure/AudioManager` + `CombatAudioSystem` | ✅ Done |
| `minigames/` | — | 🚫 Permanently deprecated (Pylos + Soluna out of scope) |

---

## 3. Available Agents & Skills

### Agents (`.github/agents/`)

Use these specialized agents for focused migration tasks:

| Agent | Purpose | When to Use |
| ------- | --------- | ------------- |
| **@python-analyst** | Analyzes Python source code | Before porting any system — understand the Python implementation first |
| **@unity-architect** | Designs and writes Unity C# code | When implementing new features or porting systems |
| **@data-migrator** | Handles data conversion | When migrating JSON configs to ScriptableObjects |
| **@migration-qa** | Tests and validates parity | After porting any system — verify correctness |
| **@asset-pipeline** | Manages sprite/audio import | When importing or organizing game assets |

### Skills (`.github/skills/`)

Invoke these as slash commands for specific workflows:

| Skill | Purpose | When to Use |
| ------- | --------- | ------------- |
| `/python-to-csharp` | Python→C# translation reference | When translating specific code patterns |
| `/combat-migration` | Combat system porting guide | When working on combat, spells, damage |
| `/asset-pipeline` | Asset import and atlas reference | When handling sprites, audio, import rules |
| `/migration-testing` | Testing and validation procedures | When writing tests or checking parity |

### Recommended Workflow

```text
1. @python-analyst    → Analyze the Python system to port
2. /python-to-csharp  → Reference the translation patterns
3. @unity-architect   → Implement the C# version
4. @migration-qa      → Validate parity and correctness
```

---

## 4. Migration Status

### Phase Summary

| Phase | Description | Steps | Done | Status |
| ------- | ------------- | ------- | ------ | -------- |
| 0 | Preparation & baseline | 1-6 | 5/6 | 🟡 Step 4 (video baseline capture) pending — documentation only |
| 1 | Unity bootstrap | 7-12 | 6/6 | ✅ Complete |
| 2 | Assets & import pipeline | 13-22 | 6/10 | 🟡 Bulk reimport applied; sprite atlas consolidation + asset_map.csv finalisation pending |
| 3 | Data contracts & migrators | 23-30 | 8/8 | ✅ Complete |
| 4 | Vertical slice | 31-36 | 6/6 | ✅ Complete |
| 5 | Full gameplay port | 37-44 | 8/8 | ✅ Complete |
| 6 | Tools & editors | 45-47 | 3/3 | ✅ Complete |
| 7 | Persistence & release | 48-50 | 3/3 | ✅ Complete |
| 8 | Meta-progression layer (post-plan) | n/a | 5/5 | ✅ IProfileDb + telemetry + StatisticsHUD |

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
| ------ | ------ | ------------- |
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
| ------ | ---------- | ------------- |
| `burn_system.py` (status-effect DoT) | Low | /combat-migration |
| `item_factory.py` (procedural item rolls) | Low | @data-migrator → @unity-architect |
| `data/config/{lighting,audio,combo_rules}.json` configs (not in any SO yet) | Low | @data-migrator |

> **Resolved** during the 2026-05 sessions: NPC spell casting ✅ (NPCAutoCast + NPCCastState + BossPhaseController), Day/night cycle ✅, Asset pipeline platform compression overrides + bulk reimport ✅, Audio coverage (death / level-up / item-pickup) ✅, Item rarity palette + LootTable ✅, Quest system (IObjective + Quest + QuestManager + QuestLogHUD) ✅, Skill tree (SkillNode/Tree + LearnedSkills + SkillEffectApplicator + AuraRegistry + SkillTreeHUD) ✅, Boss framework (BossPhaseController + BossDefinition + BossConfigurator + BossHealthBarHUD + SampleBoss.asset) ✅, Procedural biomes (Checkerboard, Ring, RoomedChunk, GraphRoom with doors/T-junctions/boss rooms, ThemedGraphRoom) ✅, Permadeath flag + autosave cleanup ✅, Meta-progression layer (IProfileDb + ProfileTelemetrySystem + StatisticsHUD) ✅. Earlier (2026-04): Physics2D Collision Matrix ✅, Material leaks ✅, Audio pipeline ✅, Patrol waypoints ✅, Vendor buy/sell UI ✅.
>
> **Permanently deprecated** (out of scope, never to be revisited): Pylos minigame, Soluna placeholder.

---

## 6. Critical Issues

All previously critical issues have been resolved:

| ID | Issue | Status | Resolution |
| ---- | ------- | -------- | ------------ |
| P2.1 | Physics2D Layer Collision Matrix | ✅ Fixed 2026-04-07 | Proper per-layer matrix configured |
| P1.4 | Material leaks in WorldGridBuilder/TileEditorGridCursor | ✅ Fixed 2026-04 | Cached materials + `OnDestroy()` cleanup |

---

## 7. System-by-System Mapping

### Combat

| Python System | Unity Script | Parity |
| --------------- | ------------- | -------- |
| `combat_system.py` | `Health.cs` + `CombatFeedback.cs` | ✅ |
| `melee_system.py` + `slash_system.py` | `MeleeCombat.cs` | ✅ |
| `death_system.py` | `CombatFeedback.cs` (death fade) | ✅ |
| `hitbox_system.py` | `MeleeCombat.OverlapCircle` + `Projectile.OnTriggerEnter2D` | ✅ |
| `explosion_system.py` | `AreaExecutor.cs` | ✅ |
| `burn_system.py` | — | ❌ Not ported |
| `combat_sfx.py` | `CombatAudioSystem.cs` + `CombatSfxConfigSO.cs` | ✅ |

### Spells

| Python System | Unity Script | Parity |
| --------------- | ------------- | -------- |
| `spells_config.py` | `SpellDefinition.cs` (ScriptableObject) | ✅ |
| Fireball (projectile) | `ProjectileExecutor.cs` + `Projectile.cs` | ✅ |
| Laser beam | `LaserBeamExecutor.cs` + `LaserBeamController.cs` | ✅ |
| Teleport | `TeleportExecutor.cs` + `TeleportPortalFX.cs` | ✅ |
| Slash | `SlashExecutor.cs` | ✅ |
| Area | `AreaExecutor.cs` | ✅ |
| Dash | `DashExecutor.cs` + `DashAbility.cs` | ✅ |
| Boomerang / chain lightning / lightning / meteor / cone breath / arcane flame / aura / mine / puddle / wall / shield / smoke / vortex / totem / summon / firework launch | `*Executor.cs` + `*Controller.cs` (per spell) | ✅ |
| `auto_cast_system.py` (NPC casting) | `NPCAutoCast.cs` + `NPCCastState.cs` + `BossPhaseController.cs` | ✅ |

### AI/FSM

| Python State | Unity State | Parity |
| ------------- | ------------ | -------- |
| `idle_state.py` | FSMMonsterBrain Idle | ✅ |
| `attack_state.py` | FSMMonsterBrain Attack | ✅ |
| `cast_state.py` | `NPCCastState.cs` (FSM blocks movement) + `NPCAutoCast.cs` (rotation) + `BossPhaseController.cs` (HP-threshold phase rotations) | ✅ |
| `damage_state.py` | FSMMonsterBrain Damage | ✅ |
| `death_state.py` | FSMMonsterBrain Death | ✅ |
| `unconscious_state.py` | FSMMonsterBrain Unconscious | ✅ |
| Patrol state | FSMMonsterBrain Patrol + `PatrolWaypointGenerator.cs` | ✅ |
| Chase state | FSMMonsterBrain Chase | ✅ |
| Flee state | FSMMonsterBrain Flee | ✅ |

### World/Map

| Python System | Unity Script | Parity |
| --------------- | ------------- | -------- |
| `map_model.py` | Tilemap system | ✅ |
| `chunked_map_view.py` | `WorldGridBuilder.cs` | ✅ |
| `tile_model.py` | Unity Tile assets | ✅ |
| `spatial_index.py` | `SpatialHash.cs` | ✅ |
| `pathfinding.py` | `PathFinder.cs` (used by ChaseState) | ✅ |
| `day_night.py` | `DayNightCycle.cs` | ✅ |
| `zone/` | `ZoneManager.cs` + `WorldManager.cs` (multi-world) | ✅ |
| Procedural worlds (Phase 2 chunks) | `ChunkStreamer` + 8 `IBiome` impls (Uniform, NoiseSplit, Checkerboard, Ring, RoomedChunk, GraphRoom, ThemedGraphRoom) | ✅ Phase 2.6 |

### Inventory

| Python System | Unity Script | Parity |
| --------------- | ------------- | -------- |
| `inventory_ui_system.py` | `InventoryUI.cs` | ✅ |
| `inventory_transfer_system.py` | `VendorShopUI.cs` (+ `.Builder` / `.Rows` partials) | ✅ |
| `inventory_pickup_system.py` | `PickupSystem.cs` | ✅ |
| `inventory_drop_system.py` | `DropSystem.cs` | ✅ |
| `item_factory.py` | — | ❌ No procedural items |

### Persistence

| Python System | Unity Script | Parity |
| --------------- | ------------- | -------- |
| `shutdown_manager.py` | `SaveService.cs` | ✅ |
| Save/load state | `GameStateCollector.cs` + `GameStateRestorer.cs` | ✅ |
| Schema migration | `SaveSchemaMigrator.cs` + `MigrationChain<T>` (per-loader) | ✅ |
| File I/O | `SaveFileManager.cs` (atomic File.Replace + sidecar .bak) | ✅ |
| Permadeath cleanup | `PermadeathSaveCleanupSystem.cs` (deletes autosave on death when `GameSettings.permadeath`) | ✅ |
| Meta-progression DB (NEW — no Python equivalent) | `IProfileDb` + `JsonProfileDb` + `ProfileTelemetrySystem.cs` | ✅ Step 8 |

---

## 8. Data Migration Reference

### JSON Sources → ScriptableObjects

| Python Source | Unity ScriptableObject | Migration Tool |
| -------------- | ---------------------- | ---------------- |
| `data/entities/new_hostiles.json` | `MonsterDefinition` (11 assets) | `PythonDataMigrator` |
| `data/entities/new_players.json` | `PlayerDefinition` | `PythonDataMigrator` |
| `data/spells/spells.json` | `SpellDefinition` | `PythonDataMigrator` |
| `data/items/` | `ItemDefinition` | `PythonDataMigrator` |
| `data/spawners/` | `SpawnerDefinition` | `PythonDataMigrator` |
| `data/config/input_bindings.json` | Input System `.inputactions` | Manual |
| `data/config/lighting.json` | — | Not migrated |
| `data/config/audio.json` | — | Not migrated |
| `data/config/combo_rules.json` | — | Not migrated |

### Python SQLite (`roguelike.sqlite3`) — NOT migrated, by design

The Python project used SQLite as a queryable cache of the JSONs (re-imported on
content-hash change via `import_log`). In Unity that role is filled directly by
ScriptableObject catalogs + JSON instance files: every Python table has a 1:1
Unity equivalent with no DB layer needed.

Detailed table-by-table audit: see [`SQLITE_MIGRATION_AUDIT.md`](SQLITE_MIGRATION_AUDIT.md).

A *new* SQLite layer (`Valkur.Infrastructure.Persistence.Profile`) is added on top
for **meta-progression / telemetry data that did NOT exist in Python** — run
history, kill stats, achievements, player profile. Orthogonal to the existing
SO + JSON layer.

### Migration Commands

- **Full migration:** Unity menu → `Valkur > Migration > Import All`
- **Dry-run (validate only):** Unity menu → `Valkur > Migration > Dry-Run All (Validate Only)`
- **Reports:** Check Unity Console for OK/Warning/Error counts per domain

---

## 9. Asset Pipeline Reference

### Current Status

| Category | Python Count | Unity Imported | Status |
| ---------- | ------------- | --------------- | -------- |
| Tiles | ~hundreds | Partial | ⚠️ Phase 2 |
| Characters | ~dozens sheets | Partial | ⚠️ Phase 2 |
| NPC | ~dozens sheets | Partial | ⚠️ Phase 2 |
| Spells/VFX | ~dozens | Partial | ⚠️ Phase 2 |
| Items | ~dozens | Partial | ⚠️ Phase 2 |
| UI | ~dozens | Partial | ⚠️ Phase 2 |
| Audio | ~dozens WAV/OGG | 0 | ❌ Empty |

### Import Settings (ValkurAssetPostprocessor.cs)

| Path Contains | PPU | Filter | Compression |
| -------------- | ----- | -------- | ------------ |
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
| ------- | ------ | ---------- | -------- |
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

```text
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

```text
Step 1: Read Python JSON source
Step 2: Read Unity DTO/ScriptableObject class
Step 3: Use @data-migrator to map fields
Step 4: Run dry-run validation
Step 5: Execute migration
Step 6: Verify counts and spot-check values
```

### Migrating Assets

```text
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

```text
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
