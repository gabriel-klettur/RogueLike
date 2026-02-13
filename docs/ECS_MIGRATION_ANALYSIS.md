# Análisis ECS — Estado Actual y Plan de Migración

> Generado: 2026-02-13  
> Proyecto: RogueLike  
> Objetivo: Identificar qué partes del código ya siguen el patrón ECS y cuáles necesitan migración.

---

## 1. Arquitectura General

El proyecto se divide en 4 paquetes principales:

| Paquete | Rol | Patrón dominante |
|---|---|---|
| `roguelike_engine` | Infraestructura (audio, cámara, mapa, input, config, consola, minimap, buildings, chat, diagnostics) | **No-ECS** — Servicios, MVC, utilidades |
| `roguelike_game` | Lógica de juego (ECS, managers, factories, game loop) | **Híbrido** — ECS + Managers procedurales |
| `roguelike_editors` | Editores de contenido (buildings, entities, FSM, inventory, items, map, particles, spawner, spells, tiles) | **No-ECS** — MVC / Controllers |
| `roguelike_ui` | Widgets UI reutilizables (botones, paneles, text input, menús) | **No-ECS** — UI pura |
| `minigames` | Mini-juegos independientes (Pylos, Soluna) | **No-ECS** — Standalone |

---

## 2. Elementos que YA son ECS ✅

### 2.1 Core ECS (`roguelike_game/ecs/`)

| Carpeta | Contenido | Estado |
|---|---|---|
| `ecs/core/manager.py` | `ECSWorld` — mundo, entidades, componentes, update/render loop | ✅ ECS |
| `ecs/core/component_registry.py` | Registro de ~60 tipos de componentes | ✅ ECS |
| `ecs/core/system_registry.py` | Registro de ~80+ sistemas (update + render) | ✅ ECS |
| `ecs/core/spatial_index.py` | Índice espacial para colisiones broad-phase | ✅ ECS |

### 2.2 Componentes ECS (`ecs/components/`) — ~60 componentes

| Dominio | Componentes | Estado |
|---|---|---|
| **Transform** | `Position`, `Velocity`, `MovementSpeed`, `Scale`, `ZLayer`, `TempZLayer` | ✅ |
| **Rendering** | `Sprite`, `Animator`, `AnimationTimer`, `FlashComponent`, `TrailComponent`, `GrayscaleComponent` | ✅ |
| **Combat** | `Health`, `Mana`, `Energy`, `Hunger`, `CombatStats`, `MeleeWeapon`, `MeleeRange`, `DamageConfig`, `AttackCooldown`, `InCombat`, `LastAttacker`, `HitboxComponent` | ✅ |
| **Spells** | `FireballComponent`, `ArcaneFlameComponent`, `FireworkLaunchComponent`, `SmokeComponent`, `SmokeEmitterComponent`, `SphereMagicShieldComponent`, `TeleportComponent`, `ExplosionComponent`, `AuraComponent`, `LaserBeamComponent`, `LightningComponent` | ✅ |
| **AI/FSM** | `NPCState`, `PatrolRoute`, `AggroRange`, `ChaseTarget`, `DefendArea`, `FacingCooldown`, `WantsToMelee`, `WantsToCastSpell`, `MonsterArchetype`, `MonsterInstanceComponent` | ✅ |
| **Physics** | `MultiCollider` | ✅ |
| **Input** | `InputComponent` | ✅ |
| **Inventory** | `InventoryComponent`, `PhysicalItemComponent`, `CollectibleComponent`, `ItemModels` | ✅ |
| **Experience** | `ExperienceComponent` | ✅ |
| **Particles** | `ParticleComponent`, `ParticlePresetComponent`, `SlashEmitterComponent`, `DashEmitterComponent` | ✅ |
| **Spawner** | `SpawnerConfig`, `SpawnerState`, `SpawnerChild`, `SpawnRequest` | ✅ |
| **Tags** | `PlayerTagComponent`, `NPCTagComponent`, `CameraFollowComponent` | ✅ |
| **Chat** | `ChatComponent`, `VendorComponent` | ✅ |
| **Abilities** | `DashMeterComponent`, `ComboCounterComponent`, `ComboRulesComponent`, `MagicSpellBarComponent` | ✅ |
| **Buildings** | `BuildingHealth` | ✅ |

### 2.3 Sistemas ECS (`ecs/systems/`) — ~80+ sistemas

| Dominio | Sistemas Update | Sistemas Render |
|---|---|---|
| **Core** | `SpawnSystem`, `SpawnStabilizationSystem`, `NpcRestoreSystem`, `NpcRespawnSystem` | — |
| **FSM** | `FSMSystem` | — |
| **Input** | `InputSystem` | — |
| **Physics** | `MovementCollisionSystem`, `FacingSystem`, `PlayerFacingSystem`, `CoinPickupSystem` | — |
| **Combat/Melee** | `MeleeCombatSystem`, `HitboxSystem`, `ComboSystem` | — |
| **Combat/Spells** | `SpellCastingSystem`, `FireballSystem`, `ArcaneFlameSystem`, `FireworkLaunchSystem`, `AuraSystem`, `SmokeSystem`, `SmokeEmitterSystem`, `TeleportSystem`, `SphereMagicShieldSystem`, `LightningSystem`, `DashSystem` | `FireballRenderSystem`, `ArcaneFlameRenderSystem`, `FireworkLaunchRenderSystem`, `SmokeRenderSystem`, `SmokeEmitterRenderSystem`, `TeleportRenderSystem`, `SphereMagicShieldRenderSystem`, `LightningRenderSystem` |
| **Combat/Other** | `ExplosionSystem`, `SpawnerDamageSystem`, `BuildingDamageSystem` | `ExplosionRenderSystem` |
| **Rendering** | `AnimationSystem`, `FlashSystem`, `TrailSystem`, `TempZLayerSystem` | `HealthBarSystem`, `ManaBarRenderSystem`, `ManaRegenAuraRenderSystem`, `GodmodeAuraRenderSystem`, `NamePlateSystem`, `DeathTimerBarSystem`, `HUDStatsRenderSystem`, `GrayscaleRenderSystem`, `ResurrectionAreaSystem`, `DropHoverRenderSystem`, `ExperienceRenderSystem`, `DashBarRenderSystem`, `ComboBarRenderSystem`, `MagicSpellBarRenderSystem`, `TargetHudRenderSystem`, `ToastRenderSystem` |
| **Particles** | `ParticleSystem`, `HealingAuraEmitterSystem`, `LaserBeamEmitterSystem`, `FireballTrailEmitterSystem`, `SlashEmitterSystem`, `DashEmitterSystem`, `LightningEmitterSystem` | `ParticleRenderSystem`, `ParticlePresetRenderSystem` |
| **Inventory** | `InventoryInitSystem`, `DeathDropSystem`, `InventoryPickupSystem`, `ConsumeSystem`, `InventoryTransferSystem`, `InventoryDragSystem`, `MapLoadDropsSystem`, `DropDespawnSystem`, `DropDragSystem` | `InventoryUISystem` |
| **Items** | `ConsumeSystem` | — |
| **Spawner** | `SpawnerPlacementSystem`, `SpawnerTriggerSystem`, `SpawnerRuntimeSystem` | `SpawnerDebugRenderSystem` |
| **Experience** | `ExperienceSystem`, `OrbAttractionSystem` | — |
| **Map** | `ExpansionSystem` | — |
| **Abilities** | `DashResourceSystem`, `ManaRegenSystem` | — |
| **Audio** | `AudioSystem` | — |
| **Chat** | `ChatProximitySystem`, `ChatRouterSystem` | `ChatProximityRenderSystem`, `ChatBubbleRenderSystem`, `ChatUISystem` |
| **Vendors** | `VendorTradeSystem` | — |
| **Debug** | — | `EntitiesDebugSystem` |

### 2.4 Factories ECS (`roguelike_game/factories/`)

| Factory | Rol | Estado |
|---|---|---|
| `factories/player/` | Crea entidad jugador con todos sus componentes ECS | ✅ ECS |
| `factories/monster/` | Crea entidades monstruo/NPC con componentes ECS | ✅ ECS |
| `factories/registry.py` | Registro de factories | ✅ ECS |

---

## 3. Elementos que NO son ECS ❌

### 3.1 Managers Procedurales (`roguelike_game/managers/`)

| Manager | Archivo(s) | Responsabilidad | Migrable a ECS? |
|---|---|---|---|
| **`GameState`** | `managers/core/state.py` | Estado global del juego (running, mode, chat state, editor states) | ⚠️ Parcial — El chat state ya tiene componentes ECS pero `GameState` sigue siendo el "hub" central |
| **`Game`** | `managers/core/game.py` | Orquestador principal: init, loop, render, shutdown | ❌ No — Es el entry point, no es lógica de gameplay |
| **`UpdateManager`** | `managers/core/update_manager.py` | Coordina update de cámara, editores, buildings, minimap | ⚠️ Parcial — Cámara y minimap podrían ser sistemas ECS |
| **`RendererManager`** | `managers/core/render/render_manager.py` | Pipeline de render: mapa → buildings → entidades → ECS → HUD → editores | ⚠️ Parcial — El Z-ordering de buildings+NPCs es híbrido |
| **`MapManager`** | `managers/map/__init__.py` | Carga, generación, colisiones, pathfinding, render de mapa | ❌ No migrable — Es infraestructura de datos del mundo |
| **`CollisionManager`** | `managers/map/collision.py` | Colisiones tile-based por zona | ⚠️ Parcial — `SpatialIndex` ya es ECS, pero colisiones de tiles siguen en manager |
| **`ItemDropManager`** | `managers/map/item_drop_manager.py` | Persistencia JSON de drops en el mapa | ⚠️ Parcial — Los drops ya son entidades ECS, pero la persistencia es procedural |
| **`BuildingsManager`** | `managers/buildings/__init__.py` | Carga, calibración y update de edificios | ❌ Complejo — Buildings tienen su propio MVC en `roguelike_engine` |
| **`PlayerManager`** | `managers/player/player_manager.py` | Cambio de clase del jugador (recarga sprites/stats) | ⚠️ Parcial — Opera sobre componentes ECS pero como procedimiento externo |
| **`ClassSelectorManager`** | `managers/player/class_selector_manager.py` | UI de selección de clase | ❌ No — Es UI pura |
| **`MenuManager`** | `managers/menu/manager.py` | Menú principal (fondo, música, saves, opciones) | ❌ No — Es UI/flujo de aplicación |
| **`ECSManager`** | `managers/ecs/__init__.py` | Wrapper que orquesta ECSWorld (load, spawn, update, render) | ✅ Ya es ECS (es el bridge) |
| **`ShutdownManager`** | `managers/core/shutdown_manager.py` | Guardado y limpieza al cerrar | ❌ No — Es lifecycle |
| **`LoopManager`** | `managers/core/loop_manager.py` | Game loop (FPS, timing) | ❌ No — Es infraestructura |
| **`AudioManager`** | `managers/core/audio_manager.py` | Bridge de audio | ⚠️ Parcial — Ya existe `AudioSystem` ECS |

### 3.2 Engine (`roguelike_engine/`) — Infraestructura No-ECS

| Módulo | Responsabilidad | Migrable? |
|---|---|---|
| **`camera/camera.py`** | Cámara con offset, zoom, follow | ⚠️ Podría ser un sistema ECS (`CameraSystem`) |
| **`audio/`** | Servicio de audio (pygame backend, cache, config) | ❌ No — Es servicio de infraestructura; `AudioSystem` ECS ya lo consume |
| **`buildings/`** | Modelo MVC de edificios (Building, BuildingModel, BuildingView, BuildingController) | ⚠️ Complejo — Los buildings son entidades complejas con su propio ciclo de vida |
| **`chat/`** | Servicio de chat (providers IA, service layer) | ❌ No — Es servicio externo; `ChatRouterSystem` ECS ya lo consume |
| **`config/`** | Configuración global (screen, tiles, map, input bindings) | ❌ No — Es configuración estática |
| **`console/`** | Consola de debug (MVC: model, view, controller, commands) | ❌ No — Es herramienta de desarrollo |
| **`diagnostics/`** | Overlay de diagnóstico, recorder, benchmarks | ❌ No — Es tooling |
| **`input/`** | Captura de eventos pygame (keyboard, mouse) | ❌ No — Es infraestructura; `InputSystem` ECS ya lo consume |
| **`map/`** | Modelo de mapa (layers, tiles, generación, cache) | ❌ No — Es datos del mundo |
| **`minimap/`** | MVC del minimapa (model, view, controller) | ⚠️ Podría ser un sistema ECS de render |
| **`tile/`** | Modelo de tiles | ❌ No — Es datos |
| **`world/`** | Save/Load del mundo (WorldSnapshot, repository) | ❌ No — Es persistencia |
| **`z_layer/`** | Sistema de Z-ordering para render | ⚠️ Parcial — Ya se usa desde ECS pero la lógica está en engine |
| **`zone/`** | Vista de zonas del mapa | ❌ No — Es datos/render de mapa |
| **`utils/`** | Utilidades (benchmark, mouse helpers) | ❌ No — Son helpers |

### 3.3 Event Handling (`managers/core/events/`)

| Archivo | Responsabilidad | Migrable? |
|---|---|---|
| `events.py` | Dispatcher central de eventos pygame | ⚠️ Parcial — Parte ya delega a `InputSystem` ECS |
| `handlers/active_editors.py` | Toggle de editores | ❌ No — Es UI/editor |
| `handlers/chat.py` | Apertura de chat, interacción | ⚠️ Ya tiene bridge a ECS |
| `handlers/menu.py` | Eventos de menú | ❌ No — Es UI |
| `handlers/toggles.py` | Hotkeys de debug/editores | ❌ No — Es tooling |
| `handlers/particles_map.py` | Input para colocar partículas en mapa | ⚠️ Parcial |
| `handlers/npc_halo.py` | Click en halos de NPC | ⚠️ Podría ser sistema ECS |

### 3.4 Editores (`roguelike_editors/`) — Completamente No-ECS

Todos los editores son herramientas de desarrollo con patrón MVC propio. **No necesitan migración a ECS** ya que son tooling, no gameplay.

### 3.5 UI (`roguelike_ui/`) — Completamente No-ECS

Widgets reutilizables (botones, paneles, grids, text input, menú renderer). **No necesitan migración** — son UI pura.

---

## 4. Zonas Híbridas (ECS parcial) ⚠️

Estas áreas ya tienen presencia ECS pero mantienen lógica significativa fuera del ECS:

### 4.1 Render Pipeline
- **Problema**: `RendererManager` y `pipeline_runner.py` orquestan el render mezclando buildings (no-ECS) con entidades ECS usando `_NPCWrapper` como adaptador.
- **Impacto**: Los buildings se renderizan como objetos MVC, no como entidades ECS.
- **Migración**: Convertir buildings a entidades ECS con componentes `Position`, `Sprite`, `ZLayer`, `BuildingHealth`.

### 4.2 Cámara
- **Problema**: La cámara es un objeto standalone en `roguelike_engine/camera/camera.py`. El follow-player está en `update_manager.py` con ~60 líneas de condicionales.
- **Migración**: Crear `CameraFollowSystem` que lea `CameraFollowComponent` + `Position`.

### 4.3 Minimap
- **Problema**: El minimapa es MVC en `roguelike_engine/minimap/`. Se actualiza desde `update_manager.py`.
- **Migración**: Crear `MinimapUpdateSystem` (update) y `MinimapRenderSystem` (render).

### 4.4 Buildings
- **Problema**: Los buildings tienen su propio modelo MVC completo en `roguelike_engine/buildings/` con `Building`, `BuildingModel`, `BuildingView`, `BuildingController`. Se actualizan desde `BuildingsManager.update()`.
- **Migración**: Es la migración más compleja. Requiere descomponer cada building en entidad ECS con componentes apropiados.

### 4.5 Event Dispatch
- **Problema**: `events.py` es un dispatcher monolítico de ~290 líneas que mezcla lógica de gameplay (chat, NPC interaction) con lógica de editores.
- **Migración**: Extraer eventos de gameplay a sistemas ECS (ej: `NPCInteractionSystem`).

### 4.6 Item Drop Persistence
- **Problema**: `ItemDropManager` persiste drops en JSON. Los drops ya son entidades ECS en runtime, pero la persistencia es procedural.
- **Migración**: Crear `ItemPersistenceSystem` que serialice/deserialice drops desde componentes ECS.

---

## 5. Plan de Migración Priorizado

### Fase 1 — Quick Wins (Bajo riesgo, alto valor)
| # | Tarea | Archivos afectados | Complejidad |
|---|---|---|---|
| 1.1 | **CameraFollowSystem** — Mover lógica de follow-player de `update_manager.py` a un sistema ECS | `update_manager.py`, nuevo `camera_follow_system.py` | 🟢 Baja |
| 1.2 | **MinimapSystem** — Mover update del minimapa a sistema ECS | `update_manager.py`, nuevo `minimap_system.py` | 🟢 Baja |
| 1.3 | **PlayerManager → ECS** — Refactorizar `change_class()` como sistema o comando ECS | `player_manager.py` | 🟢 Baja |

### Fase 2 — Consolidación (Riesgo medio)
| # | Tarea | Archivos afectados | Complejidad |
|---|---|---|---|
| 2.1 | **Event Dispatch cleanup** — Extraer lógica de gameplay de `events.py` a sistemas ECS existentes | `events.py`, `handlers/*.py` | 🟡 Media |
| 2.2 | **ItemDrop Persistence** — Integrar `ItemDropManager` como sistema ECS | `item_drop_manager.py`, `MapLoadDropsSystem` | 🟡 Media |
| 2.3 | **Collision unification** — Unificar `CollisionManager` con `SpatialIndex` | `collision.py`, `spatial_index.py` | 🟡 Media |
| 2.4 | **AudioManager bridge cleanup** — Eliminar `AudioManager` redundante, usar solo `AudioSystem` ECS | `audio_manager.py` | 🟢 Baja |

### Fase 3 — Migración Mayor (Alto riesgo, alto impacto)
| # | Tarea | Archivos afectados | Complejidad |
|---|---|---|---|
| 3.1 | **Buildings → Entidades ECS** — Convertir buildings de MVC a entidades ECS | `roguelike_engine/buildings/`, `BuildingsManager`, `entities_renderer.py` | 🔴 Alta |
| 3.2 | **Render Pipeline ECS-first** — Eliminar `_NPCWrapper`, render unificado desde ECS | `render_manager.py`, `pipeline_runner.py`, `entities_renderer.py` | 🔴 Alta |
| 3.3 | **GameState → ECS Resources** — Migrar estado global a recursos/singletons ECS | `state.py`, múltiples sistemas | 🔴 Alta |

### Fase 4 — No Migrar (Mantener como está)
Estos elementos **no deben** migrarse a ECS:
- `Game` (entry point / orchestrator)
- `GameLoop` / `ShutdownManager` (lifecycle)
- `MenuManager` (UI de aplicación)
- `ClassSelectorManager` (UI)
- `roguelike_editors/*` (tooling de desarrollo)
- `roguelike_ui/*` (widgets UI)
- `roguelike_engine/config/*` (configuración)
- `roguelike_engine/console/*` (debug tooling)
- `roguelike_engine/diagnostics/*` (profiling)
- `roguelike_engine/chat/` (servicio externo)
- `roguelike_engine/audio/` (servicio de infraestructura)
- `roguelike_engine/world/` (persistencia)
- `roguelike_engine/map/` (datos del mundo)
- `minigames/*` (standalone)

---

## 6. Resumen Cuantitativo

| Categoría | Cantidad | % del código de gameplay |
|---|---|---|
| ✅ **Ya es ECS** (componentes + sistemas + factories) | ~60 componentes, ~80 sistemas | **~65%** |
| ⚠️ **Híbrido / Parcialmente ECS** | ~10 módulos (camera, minimap, buildings, events, drops, render pipeline) | **~20%** |
| ❌ **No-ECS (gameplay)** | ~5 managers (GameState, UpdateManager, BuildingsManager, PlayerManager) | **~10%** |
| ❌ **No-ECS (no migrable)** | Editores, UI, config, engine services, lifecycle | **~5% gameplay / 100% tooling** |

**Conclusión**: El proyecto ya tiene una base ECS sólida (~65% del gameplay). Las áreas pendientes más importantes son: **Buildings** (la migración más compleja), **Render Pipeline** (unificación), y **Camera/Minimap** (quick wins). Los editores, UI y servicios de infraestructura no necesitan migración.
