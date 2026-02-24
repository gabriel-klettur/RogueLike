# Valkur — Roguelike RPG

<p align="center">
  <strong>A full-featured top-down roguelike RPG built from scratch in Python/Pygame, now being migrated to Unity/C# — featuring a custom ECS architecture, procedural dungeon generation, real-time combat with 25+ spells, FSM-driven AI, in-game editors, a lighting engine, and a complete save/load pipeline.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Python-3.11+-3776AB?logo=python&logoColor=white" alt="Python" />
  <img src="https://img.shields.io/badge/Pygame--CE-2.x-00CC00?logo=python&logoColor=white" alt="Pygame" />
  <img src="https://img.shields.io/badge/Unity-2022.3_LTS-000000?logo=unity&logoColor=white" alt="Unity" />
  <img src="https://img.shields.io/badge/C%23-10-239120?logo=csharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/SQLite-3-003B57?logo=sqlite&logoColor=white" alt="SQLite" />
  <img src="https://img.shields.io/badge/Alembic-Migrations-FF6F00" alt="Alembic" />
  <img src="https://img.shields.io/badge/ECS-Custom_Architecture-8B5CF6" alt="ECS" />
  <img src="https://img.shields.io/badge/FSM-AI_Driven-E11D48" alt="FSM" />
  <img src="https://img.shields.io/badge/pytest-Test_Suite-0A9EDC?logo=pytest&logoColor=white" alt="pytest" />
  <img src="https://img.shields.io/badge/NUnit-EditMode_Tests-25A162" alt="NUnit" />
</p>

---

## About This Project

**Valkur** is a top-down roguelike RPG developed as a monorepo with two parallel implementations:

1. **Python/Pygame** — A fully playable desktop game with a custom ECS engine, 25+ spell systems, FSM-driven monster AI, procedural map generation, a real-time lighting engine, drag-and-drop inventory, multiple in-game editors, and a SQLite persistence layer with Alembic migrations.

2. **Unity/C#** — An ongoing migration of the Python game into Unity, preserving all gameplay systems with a clean C# architecture: ScriptableObject-driven data, FSM enemy AI, a spell casting framework, tile/map editors, zone management, and a full save/load pipeline.

This is not a tutorial or prototype. The Python version alone contains **1,800+ source files** across engine, game, UI, and editor modules, with a comprehensive test suite. The Unity port already includes **14 EditMode test suites**, data migration tooling that imports directly from the Python data layer, and production-quality editor extensions.

The goal of this repository is to demonstrate how I design, architect, and maintain **complex game systems** with engineering rigor comparable to production software — across two different technology stacks.

---

## Table of Contents

- [Key Highlights](#key-highlights)
- [Architecture Overview](#architecture-overview)
- [Tech Stack](#tech-stack)
- [Python Game — Deep Dive](#python-game--deep-dive)
  - [ECS Architecture](#ecs-architecture)
  - [Combat & Spells](#combat--spells)
  - [AI — Finite State Machine](#ai--finite-state-machine)
  - [Rendering & Lighting](#rendering--lighting)
  - [In-Game Editors](#in-game-editors)
  - [Persistence & Data](#persistence--data)
- [Unity Migration — Deep Dive](#unity-migration--deep-dive)
  - [Core Architecture](#core-architecture)
  - [Gameplay Systems](#gameplay-systems)
  - [Editor Tooling](#editor-tooling)
  - [Data Pipeline](#data-pipeline)
- [Testing Strategy](#testing-strategy)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Roadmap](#roadmap)
- [About Me](#about-me)

---

## Key Highlights

| Area | What I Built |
|------|-------------|
| **Custom ECS Engine (Python)** | Component registry, system registry, spatial indexing, spawn manager — all from scratch with strict domain separation |
| **25+ Spell Systems** | Fireball, chain lightning, meteor shower, dash, boomerang, teleport, force field, mines, puddles, cone breath, totems, summons, and more — each with dedicated ECS systems |
| **FSM-Driven Monster AI** | 9 states (Idle, Patrol, Chase, AlertChase, Attack, Flee, Damage, Unconscious, Death) with animation bridges and configurable AI parameters |
| **Real-Time Lighting Engine** | Day/night cycle, lightmap rendering, shadow polygons, occlusion tiles, light caching, quality presets, and staggered updates for performance |
| **Procedural Map Generation** | Multi-zone worlds with collision, pathfinding, building placement, and overlay layers |
| **7 In-Game Editors** | Tiles, buildings, map, inventory, items, entities, and spells — full CRUD with UI panels built on a reusable widget framework |
| **Drag-and-Drop Inventory** | Transfer system, death drops, map-load drops, pickup system, and a complete inventory UI with item factories |
| **SQLite + Alembic Persistence** | Structured data layer with schema migrations, JSON/SQLite hybrid storage, and save-slot management |
| **Unity C# Migration** | Clean port of all Python systems into Unity with ScriptableObject catalogs, Assembly Definitions, ServiceLocator, and automated data migration from Python JSON/SQLite |
| **14 EditMode Test Suites (Unity)** | NUnit tests covering FSM, combat, health, inventory, save data, spatial hashing, spell casting, zone management, and more |
| **40+ Python Test Files** | pytest suites covering ECS systems, combat, spells, FSM, editors, rendering, persistence, and integration |
| **Performance Tooling** | Benchmark framework with JSON export, performance overlay, entity culling, spatial hashing, chunk-based rendering, and object pooling |

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│              PYTHON / PYGAME  (Fully Playable Desktop Game)              │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                    roguelike_engine (Motor)                         │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐  │  │
│  │  │  Camera   │ │   Map    │ │ Lighting │ │  Cache   │ │ Config │  │  │
│  │  │  2D +     │ │  MVC +   │ │  Engine  │ │ Memory + │ │ Tiles, │  │  │
│  │  │  Viewport │ │  Chunks  │ │ Day/Night│ │ Disk     │ │ Editor │  │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └────────┘  │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐  │  │
│  │  │  Tiles   │ │Buildings │ │ Minimap  │ │ Console  │ │ Diag/  │  │  │
│  │  │  MVC     │ │  MVC     │ │          │ │  MVC     │ │ Debug  │  │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └────────┘  │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                   roguelike_game (Gameplay)                         │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐  │  │
│  │  │   ECS    │ │  Game    │ │   Map    │ │  Menu    │ │Editors │  │  │
│  │  │Components│ │  Loop    │ │ Loader + │ │  MVC     │ │ 7 in-  │  │  │
│  │  │ Systems  │ │ Managers │ │ Pathfind │ │          │ │ game   │  │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └────────┘  │  │
│  │                                                                    │  │
│  │  ECS Systems:                                                      │  │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐    │  │
│  │  │ Input  │ │Physics │ │ Combat │ │  FSM   │ │  Rendering   │    │  │
│  │  │        │ │Movement│ │ Spells │ │   AI   │ │  40+ systems │    │  │
│  │  │        │ │Facing  │ │ Melee  │ │9 states│ │  Health bars │    │  │
│  │  │        │ │Collisn │ │ Hitbox │ │  Anim  │ │  Particles   │    │  │
│  │  └────────┘ └────────┘ └────────┘ └────────┘ └──────────────┘    │  │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐    │  │
│  │  │Invent. │ │ Items  │ │ Audio  │ │Particl.│ │  Experience  │    │  │
│  │  │ UI/DnD │ │Factory │ │  SFX   │ │Effects │ │  Leveling    │    │  │
│  │  └────────┘ └────────┘ └────────┘ └────────┘ └──────────────┘    │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                    roguelike_ui (UI Framework)                      │  │
│  │  Widgets: Button, Grid, ScrollPanel, TabPanel, TextInput,          │  │
│  │           ToolbarPanel, TitleBar, Hover, MenuConfigurator,         │  │
│  │           MenuRenderer, PickerPanel, FileSystemPicker              │  │
│  │  HUD: ActionGrid (MVC), InputProfiles, HUDOrchestrator            │  │
│  │  Services: JsonPersistence, Formatting, UIBlocker                  │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                roguelike_editors (Editor Tools)                     │  │
│  │  Tiles Editor · Buildings Editor · Map Editor · Inventory Editor   │  │
│  │  Items Editor · Entities Editor · Spells Editor · Spawner Editor   │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│      Data: SQLite + Alembic │ JSON schemas │ YAML configs                │
│      Persistence: Save slots │ World state │ Inventory maps              │
└──────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────┐
│                 UNITY / C#  (Migration in Progress)                       │
│                                                                          │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐  │
│  │   Core   │ │   Data   │ │ Gameplay │ │    UI    │ │    Editor    │  │
│  │ Bootstrap│ │ SO-driven│ │ Combat,  │ │ HUD,     │ │ Data Migr.   │  │
│  │ Services │ │ Catalogs │ │ Spells,  │ │ Menus,   │ │ Asset Binder │  │
│  │ Events   │ │ Defns.   │ │ FSM AI,  │ │ Debug,   │ │ Content Val. │  │
│  │ Pooling  │ │ Save     │ │ Tile Ed. │ │ Death    │ │ Atlas Build  │  │
│  │ Perf Mon │ │          │ │ Map Ed.  │ │          │ │ Build Valid. │  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────────┘  │
│                                                                          │
│  Assembly Definitions: Valkur.Core · Valkur.Data · Valkur.Gameplay      │
│                        Valkur.UI · Valkur.Editor · Valkur.Tests         │
│                                                                          │
│  14 EditMode Test Suites (NUnit) · PlayMode Tests · ScriptableObjects  │
└──────────────────────────────────────────────────────────────────────────┘
```

### Design Principles Applied

- **Separation of Concerns** — Engine (rendering, data) → Game (ECS, gameplay) → UI (widgets, HUD) → Editors (tools), with strict module boundaries enforced by Assembly Definitions in Unity and package-based imports in Python
- **Security by Default** — All external data (JSON, SQLite, save files, user input) validated via Pydantic/JSONSchema (Python) and null-safe deserialization (Unity) before processing
- **Explicit Error Handling** — No silently swallowed exceptions; every system handles edge cases (missing components, invalid states, corrupt saves) with clear logging
- **Scalability Awareness** — Spatial hashing, entity culling, chunk-based rendering, object pooling, and staggered lighting updates prevent performance degradation at scale
- **Clean Code Over Clever Code** — MVC per domain, typed components, documented intent for complex AI/spell logic
- **Deterministic & Predictable** — Pure ECS systems with no hidden side effects; FSM states with explicit transitions; immutable spell definitions

---

## Tech Stack

### Python (Desktop Game)

| Technology | Purpose |
|------------|---------|
| **Python 3.11+** | Type hints, dataclasses, structural pattern matching |
| **Pygame-CE** | Rendering, input, audio, and windowing |
| **tcod** | Field-of-view, pathfinding, and procedural generation |
| **SQLite + SQLAlchemy** | Structured persistence for entities, spawners, and world data |
| **Alembic** | Database schema migrations |
| **Pydantic** | Data validation for configs, spell definitions, and entity schemas |
| **JSONSchema** | Schema enforcement for all JSON data files |
| **PyYAML** | Configuration file parsing |
| **pytest** | Test framework with Pygame headless fixtures |
| **Ruff** | Linting and formatting |
| **PyInstaller** | Standalone executable packaging |
| **websockets + aiortc** | Multiplayer networking foundation (WebRTC/WebSocket) |

### Unity (C# Migration)

| Technology | Purpose |
|------------|---------|
| **Unity 2022.3 LTS** | Game engine with Tilemap, 2D rendering, and UI Toolkit |
| **C# 10** | Full type safety with nullable reference types |
| **ScriptableObjects** | Data-driven catalogs for players, monsters, items, spells, and spawners |
| **Assembly Definitions** | Strict compile-time separation: Core, Data, Gameplay, UI, Editor, Tests |
| **NUnit** | EditMode test framework with 14 test suites |
| **TextMeshPro** | Rich text rendering for HUD, nameplates, and debug overlays |
| **Cinemachine** | Smooth camera follow and framing |
| **SpriteAtlas** | Optimized sprite batching for characters and tiles |
| **ServiceLocator** | Runtime service injection for audio, VFX, and core systems |

### Shared Patterns

| Pattern | Where |
|---------|-------|
| **Entity-Component-System** | Custom ECS in Python; component-based architecture in Unity |
| **Finite State Machine** | AI behavior for both Python (9 states + player states) and Unity (9 mirrored states) |
| **MVC per Domain** | Map, Tiles, Buildings, Console, Menu, Editors — consistent across both codebases |
| **Data Migration Pipeline** | Unity's `PythonDataMigrator` imports JSON/SQLite data directly from the Python project |
| **Event-Driven Architecture** | `GameEvents` in Unity; event systems per module in Python |

---

## Python Game — Deep Dive

### ECS Architecture

The game runs on a **custom Entity-Component-System** built from scratch:

| Layer | Implementation |
|-------|---------------|
| **Core** | `manager.py` (world), `component_registry.py`, `system_registry.py`, `spatial_index.py`, `spawn_manager.py` |
| **Components** | 15+ domains: `abilities/`, `ai/`, `combat/`, `fsm/`, `inventory/`, `items/`, `particles/`, `physics/`, `rendering/`, `spawn/`, `spawner/`, `stats/`, `status/`, `transform/` |
| **Systems** | 12 system groups: Input, Physics, Combat (melee + 25 spell systems), Rendering (40+ render systems), Inventory, Items, Experience, FSM, Audio, Particles, Lighting, Spawner |
| **Managers** | Game loop orchestration via `core/` (game, events, update, render, loop, state, shutdown), ECS execution via `ecs/` (loader, runner, spawner) |

### Combat & Spells

Every spell is a **dedicated ECS system** with its own physics, visuals, and lifecycle:

| Spell System | Mechanics |
|-------------|-----------|
| **Fireball** | Projectile with explosion radius, burn DOT, and building damage |
| **Chain Lightning** | Multi-target chaining with damage falloff |
| **Meteor Shower** | Area-targeted multi-impact with telegraph indicators |
| **Meteor Fall** | Single high-damage impact with wind-up animation |
| **Dash** | Directional movement ability with invincibility frames |
| **Boomerang** | Return-path projectile with hit detection on both passes |
| **Cone Breath** | Directional AOE with facing-dependent hitbox |
| **Force Field** | Persistent area denial with push-back physics |
| **Mine** | Placement-based trap with proximity trigger |
| **Puddle** | Persistent ground effect with status application |
| **Teleport** | Instant repositioning with validation |
| **Summon/Totem/Wall** | Entity-spawning spells with independent AI/duration |
| **Laser Beam** | Continuous channeled damage with hit detection |
| **And more...** | Aura, smoke, arcane flame, sphere shield, firework launch |

Supporting systems: `hitbox_system.py` (28K lines — collision detection and resolution), `spell_casting_system.py` (orchestration, cooldowns, mana gating), `combat_sfx.py`, `explosion_system.py`.

### AI — Finite State Machine

Monsters use a **hierarchical FSM** with dedicated states for both NPCs and the player:

```
Monster States: Idle → Patrol → AlertChase → Chase → Attack → Flee → Damage → Unconscious → Death
Player States:  Idle → Move → Attack → SpellSelect → SpellCast → SpellChannel → SpellRelease → SpellCooldown → Interact → Damage → Death
```

- `fsm_system.py` updates all FSM-driven entities per frame
- `anim_bridge.py` synchronizes FSM state transitions with sprite animation
- Each state has configurable parameters via `AIConfig`
- Monster archetypes define behavior profiles (aggression ranges, flee thresholds, patrol patterns)

### Rendering & Lighting

| System | Description |
|--------|------------|
| **Chunk-based Map Rendering** | Only visible chunks are drawn; off-screen tiles are skipped entirely |
| **Day/Night Cycle** | Full `daynight.py` engine with configurable speed, ambient color shifts, and light intensity curves |
| **Lightmap Rendering** | Per-pixel lighting with `lightmap.py`, gradient blending, and cached light surfaces |
| **Shadow Polygons** | `shadows_poly.py` casts geometric shadows from light sources against occlusion tiles |
| **Light Grid + Culling** | Spatial grid for light sources with frustum culling and staggered per-frame updates |
| **Quality Presets** | Configurable rendering quality (light resolution, shadow detail, update frequency) |
| **40+ Render Systems** | Health bars, nameplates, spell collision debug, telegraph indicators, toast notifications, mana bars, experience overlays, trail effects, flash effects, grayscale death, and more |

### In-Game Editors

7 fully functional editors accessible at runtime:

| Editor | Capabilities |
|--------|-------------|
| **Tiles Editor** | Brush tool, eyedropper, layer view, collision view, batch delete/default, toolbar with tools |
| **Buildings Editor** | Placement, hitbox editing, outline rendering, zone assignment |
| **Map Editor** | Zone management, overlay editing, world-level operations |
| **Inventory Editor** | Item manipulation, slot management, persistence |
| **Items Editor** | Item definition editing, factory configuration |
| **Entities Editor** | Entity inspection, component editing, FSM state visualization |
| **Spells Editor** | Spell parameter tuning, hot-reload (F4), visual testing |
| **Spawner Editor** | Template management, wave configuration, visual properties editing |

All editors share a common **widget framework** (`roguelike_ui/widgets/`): Button, Grid, ScrollPanel, TabPanel, TextInput, ToolbarPanel, TitleBar, Hover, MenuConfigurator, PickerPanel, and more.

### Persistence & Data

| Layer | Implementation |
|-------|---------------|
| **SQLite** | Primary structured storage (`roguelike.sqlite3`) for entities, spawners, and world state |
| **Alembic** | Schema migrations for database evolution |
| **JSON** | 750+ data files across configs, inventories, entities, FSM definitions, chat, vendors, spells, maps, and worlds |
| **JSONSchema** | Validation schemas for buildings, chat, editors, entities, inventory, items, and vendors |
| **Save System** | Multi-slot save/load with world state serialization |
| **Benchmark Export** | Performance data exported to JSON for analysis |

---

## Unity Migration — Deep Dive

### Core Architecture

The Unity port uses **Assembly Definitions** for strict compile-time module separation:

| Assembly | Contents |
|----------|----------|
| **Valkur.Core** | `GameBootstrap`, `GameDirector`, `GameEvents`, `ServiceLocator`, `EntityRegistry`, `EntityCulling`, `ObjectPool`, `PerformanceMonitor`, `SceneTransitionManager`, `SortingConfig`, `SingletonMonoBehaviour` |
| **Valkur.Data** | ScriptableObject definitions: `PlayerDefinition`, `MonsterDefinition`, `ItemDefinition`, `SpellDefinition`, `SpawnerDefinition`, `EntityAssetConfig`, `EntityStats`, `PlayerClassCatalog`, `PlayerSelectionState`, `SaveData` |
| **Valkur.Gameplay** | All runtime gameplay systems (see below) |
| **Valkur.UI** | `PlayerHUD`, `TargetHUD`, `DebugHUD`, `HUDManager`, `HUDBootstrap`, `DeathScreenUI`, `MainMenu` |
| **Valkur.Editor** | Editor-only tooling (see [Editor Tooling](#editor-tooling)) |
| **Valkur.Tests.EditMode** | 14 NUnit test suites |

### Gameplay Systems

| Domain | Systems |
|--------|---------|
| **Combat** | `MeleeCombat`, `Health`, `Mana`, `Experience`, `DashAbility`, `CombatFeedback`, `CombatRangeVisualizer`, `FacingIndicator`, `FloatingDamageNumber/Spawner`, `MouseTargetDetector`, `WorldHealthBar` |
| **Spells** | `SpellCaster` (orchestration + mana gating), `ISpellExecutor` interface, `ProjectileExecutor`, `AreaExecutor`, `SlashExecutor`, `DashExecutor`, `Projectile`, `FireballVisual` |
| **Enemy AI (FSM)** | `StateMachine`, `FSMMonsterBrain`, `FSMComponents` + 9 states: `IdleState`, `PatrolState`, `ChaseState`, `AlertChaseState`, `AttackState`, `FleeState`, `DamageState`, `UnconsciousState`, `DeathState` |
| **Inventory** | `Inventory`, `InventoryUI`, `PickupSystem`, `DropSystem`, `WorldPickup` |
| **World** | `ZoneManager` (multi-zone), `WorldGridBuilder`, `TilemapLayerSetup`, `SpatialHash`, `CameraSetup`, `YSortEntity` |
| **Tile Editor** | `TileEditorManager`, `TileEditorUI/UIBuilder`, `TileBrush`, `TileCatalog`, `TileRegistry`, `TileEditorInputHandler`, `TileEditorUndoSystem`, `TileEditorGridCursor`, `TileEditorBorderOverlay`, `TileEditorDiagnostics` |
| **Map Editor** | `MapEditorManager`, `MapEditorUI`, `MapEditorInputHandler`, `MapEditorState` |
| **Save System** | `SaveService`, `SaveFileManager`, `GameStateCollector`, `GameStateRestorer`, `SaveLoadInputHandler`, `SaveSchemaMigrator` |
| **Bootstrap** | `GameplaySceneSetup`, `EntitySetup`, `EntityAnimationBinder`, `EntitySpriteHelper`, `ProjectilePrefabFactory` |
| **Player** | `PlayerController` (8-direction movement + mouse-facing), `DirectionalAnimator` (4/8-direction sprite animation with cardinal resolution) |

### Editor Tooling

Custom Unity Editor extensions for content pipeline automation:

| Tool | Purpose |
|------|---------|
| **PythonDataMigrator** | Imports 750+ JSON files and SQLite data from the Python project into Unity ScriptableObjects — entities, monsters, items, spells, spawners, tiles, and zone definitions |
| **PlayerCharacterAssetBinder** | Slices character sprite sheets (128×128), configures import settings, and binds sprites into `PlayerDefinition.assetConfig` |
| **CharacterAtlasBuilder** | Creates and validates `SpriteAtlas` for character sprites |
| **TileAtlasBuilder** | Builds optimized tile atlases |
| **TilePaletteBuilder** | Generates Unity Tile Palettes from tile catalogs |
| **ContentValidator** | Validates all ScriptableObject data integrity (missing sprites, null references, stat ranges) |
| **BuildValidator** | Pre-build checks for production readiness |
| **SortingLayerSetup** | Ensures correct sorting layer configuration |
| **ValkurAssetPostprocessor** | Automatic asset import settings enforcement |

### Data Pipeline

```
Python (data/, schemas/, roguelike.sqlite3)
    │
    ▼  PythonDataMigrator (Unity Editor tool)
    │
Unity ScriptableObjects (Assets/_Project/Data/, Resources/)
    │
    ▼  ContentValidator (integrity checks)
    │
Runtime (GameplaySceneSetup → EntitySetup → EntityAnimationBinder)
```

---

## Testing Strategy

| Layer | Tool | Coverage |
|-------|------|----------|
| **Python ECS Systems** | pytest | Combat, spells (fireball, meteor, laser, boomerang), FSM integration, hitboxes, friendly fire |
| **Python Editors** | pytest | Tiles toolbar, spawner panels, building editors, world rendering |
| **Python Persistence** | pytest | DB engine models, integrity imports, preflight persistence, schema validation |
| **Python Integration** | pytest | Blank world rendering, hostile AI behavior, NPC animation sync |
| **Unity FSM** | NUnit EditMode | All 9 states, transitions, brain initialization, cooldowns |
| **Unity Combat** | NUnit EditMode | Melee damage, health clamping, death triggers, experience gain |
| **Unity Spells** | NUnit EditMode | Mana consumption, insufficient mana gating, CanCast checks |
| **Unity Inventory** | NUnit EditMode | Add/remove, stacking, slot management, persistence |
| **Unity Save System** | NUnit EditMode | Serialization, schema migration, collector/restorer roundtrip |
| **Unity World** | NUnit EditMode | Zone management, spatial hash queries, tile brush operations |
| **Unity Data** | NUnit EditMode | Data migration validation, player selection, bootstrap |
| **Unity Animation** | NUnit EditMode | Directional animator, cardinal resolution, set building |
| **Python Static** | Ruff | Strict linting and formatting across all Python sources |
| **Type Safety** | Type hints (Python) + nullable refs (C#) | Full static analysis across both stacks |

---

## Project Structure

```
RogueLike/
├── python/                              # Fully playable Python/Pygame game
│   ├── launcher.py                      # Entry point
│   ├── requirements.txt                 # Dependencies
│   ├── setup.py                         # Editable package install
│   ├── alembic/                         # Database migrations
│   ├── schemas/                         # JSON validation schemas
│   ├── data/                            # 750+ data files (JSON, SQLite)
│   │   ├── roguelike.sqlite3            # Primary database
│   │   ├── config/                      # Input bindings, settings
│   │   ├── entities/                    # Entity definitions
│   │   ├── spells/                      # Spell definitions
│   │   ├── inventory/                   # Inventory maps
│   │   ├── worlds/                      # World/zone data
│   │   ├── saves/                       # Save slots
│   │   └── ...                          # buildings, chat, fsm, vendors, etc.
│   ├── assets/                          # Sprites, tilesets, audio, particles
│   ├── scripts/                         # Tooling: migrations, validators,
│   │                                    #   asset audit, schema enforcement
│   ├── tests/                           # 40+ test files
│   │   ├── roguelike_engine/            # Engine tests
│   │   ├── roguelike_game/              # Game logic tests
│   │   ├── roguelike_editors/           # Editor tests
│   │   └── roguelike_ui/               # UI widget tests
│   └── src/
│       ├── roguelike_engine/            # Low-level motor
│       │   ├── camera/                  # 2D camera + viewport
│       │   ├── map/                     # Map MVC + chunk rendering
│       │   ├── tile/                    # Tile MVC + loaders
│       │   ├── buildings/               # Building MVC + hitboxes
│       │   ├── rendering/lighting/      # Lighting engine (18 modules)
│       │   ├── minimap/                 # Configurable minimap
│       │   ├── z_layer/                 # Z-layer composition + persistence
│       │   ├── world/                   # World state + persistence
│       │   ├── cache/                   # Memory + disk cache framework
│       │   ├── console/                 # In-game console MVC
│       │   ├── diagnostics/             # Debug overlay MVC
│       │   ├── input/                   # Keyboard + mouse abstraction
│       │   ├── audio/                   # Audio engine
│       │   ├── chat/                    # Chat system
│       │   ├── config/                  # Engine configuration
│       │   ├── db/                      # Database layer
│       │   └── utils/                   # Loaders, benchmark, helpers
│       │
│       ├── roguelike_game/              # Gameplay layer
│       │   ├── ecs/
│       │   │   ├── core/                # ECS manager, registries, spatial index
│       │   │   ├── components/          # 15 component domains
│       │   │   │   ├── abilities/       # 27 ability components
│       │   │   │   ├── combat/          # 21 combat components
│       │   │   │   ├── ai/              # 9 AI components
│       │   │   │   └── ...              # physics, rendering, inventory, etc.
│       │   │   └── systems/             # 12 system groups
│       │   │       ├── combat/spells/   # 25+ spell systems
│       │   │       ├── fsm/states/      # FSM states (monster + player)
│       │   │       ├── rendering/       # 40+ render systems
│       │   │       ├── inventory/       # 7 inventory systems
│       │   │       └── ...              # input, physics, audio, particles
│       │   ├── managers/                # Game loop + editors (7 editors)
│       │   ├── factories/               # Entity + item factories
│       │   ├── config/                  # Spells, input, players config
│       │   └── map/                     # Map loading, generation, pathfinding
│       │
│       ├── roguelike_ui/                # Reusable UI framework
│       │   ├── widgets/                 # 15+ widget types
│       │   ├── hud/                     # HUD orchestrator + action grid
│       │   └── services/                # JSON persistence, formatting
│       │
│       └── roguelike_editors/           # Editor implementations
│
└── unity/
    └── Valkur/                          # Unity 2022.3 LTS project
        └── Assets/
            ├── _Project/
            │   ├── Scripts/
            │   │   ├── Core/            # Bootstrap, events, services, pooling
            │   │   ├── Data/            # ScriptableObject definitions
            │   │   ├── Gameplay/
            │   │   │   ├── Bootstrap/   # Scene setup, entity setup, animation
            │   │   │   ├── Combat/      # 12 combat components
            │   │   │   ├── Spells/      # 7 spell executors + spell caster
            │   │   │   ├── Enemies/FSM/ # 9 AI states + brain + state machine
            │   │   │   ├── Inventory/   # Inventory + pickup + drops
            │   │   │   ├── Player/      # Controller + directional animator
            │   │   │   ├── World/       # Zone, grid, spatial hash, camera
            │   │   │   ├── TileEditor/  # 12 tile editor components
            │   │   │   ├── MapEditor/   # Map editor with zone management
            │   │   │   ├── Save/        # Save service + migration
            │   │   │   └── VFX/         # Visual effects
            │   │   ├── UI/              # HUD, menus, death screen
            │   │   ├── Editor/          # 10 editor tools (migration, atlas,
            │   │   │                    #   validation, asset binding)
            │   │   └── Infrastructure/  # Cross-cutting concerns
            │   ├── Art/                 # 19K+ art assets (characters, tiles,
            │   │                        #   buildings, items, UI, VFX)
            │   ├── Data/               # ScriptableObject instances
            │   ├── Resources/          # Runtime-loadable assets
            │   ├── Audio/              # Sound effects and music
            │   ├── Prefabs/            # Prefab templates
            │   └── Scenes/             # Game scenes
            │
            └── Tests/
                ├── EditMode/            # 14 NUnit test suites
                └── PlayMode/            # PlayMode test framework
```

---

## Getting Started

### Prerequisites

- **Python 3.11+** and **pip** (for the Python game)
- **Unity 2022.3 LTS** (for the Unity project)

### Run the Python game

```bash
cd python
python -m venv .venv

# Windows
.\.venv\Scripts\Activate
# Linux/macOS
source .venv/bin/activate

pip install -e .
pip install -r requirements.txt

python launcher.py
```

### Run Python tests

```bash
cd python
pytest -q
```

### Open the Unity project

1. Open **Unity Hub**
2. Add the `unity/Valkur/` folder as an existing project
3. Open with **Unity 2022.3 LTS**
4. Open `Assets/_Project/Scenes/MainMenu.unity` to start

### Run Unity tests

1. Open **Window → General → Test Runner**
2. Select **EditMode** tab
3. Click **Run All** — 14 test suites will execute

### Migrate Python data to Unity

In Unity Editor:
1. **Tools → Valkur → Migrate Python Data** — imports all JSON/SQLite data
2. **Tools → Valkur → Rebuild Player Character Assets** — slices and binds character sprites
3. **Tools → Valkur → Build Character Atlas** — generates optimized sprite atlas
4. **Tools → Valkur → Validate Content** — checks data integrity

---

## Roadmap

- [ ] Complete Unity migration of all Python gameplay systems
- [ ] Multiplayer networking via WebRTC/WebSocket (foundation exists in Python)
- [ ] Neural-network-trained enemy AI behaviors
- [ ] Procedural dungeon generation in Unity (ported from Python `map/generator.py`)
- [ ] Full lighting engine port to Unity (Universal Render Pipeline)
- [ ] Steam/itch.io distribution build
- [ ] End-to-end Playwright/integration tests
- [ ] CI/CD pipeline with automated test runs
- [ ] Localization system (i18n)
- [ ] Modding support via external data definitions

---

## About Me

I'm **Gabriel Astudillo Roca** — a full-stack developer who builds complex interactive systems with engineering rigor and product thinking.

This project demonstrates my ability to:

- **Architect complex game systems from scratch** — A custom ECS engine, 25+ spell systems, FSM-driven AI, a real-time lighting engine, and 7 in-game editors, all with strict separation of concerns and no framework magic
- **Work across multiple technology stacks** — Python/Pygame for rapid prototyping, Unity/C# for production — with a data migration pipeline that bridges both worlds
- **Build production-quality tooling** — In-game editors, data validators, asset pipelines, and migration scripts that I'd build for any engineering team
- **Design scalable architectures** — Entity culling, spatial hashing, chunk-based rendering, object pooling, and Assembly Definitions keep the codebase maintainable at 20K+ files
- **Write maintainable, tested code** — 40+ Python test files, 14 Unity EditMode test suites, schema validation, static analysis, and explicit error handling throughout
- **Own the full pipeline** — From database schema design to AI behavior trees to UI widget frameworks to editor tooling, I own every layer of the stack

**I'm looking for opportunities** where I can bring this level of engineering depth and product sensibility to a team building impactful software.

📧 **gabriel.astudillo.roca@gmail.com**
🔗 **[GitHub](https://github.com/gabriel-klettur)**

---

<p align="center">
  <em>Built with passion and engineering rigor by Gabriel Astudillo Roca</em>
</p>
