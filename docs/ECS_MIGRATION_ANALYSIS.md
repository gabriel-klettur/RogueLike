# Análisis ECS — Estado Actual y Plan de Migración

> Generado: 2026-02-13  
> Última actualización: 2026-02-13 (post-migración Fase 1)  
> Proyecto: RogueLike  
> Objetivo: Documentar el estado ECS del proyecto y las áreas pendientes de migración.

---

## 1. Arquitectura General

El proyecto se divide en 5 paquetes principales:

| Paquete | Rol | Patrón dominante |
|---|---|---|
| `roguelike_engine` | Infraestructura (audio, cámara, mapa, input, config, consola, minimap, buildings, chat, diagnostics) | **No-ECS** — Servicios, MVC, utilidades |
| `roguelike_game` | Lógica de juego (ECS, managers, factories, game loop) | **ECS-first** — ECS dominante + managers residuales |
| `roguelike_editors` | Editores de contenido (buildings, entities, FSM, inventory, items, map, particles, spawner, spells, tiles) | **No-ECS** — MVC / Controllers |
| `roguelike_ui` | Widgets UI reutilizables (botones, paneles, text input, menús) | **No-ECS** — UI pura |
| `minigames` | Mini-juegos independientes (Pylos, Soluna) | **No-ECS** — Standalone |

---

## 2. Elementos que YA son ECS ✅

### 2.1 Core ECS (`roguelike_game/ecs/`)

| Carpeta | Contenido | Estado |
|---|---|---|
| `ecs/core/manager.py` | `ECSWorld` — mundo, entidades, componentes, update/render loop | ✅ ECS |
| `ecs/core/component_registry.py` | Registro de ~62 tipos de componentes | ✅ ECS |
| `ecs/core/system_registry.py` | Registro de ~85+ sistemas (update + render) | ✅ ECS |
| `ecs/core/spatial_index.py` | Índice espacial para colisiones broad-phase | ✅ ECS |

### 2.2 Componentes ECS (`ecs/components/`) — ~62 componentes

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
| **Tags/Camera** | `PlayerTagComponent`, `NPCTagComponent`, `CameraFollowComponent` (con `enabled`, `defer_follow_frames`) | ✅ |
| **Chat** | `ChatComponent`, `VendorComponent` | ✅ |
| **Abilities** | `DashMeterComponent`, `ComboCounterComponent`, `ComboRulesComponent`, `MagicSpellBarComponent` | ✅ |
| **Buildings** | `BuildingHealth` | ✅ |
| **Class Change** | `ClassChangeRequest` (one-shot, consumido por `ClassChangeSystem`) | ✅ Nuevo |

### 2.3 Sistemas ECS (`ecs/systems/`) — ~85+ sistemas

| Dominio | Sistemas Update | Sistemas Render |
|---|---|---|
| **Core** | `SpawnSystem`, `SpawnStabilizationSystem`, `NpcRestoreSystem`, `NpcRespawnSystem`, `ClassChangeSystem`, `CameraFollowSystem`, `MinimapUpdateSystem` | — |
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

## 3. Elementos que NO son ECS

### 3.1 Managers (`roguelike_game/managers/`)

| Manager | Archivo(s) | Responsabilidad | Estado |
|---|---|---|---|
| **`GameState`** | `managers/core/state.py` | Estado global del juego (running, mode, chat state, editor states) | ⚠️ Híbrido — Hub central, parcialmente leído por sistemas ECS via `world.state` |
| **`Game`** | `managers/core/game.py` | Orquestador principal: init, loop, render, shutdown | ❌ No migrable — Entry point |
| **`UpdateManager`** | `managers/core/update_manager.py` | Coordina editores y buildings update | ✅ Migrado parcialmente — Cámara y minimap ya son ECS; solo queda `_step_entities` (buildings) |
| **`RendererManager`** | `managers/core/render/render_manager.py` | Pipeline de render: mapa → buildings → entidades → ECS → HUD → editores | ⚠️ Híbrido — Z-ordering de buildings+NPCs es mixto |
| **`MapManager`** | `managers/map/__init__.py` | Carga, generación, colisiones, pathfinding, render de mapa | ❌ No migrable — Infraestructura de datos del mundo |
| **`CollisionManager`** | `managers/map/collision.py` | Colisiones tile-based por zona | ⚠️ Híbrido — `SpatialIndex` ya es ECS, colisiones de tiles siguen en manager |
| **`ItemDropManager`** | `managers/map/item_drop_manager.py` | Persistencia JSON de drops en el mapa | ❌ No migrable — Infraestructura de I/O consumida por 10+ sistemas ECS como servicio |
| **`BuildingsManager`** | `managers/buildings/__init__.py` | Carga, calibración y update de edificios | ⚠️ Pendiente — Buildings tienen MVC propio en `roguelike_engine` |
| **`PlayerManager`** | `managers/player/player_manager.py` | Thin facade que encola `ClassChangeRequest` | ✅ Migrado — Lógica real en `ClassChangeSystem` ECS |
| **`ClassSelectorManager`** | `managers/player/class_selector_manager.py` | UI de selección de clase | ❌ No migrable — UI pura |
| **`MenuManager`** | `managers/menu/manager.py` | Menú principal (fondo, música, saves, opciones) | ❌ No migrable — UI/flujo de aplicación |
| **`ECSManager`** | `managers/ecs/__init__.py` | Wrapper que orquesta ECSWorld (load, spawn, update, render) | ✅ Ya es ECS (bridge) |
| **`ShutdownManager`** | `managers/core/shutdown_manager.py` | Guardado y limpieza al cerrar | ❌ No migrable — Lifecycle |
| **`LoopManager`** | `managers/core/loop_manager.py` | Game loop (FPS, timing) | ❌ No migrable — Infraestructura |
| **`AudioManager`** | `managers/core/audio_manager.py` | Volúmenes init-time para menú | ❌ No migrable — Complementario con `AudioSystem` ECS (init-time vs runtime) |

### 3.2 Engine (`roguelike_engine/`) — Infraestructura No-ECS

| Módulo | Responsabilidad | Estado |
|---|---|---|
| **`camera/camera.py`** | Cámara con offset, zoom, pixel-snap | ✅ Controlada por ECS — `CameraFollowSystem` invoca `camera.update()` |
| **`audio/`** | Servicio de audio (pygame backend, cache, config) | ❌ No migrable — Servicio consumido por `AudioSystem` ECS |
| **`buildings/`** | Modelo MVC de edificios (Building, BuildingModel, BuildingView, BuildingController) | ⚠️ Pendiente — Migración compleja a entidades ECS |
| **`chat/`** | Servicio de chat (providers IA, service layer) | ❌ No migrable — Servicio consumido por `ChatRouterSystem` ECS |
| **`config/`** | Configuración global (screen, tiles, map, input bindings) | ❌ No migrable — Configuración estática |
| **`console/`** | Consola de debug (MVC: model, view, controller, commands) | ❌ No migrable — Tooling |
| **`diagnostics/`** | Overlay de diagnóstico, recorder, benchmarks | ❌ No migrable — Tooling |
| **`input/`** | Captura de eventos pygame (keyboard, mouse) | ❌ No migrable — Infraestructura consumida por `InputSystem` ECS |
| **`map/`** | Modelo de mapa (layers, tiles, generación, cache) | ❌ No migrable — Datos del mundo |
| **`minimap/`** | MVC del minimapa (model, view, controller) | ✅ Controlado por ECS — `MinimapUpdateSystem` invoca `minimap.update()` |
| **`tile/`** | Modelo de tiles | ❌ No migrable — Datos |
| **`world/`** | Save/Load del mundo (WorldSnapshot, repository) | ❌ No migrable — Persistencia |
| **`z_layer/`** | Sistema de Z-ordering para render | ⚠️ Parcial — Usado desde ECS pero lógica en engine |
| **`zone/`** | Vista de zonas del mapa | ❌ No migrable — Datos/render de mapa |
| **`utils/`** | Utilidades (benchmark, mouse helpers) | ❌ No migrable — Helpers |

### 3.3 Event Handling (`managers/core/events/`)

| Archivo | Responsabilidad | Estado |
|---|---|---|
| `events.py` | Dispatcher central de eventos pygame | ⚠️ Híbrido — Delega a `InputSystem` ECS; NPC halo es input-consumer acoplado a pygame |
| `handlers/active_editors.py` | Toggle de editores | ❌ No migrable — UI/editor |
| `handlers/chat.py` | Apertura de chat, interacción | ✅ Bridge a ECS existente |
| `handlers/menu.py` | Eventos de menú | ❌ No migrable — UI |
| `handlers/toggles.py` | Hotkeys de debug/editores | ❌ No migrable — Tooling |
| `handlers/particles_map.py` | Input para colocar partículas en mapa | ❌ No migrable — Editor tooling |
| `handlers/npc_halo.py` | Click en halos de NPC | ❌ No migrable — Input-event consumer acoplado a pygame events y UI blocking |

### 3.4 Editores (`roguelike_editors/`) — No-ECS

Herramientas de desarrollo con patrón MVC propio. **No necesitan migración a ECS** — son tooling, no gameplay.

### 3.5 UI (`roguelike_ui/`) — No-ECS

Widgets reutilizables (botones, paneles, grids, text input, menú renderer). **No necesitan migración** — UI pura.

---

## 4. Zonas Híbridas Pendientes ⚠️

Áreas que mantienen lógica significativa fuera del ECS:

### 4.1 Render Pipeline
- **Estado**: `RendererManager` y `pipeline_runner.py` mezclan buildings (no-ECS) con entidades ECS usando `_NPCWrapper`.
- **Impacto**: Buildings se renderizan como objetos MVC, no como entidades ECS.
- **Migración pendiente**: Convertir buildings a entidades ECS con `Position`, `Sprite`, `ZLayer`, `BuildingHealth`.

### 4.2 Buildings
- **Estado**: MVC completo en `roguelike_engine/buildings/` con `Building`, `BuildingModel`, `BuildingView`, `BuildingController`. Se actualizan desde `BuildingsManager.update()`.
- **Migración pendiente**: Descomponer cada building en entidad ECS. Es la migración más compleja del proyecto.

### 4.3 GameState
- **Estado**: `GameState` es el hub central de estado global. Sistemas ECS lo leen via `world.state`.
- **Migración pendiente**: Migrar campos de gameplay a recursos/singletons ECS. Campos de editor/UI permanecen en `GameState`.

---

## 5. Plan de Migración Pendiente

### Fase 3 — Migración Mayor (Alto riesgo, alto impacto)
| # | Tarea | Archivos afectados | Complejidad |
|---|---|---|---|
| 3.1 | **Buildings → Entidades ECS** — Convertir buildings de MVC a entidades ECS | `roguelike_engine/buildings/`, `BuildingsManager`, `entities_renderer.py` | 🔴 Alta |
| 3.2 | **Render Pipeline ECS-first** — Eliminar `_NPCWrapper`, render unificado desde ECS | `render_manager.py`, `pipeline_runner.py`, `entities_renderer.py` | 🔴 Alta |
| 3.3 | **GameState → ECS Resources** — Migrar estado global a recursos/singletons ECS | `state.py`, múltiples sistemas | 🔴 Alta |

### No Migrar (Mantener como está)
- `Game` (entry point / orchestrator)
- `GameLoop` / `ShutdownManager` (lifecycle)
- `MenuManager`, `ClassSelectorManager` (UI)
- `AudioManager` (init-time config, complementario con `AudioSystem` ECS)
- `ItemDropManager` (infraestructura I/O consumida por sistemas ECS)
- `roguelike_editors/*` (tooling de desarrollo)
- `roguelike_ui/*` (widgets UI)
- `roguelike_engine/config/*`, `console/*`, `diagnostics/*`, `chat/*`, `audio/*`, `world/*`, `map/*`, `input/*` (infraestructura/servicios)
- `minigames/*` (standalone)

---

## 6. Resumen Cuantitativo

| Categoría | Cantidad | % del código de gameplay |
|---|---|---|
| ✅ **Ya es ECS** (componentes + sistemas + factories + migrados) | ~62 componentes, ~85 sistemas | **~75%** |
| ⚠️ **Híbrido / Pendiente** | ~5 módulos (buildings, render pipeline, GameState) | **~12%** |
| ❌ **No-ECS (gameplay residual)** | ~3 managers (UpdateManager residual, BuildingsManager, CollisionManager) | **~8%** |
| ❌ **No-ECS (no migrable)** | Editores, UI, config, engine services, lifecycle | **~5% gameplay / 100% tooling** |

**Conclusión**: Tras la migración de Fase 1, el proyecto tiene **~75% del gameplay en ECS**. Las áreas pendientes son: **Buildings → ECS** (la más compleja), **Render Pipeline unificado**, y **GameState → ECS Resources**. Todo lo demás (editores, UI, servicios de infraestructura) permanece correctamente fuera del ECS.

---

## 7. Historial de Migración

### 2026-02-13 — Fase 1 Completada ✅

**Sistemas creados:**

| Sistema | Archivo | Descripción |
|---|---|---|
| `CameraFollowSystem` | `ecs/systems/core/camera_follow_system.py` | Lee `CameraFollowComponent` + `Position`, centra la cámara respetando flags de supresión de editores (particles, map, item, spawner, MMB pan, debug overlays) |
| `MinimapUpdateSystem` | `ecs/systems/core/minimap_update_system.py` | Lee `Position` del jugador, delega a la fachada `Minimap` almacenada en `world.minimap` |
| `ClassChangeSystem` | `ecs/systems/core/class_change_system.py` | Consume `ClassChangeRequest` one-shot, aplica cambio completo de clase (sprites, stats, colliders, contexto FSM) |

**Componentes creados/modificados:**

| Componente | Archivo | Cambio |
|---|---|---|
| `CameraFollowComponent` | `ecs/components/core/camera_follow.py` | Extendido con `enabled` y `defer_follow_frames` |
| `ClassChangeRequest` | `ecs/components/core/class_change_request.py` | Nuevo — one-shot request con `new_class: str` |

**Managers refactorizados:**

| Manager | Cambio |
|---|---|
| `PlayerManager` | Reducido a thin facade que encola `ClassChangeRequest` |
| `update_manager.py` | Eliminados `_step_camera` y `_step_minimap`; solo queda `_step_entities` (buildings) |

**Inicialización:**

| Stage | Cambio |
|---|---|
| `stages/minimap.py` | Wires `game.minimap` → `ecs_world.minimap` para que `MinimapUpdateSystem` lo acceda |

### 2026-02-13 — Fase 2 Evaluada

| Tarea | Decisión | Razón |
|---|---|---|
| Event dispatch cleanup | ⏭️ No migrable | NPC halo handler es input-event consumer acoplado a pygame events y UI blocking |
| ItemDrop persistence | ⏭️ No migrable | `ItemDropManager` es infraestructura I/O consumida por 10+ sistemas ECS |
| AudioManager bridge | ⏭️ No migrable | Complementario con `AudioSystem` ECS (init-time vs runtime) |
