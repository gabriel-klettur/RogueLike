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

## 4. Zonas Híbridas — Evaluadas ⚠️

Áreas que mantienen lógica fuera del ECS. Tras análisis detallado, se concluye que el patrón actual es correcto:

### 4.1 Render Pipeline — ✅ Bridge funcional
- **Estado**: `entities_renderer.py` unifica buildings (via `get_parts()`) y entidades ECS (via `_NPCWrapper`) en una lista z-ordenada.
- **Análisis**: `_NPCWrapper` es un proxy ligero (~118 líneas) con cache de escalado y tinting. El overhead por frame es mínimo.
- **Decisión**: **No migrar**. El patrón bridge funciona correctamente. Eliminar `_NPCWrapper` requeriría que buildings sean entidades ECS (ver 4.2).

### 4.2 Buildings — ✅ MVC correcto, no migrable
- **Estado**: MVC completo en `roguelike_engine/buildings/` (Building facade 349 líneas, BuildingModel 295 líneas, BuildingView 133 líneas, BuildingController 86 líneas).
- **Análisis**: 93 referencias en 27 archivos. El `buildings_editor` tiene 22 referencias directas. La clase `Building` expone ~15 propiedades (x, y, z, image, collision_map, split_ratio, visual states, flash, etc.) que requerirían ~10 componentes ECS nuevos.
- **Riesgo**: Reescribir el editor de buildings, el sistema de colisiones, la persistencia JSON, y el render pipeline — todo simultáneamente.
- **Decisión**: **No migrar**. El costo/beneficio es desfavorable. Buildings ya participan en z-ordering via `get_parts()` y en colisiones via `world.buildings`. El patrón actual es un bridge funcional.

### 4.3 GameState — ✅ Bus de comunicación correcto
- **Estado**: `GameState` es hub central leído por ECS via `world.state`. Campos: lifecycle (`running`, `mode`), editor flags (`buildings_editor_active`, `particles_editor_visible`, etc.), chat UI state, player class.
- **Análisis**: Los campos son mayoritariamente flags de editor/UI escritos por código no-ECS (game.py, input handlers, editores) y leídos por sistemas ECS. Migrar a singleton components ECS requeriría que todo el código no-ECS obtenga referencia al ECS world solo para setear un flag.
- **Decisión**: **No migrar**. `GameState` funciona como bus de comunicación entre capas no-ECS y ECS. El patrón bridge via `world.state` es limpio y eficiente.

---

## 5. Arquitectura Final — No Migrar

Estos elementos permanecen correctamente fuera del ECS:

### Infraestructura con bridge ECS
- `Building` MVC → bridge via `get_parts()` + `world.buildings`
- `Camera` → controlada por `CameraFollowSystem` via `camera.update()`
- `Minimap` → controlada por `MinimapUpdateSystem` via `minimap.update()`
- `GameState` → leída por ECS via `world.state`
- `AudioService` → consumida por `AudioSystem` ECS
- `ItemDropManager` → consumida por 10+ sistemas ECS como servicio I/O

### No-ECS puro (correcto)
- `Game` (entry point / orchestrator)
- `GameLoop` / `ShutdownManager` (lifecycle)
- `MenuManager`, `ClassSelectorManager` (UI)
- `AudioManager` (init-time config)
- `roguelike_editors/*` (tooling de desarrollo)
- `roguelike_ui/*` (widgets UI)
- `roguelike_engine/config/*`, `console/*`, `diagnostics/*`, `chat/*`, `audio/*`, `world/*`, `map/*`, `input/*` (infraestructura/servicios)
- `minigames/*` (standalone)

---

## 6. Resumen Cuantitativo

| Categoría | Cantidad | % del código de gameplay |
|---|---|---|
| ✅ **Ya es ECS** (componentes + sistemas + factories) | ~62 componentes, ~85 sistemas | **~75%** |
| ✅ **Híbrido con bridge funcional** | Buildings, Render Pipeline, GameState, Camera, Minimap | **~15%** |
| ❌ **No-ECS (residual)** | `UpdateManager` (solo buildings update), `BuildingsManager`, `CollisionManager` | **~5%** |
| ❌ **No-ECS (no migrable)** | Editores, UI, config, engine services, lifecycle | **~5% gameplay / 100% tooling** |

**Conclusión**: El proyecto tiene **~75% del gameplay en ECS puro** y **~15% en bridges funcionales** que conectan infraestructura no-ECS con el ECS. La migración ECS está **completa** para todos los casos donde el beneficio supera el costo. Las áreas restantes (Buildings MVC, GameState, Render Pipeline) funcionan correctamente con el patrón bridge actual y no justifican migración.

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

### 2026-02-13 — Fase 3 Evaluada

| Tarea | Decisión | Razón |
|---|---|---|
| Buildings → ECS | ❌ No migrable | 93 referencias en 27 archivos. `buildings_editor` tiene 22 refs directas. Requeriría ~10 componentes nuevos y reescribir editor, colisiones, persistencia y render pipeline simultáneamente. El bridge actual (`get_parts()` + `world.buildings`) funciona correctamente |
| Render Pipeline ECS-first | ❌ No migrable | Depende de Buildings→ECS. `_NPCWrapper` es proxy ligero con cache. `entities_renderer.py` ya unifica buildings y entidades ECS en lista z-ordenada. Overhead mínimo |
| GameState → ECS Resources | ❌ No migrable | Campos son mayoritariamente flags de editor/UI escritos por código no-ECS y leídos por ECS via `world.state`. Migrar a singletons ECS forzaría a editores/input handlers a obtener referencia al ECS world solo para setear flags. El bus de comunicación actual es limpio |

### Conclusión de migración

La migración ECS está **completa**. El proyecto alcanza **~75% ECS puro + ~15% bridges funcionales = ~90% del gameplay** integrado con ECS. Las áreas restantes (Buildings MVC, GameState, Render Pipeline) funcionan correctamente con patrones bridge y no justifican el riesgo de migración.
