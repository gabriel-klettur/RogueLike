# Phase 00 Baseline and Functional Parity

**Document type:** Baseline evidence and parity matrix  
**Last updated:** 2026-02-23  
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
|---------|------------------------|----------------|
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
|---|-------|-------------|
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

| Capacidad Python | Prioridad | Estado Unity | Referencia |
|------------------|-----------|--------------|------------|
| Player movement + collision | P0 | **DONE** | `PlayerController.cs` (WASD + arrows, Rigidbody2D) |
| Camera follow | P0 | **DONE** | `CameraSetup.cs` (Cinemachine) |
| Tilemap render + sorting Y/Z | P0 | **DONE** | `WorldGridBuilder.cs`, `YSortEntity.cs`, `SortingConfig.cs` |
| Melee combat | P0 | **DONE** | `MeleeCombat.cs` (OverlapCircle + arc, cooldown, knockback) |
| Spell system (fireball, dash, etc) | P0 | **DONE** | `SpellCaster.cs` (4 types: Projectile/Slash/Area/Dash) |
| FSM/AI (Idle, Patrol, Aggro, Attack, Flee, Death) | P0 | **DONE** | `FSMMonsterBrain.cs` + `StateMachine.cs` (9 states) |
| Spawn system + budget | P0 | **DONE** | `MonsterSpawner.cs` (from SpawnerDefinition) |
| Inventory + pickup + drop | P0 | **DONE** | `Inventory.cs`, `InventoryUI.cs`, `WorldPickup.cs`, `PickupSystem.cs`, `DropSystem.cs` |
| Save/Load + autosave | P0 | **DONE** | `SaveService.cs` (checksum, recovery, schema migration v1.1) |
| Mana system | P0 | **DONE** | `Mana.cs` (regen, delay, events) |
| Experience/leveling | P0 | **DONE** | `Experience.cs` (XP curve, OnLevelUp) |
| HUD (HP, MP, XP bars) | P0 | **DONE** | `PlayerHUD.cs`, `HUDManager.cs`, `HUDBootstrap.cs` |
| Target HUD + nameplates | P0 | **DONE** | `TargetHUD.cs` (hover + hit), `WorldHealthBar.cs` |
| Floating damage numbers | P0 | **DONE** | `FloatingDamageNumber.cs`, `FloatingDamageSpawner.cs` |
| Particles/VFX | P1 | **DONE** | `VFXManager.cs`, `SimpleVFX.cs`, `ObjectPool.cs` |
| Combat range debug | P1 | **DONE** | `CombatRangeVisualizer.cs` (F2 toggle) |
| Performance monitor | P1 | **DONE** | `PerformanceMonitor.cs` (F3 overlay, FPS/p95/p99/GC) |
| Entity culling | P1 | **DONE** | `EntityCulling.cs` (viewport frustum, offscreen throttle) |
| Death screen | P1 | **DONE** | `DeathScreenUI.cs` |
| Debug HUD | P1 | **DONE** | `DebugHUD.cs` (F1 toggle) |
| Data migration + validation | P1 | **DONE** | `PythonDataMigrator.cs` (report + dry-run) |
| Content validators | P1 | **DONE** | `ContentValidator.cs` (5 validators) |
| Build pipeline | P1 | **DONE** | `BuildValidator.cs` (pre-build hook) |
| Map transitions (portals) | P1 | pendiente | — |
| Buildings + collision | P1 | pendiente | — |
| Day/night cycle | P1 | pendiente | — |
| Lighting 2D | P1 | pendiente | — |
| Audio system | P2 | pendiente | — |
| Chat/Vendor system | P2 | pendiente | — |
| Combo system | P2 | pendiente | — |
| Inventory transfer UI (buy/sell) | P2 | pendiente | — |
| Minimap | P2 | pendiente | — |
| Tiles editor | P3 | pendiente | — |
| Buildings editor | P3 | pendiente | — |
| Map editor | P3 | pendiente | — |
| Entities debug editor | P3 | pendiente | — |
| Spells editor | P3 | pendiente | — |
| Particles editor | P3 | pendiente | — |
| Console overlay | P3 | pendiente | — |

## Inventario de assets Python

| Tipo | Cantidad | Extensiones |
|------|----------|-------------|
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
|----------|--------|
| Flujos P0 funcionales | CUMPLIDO |
| Assets migrados | PENDIENTE (Fase 2 diferida) |
| Saves migrables | CUMPLIDO |
| Tests automatizados | PARCIAL |
| Build reproducible | CUMPLIDO |
| Rendimiento >= baseline | PARCIAL |
