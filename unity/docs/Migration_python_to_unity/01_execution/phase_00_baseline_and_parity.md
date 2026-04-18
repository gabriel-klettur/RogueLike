# Phase 00 Baseline and Functional Parity

**Document type:** Baseline evidence and parity matrix  
**Last updated:** 2026-04-08  
**Primary audience:** QA, gameplay, technical production

This document defines the baseline evidence model and parity matrix used to validate the Python -> Unity migration.

## Commit baseline

- Tag: `python-baseline-v1`
- Branch: `developing_v2`
- Commit: `d4279eb1`

## KPIs baseline (Python/Pygame)

- Resolucion: 1600x800
- FPS target: 60
- Optimizaciones activas: spawn budget (3/frame), asset sharing, spatial hash, frustum culling, entities set

### Metricas de referencia (estimadas del Python)

| Metrica | Valor Python (estimado) | Objetivo Unity |
| ------- | ----------------------- | -------------- |
| FPS medio (gameplay normal) | ~55-60 | >= 60 |
| Frame time p95 | ~18-20 ms | <= 16.6 ms |
| Tiempo de carga (mapa) | ~2-4 s | <= 2 s |
| RAM en gameplay | ~200-350 MB | <= 300 MB |
| Entidades simultaneas tipicas | 20-50 | >= 50 |

> **Nota:** Los valores Python son estimaciones basadas en observacion de gameplay.
> Para medicion precisa, usar `PerformanceMonitor.cs` (F3 overlay) en Unity.
> Pendiente: captura de video/screenshots del Python baseline para evidencia formal.

### Evidencias pendientes (Paso 4)

- [ ] Video corto (2-3 min) del gameplay Python mostrando flujos criticos.
- [ ] Capturas de pantalla de HUD, inventario, combate, spawner.
- [ ] Log de perfil del Python (FPS counter, frame time).
- [ ] Equivalentes en Unity para comparacion lado a lado.

## Flujos criticos jugables

| # | Flujo | Descripcion |
| - | ----- | ----------- |
| 1 | Movimiento | WASD + colision mundo + buildings + NPC separation |
| 2 | Combate melee | Click/tecla -> hitbox -> damage -> death -> drop |
| 3 | Spells | Cast -> projectile/area -> damage -> cooldown |
| 4 | Loot | Drop en suelo -> pickup -> inventario |
| 5 | Inventario | Abrir/cerrar -> drag -> consume -> transfer |
| 6 | IA/FSM | Idle -> patrol -> aggro -> chase -> attack -> flee -> death |
| 7 | Spawner | Trigger por proximidad -> spawn waves -> budget |
| 8 | Save/Load | Autosave + shutdown -> posicion + HP + inventario + NPC memory |
| 9 | Cambio de mapa | Portal -> cargar nuevo nivel -> restaurar NPCs |
| 10 | HUD | Barras HP/MP/XP + nameplates + target + toasts |

## Matriz de paridad funcional

> **Aviso 2026-04-18:** La columna **Estado Unity** registra paridad a nivel de *sistema*. Para los **editores in-game** (Buildings, Entities, Spells, FSM, Spawner, Tiles, Map, Items, Inventory, Particles, Lighting), el estado “DONE” significa “shell ejecutándose con F2-F11”, no “feature parity”. La cobertura real de panels / sub-panels / botones / undo es 5–35 %. Se añade abajo la columna **Detail %** para los editores y se referencia [`../03_audits/editor_and_feature_depth_gap_2026-04-18.md`](../03_audits/editor_and_feature_depth_gap_2026-04-18.md) para la matriz por-editor con evidencia.

| Capacidad Python | Prioridad | Estado Unity | Detail % | Referencia |
| ---------------- | --------- | ------------ | -------- | ---------- |
| Player movement + collision | P0 | **DONE** | - | `PlayerController.cs` (WASD + arrows, Rigidbody2D) |
| Camera follow | P0 | **DONE** | - | `CameraSetup.cs` (Cinemachine) |
| Tilemap render + sorting Y/Z | P0 | **DONE** | - | `WorldGridBuilder.cs`, `YSortEntity.cs`, `SortingConfig.cs` |
| Melee combat | P0 | **DONE** | - | `MeleeCombat.cs` (OverlapCircle + arc, cooldown, knockback) |
| Spell system (fireball, dash, etc) | P0 | **DONE** | - | `SpellCaster.cs` (8 types: Projectile/Slash/Area/Dash/Teleport/Boomerang/Lightning/ChainLightning) |
| FSM/AI (Idle, Patrol, Aggro, Attack, Flee, Death) | P0 | **DONE** | - | `FSMMonsterBrain.cs` + `StateMachine.cs` (9 states) |
| Spawn system + budget | P0 | **DONE** | - | `MonsterSpawner.cs` (from SpawnerDefinition) |
| Inventory + pickup + drop | P0 | **DONE** | - | `Inventory.cs`, `InventoryUI.cs`, `WorldPickup.cs`, `PickupSystem.cs`, `DropSystem.cs` |
| Item consume (potions/food) | P0 | **DONE** | - | `ItemConsumer.cs` (heal/mana/buff timed) |
| Currency / coin system | P0 | **DONE** | - | `CurrencyWallet.cs` + `CoinPickup.cs` (magnet, auto-collect) |
| Save/Load + autosave | P0 | **DONE** | - | `SaveService.cs` (checksum, recovery, schema migration v1.1) |
| Mana system | P0 | **DONE** | - | `Mana.cs` (regen, delay, events) |
| Experience/leveling | P0 | **DONE** | - | `Experience.cs` (XP curve, OnLevelUp) |
| HUD (HP, MP, XP bars) | P0 | **DONE** | - | `PlayerHUD.cs`, `HUDManager.cs`, `HUDBootstrap.cs` |
| Target HUD + nameplates | P0 | **DONE** | - | `TargetHUD.cs` (hover + hit), `WorldHealthBar.cs` |
| Floating damage numbers | P0 | **DONE** | - | `FloatingDamageNumber.cs`, `FloatingDamageSpawner.cs` |
| Particles/VFX | P1 | **DONE** | - | `VFXManager.cs`, `SimpleVFX.cs`, `ObjectPool.cs` |
| Status effects (burn/slow/stun/poison/freeze) | P1 | **DONE** | - | `StatusEffectManager.cs` + 5 effect classes |
| Pathfinding (A*) | P1 | **DONE** | - | `PathFinder.cs` (SortedList A*, integrated in ChaseState/AlertChaseState) |
| Explosion area damage | P1 | **DONE** | - | `ExplosionEffect.cs` (static Spawn, linear falloff, VFX) |
| Combat range debug | P1 | **DONE** | - | `CombatRangeVisualizer.cs` (F2 toggle) |
| Performance monitor | P1 | **DONE** | - | `PerformanceMonitor.cs` (F3 overlay, FPS/p95/p99/GC) |
| Entity culling | P1 | **DONE** | - | `EntityCulling.cs` (viewport frustum, offscreen throttle) |
| Death screen | P1 | **DONE** | - | `DeathScreenUI.cs` |
| Debug HUD | P1 | **DONE** | - | `DebugHUD.cs` (F1 toggle) |
| Data migration + validation | P1 | **DONE** | - | `PythonDataMigrator.cs` (report + dry-run) |
| Content validators | P1 | **DONE** | - | `ContentValidator.cs` (5 validators) |
| Build pipeline | P1 | **DONE** | - | `BuildValidator.cs` (pre-build hook) |
| Map transitions (portals) | P1 | **DONE** | - | `ZonePortal.cs`, `NPCZone.cs` |
| Buildings + collision | P1 | **DONE** | - | `BuildingLoader.cs`, `BuildingObject.cs`, `BuildingCollisionLoader.cs` (fine-grained grid) |
| Day/night cycle | P1 | **DONE** | - | `DayNightCycle.cs` (singleton, URP Global Light 2D via reflection) |
| Lighting 2D | P1 | **DONE** | - | `WorldLightLoader.cs`, `LightPresetDefinition.cs`, `LightPresetCatalog.cs` |
| Audio system | P2 | **DONE** | - | `AudioManager.cs` (crossfade, SFX pool, ducking, playlists) + `AudioCatalogSO.cs` + `CombatSfxConfigSO.cs` |
| Chat/Vendor system | P2 | **DONE** | - | `ChatSystem.cs`, `ChatBubble.cs`, `ChatUI.cs`, `VendorEconomyService.cs`, `VendorShopUI.cs` |
| Combo system | P2 | **DONE** | - | `ComboCounter.cs` + `ComboHUD.cs` |
| Inventory transfer UI (buy/sell) | P2 | **DONE** | - | `VendorShopUI.cs` (split-panel, CurrencyWallet) |
| Minimap | P2 | **DONE** | - | `MinimapManager.cs` + `MinimapDot.cs` (Texture2D 160×160) |
| Tiles editor | P3 | **DONE** | 30% | `TileEditorManager.cs` (F6 runtime, 9 layers, brush/eraser/fill/eyedropper, undo/redo) |
| Buildings editor | P3 | **DONE** | 25% | `BuildingsEditorWindow.cs` (`Valkur > Buildings Editor`) |
| Map editor | P3 | **DONE** | 15% | `MapEditorManager.cs` (F7 runtime, overlay paint/erase) |
| Entities debug editor | P3 | **DONE** | 35% | `DebugHUD.cs` (F1) + `DevConsole.cs` (backtick/F4, godmode/heal/tp/spawn) |
| Spells editor | P3 | **DONE** | 20% | `SpellsEditorWindow.cs` (`Valkur > Spells Editor`) |
| Particles editor | P3 | **DONE** | 20% | `ParticlesEditorWindow.cs` (`Valkur > Particles > Particles Editor`) |
| Console overlay | P3 | **DONE** | - | `DevConsole.cs` (IMGUI, backtick/F4) |

## Inventario de assets Python

| Tipo | Cantidad | Extensiones |
| ---- | -------- | ----------- |
| Sprites | 1326 | .png |
| Audio | 65 | .wav, .mp3, .ogg, .flac |
| Source art | 47 | .aseprite |
| Images misc | 9+9+3 | .gif, .jpg, .avif |
| Archives | 4 | .zip |
| Docs | 2 | .md, .docx |

## Criterios de aceptacion

La migracion se considera completa cuando:

1. **Todos los flujos P0 funcionan en Unity sin errores bloqueantes.**
   - Estado actual: **CUMPLIDO** — movimiento, combate, spells, IA/FSM, inventario, save/load, mana, XP, HUD.
2. **100% de assets .png y audio migrados con trazabilidad.**
   - Estado actual: PENDIENTE (Fase 2 diferida).
3. **Saves de Python son importables o migrables.**
   - Estado actual: **CUMPLIDO** — `SaveService.cs` con schema migration v1.0→v1.1, checksum, recovery.
4. **Tests automatizados cubren flujos P0.**
   - Estado actual: PARCIAL — `DataMigrationTests` (9/9), `ContentValidator` (5 validators), `BuildValidator`.
   - Pendiente: PlayMode smoke tests para flujos de gameplay.
5. **Build Windows x64 reproducible.**
   - Estado actual: **CUMPLIDO** — `BuildValidator.cs` + menu Valkur > Build.
6. **Rendimiento Unity >= baseline Python.**
   - Estado actual: PARCIAL — `PerformanceMonitor.cs` + `EntityCulling.cs` implementados.
   - Pendiente: medicion formal y comparacion con KPIs Python.

### Resumen de cumplimiento

| Criterio | Estado |
| -------- | ------ |
| Flujos P0 funcionales | CUMPLIDO |
| Assets migrados | PENDIENTE (Fase 2 diferida) |
| Saves migrables | CUMPLIDO |
| Tests automatizados | PARCIAL |
| Build reproducible | CUMPLIDO |
| Rendimiento >= baseline | PARCIAL |
