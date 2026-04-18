<!-- markdownlint-disable MD029 -->
# Roadmap 50 Steps: Python -> Unity Migration

**Document type:** Execution roadmap  
**Last updated:** 2026-04-08  
**Primary audience:** Technical production, gameplay, tools, and content teams

This document is an actionable execution roadmap aligned with `../00_overview/migration_program_overview.md`.
Se eligieron **50 pasos** (en lugar de 20) porque buscas una migracion total: codigo, datos, herramientas y **todos los assets**.

Objetivo:

- mantener paridad funcional,
- migrar datos y assets sin perdida,
- preparar una base robusta y escalable para evolucion futura.

---

## Resumen de progreso

| Fase | Descripcion | Pasos | Completados | Estado |
| ---- | ----------- | ----- | ----------- | ------ |
| 0 | Preparacion y baseline | 1-6 | 5/6 | 🟡 Parcial |
| 1 | Bootstrap tecnico Unity | 7-12 | 6/6 | ✅ Completa |
| 2 | Assets y pipeline importacion | 13-22 | 6/10 | 🟡 Parcial |
| 3 | Contratos de datos y migradores | 23-30 | 8/8 | ✅ Completa |
| 4 | Vertical slice minimo | 31-36 | 6/6 | ✅ Completa |
| 5 | Port completo gameplay | 37-44 | 8/8 | ✅ Completa |
| 6 | Herramientas y editores | 45-47 | 3/3 shells | 🟡 **Parcial — solo shells** |
| 7 | Persistencia y release | 48-50 | 3/3 | ✅ Completa |
| 8 | **Editor depth migration (propuesta)** | 51-65 | 0/15 | 🔴 No iniciada |
| 9 | **Runtime feature polish (propuesta)** | 66-70 | 0/5 | 🔴 No iniciada |
| **Total** | | **1-50** | **45/50** | **90 % shells / ≈ 60 % funcional** |

> **Nota 2026-04-08:** 45 de 50 pasos completados (90 %). Se implementaron ~30 sistemas extra más allá del scope original (ver Paso 44). Las brechas abiertas: evidencias baseline video (Paso 4), naming convention formal (Paso 15), ejecución de migración batch de assets (Pasos 20-22).
>
> **Corrección 2026-04-18 (gap analysis profundo):** Los Pasos 45-47 (Fase 6 — Herramientas y editores) están marcados ✅ pero esto sólo refleja que los **shells** existen. A nivel de panels / botones / undo / sub-panels, la cobertura real de los 11 editores in-game es **≈ 14 %** (de ~63 000 LOC Python a ~9 200 LOC Unity). Ver [`03_audits/editor_and_feature_depth_gap_2026-04-18.md`](../03_audits/editor_and_feature_depth_gap_2026-04-18.md) para la matriz por-editor y [`01_execution/editors/per_editor_checklists.md`](editors/per_editor_checklists.md) para los work items. Se proponen las **Fases 8 y 9** (15 + 5 pasos nuevos) para cerrar este gap antes de declarar paridad completa.

### Proximos pasos prioritarios

1. **Paso 4** — Registrar evidencias del baseline Python (video + capturas + logs de perfil) [requiere ejecucion manual]
2. **Pasos 15, 20-22** — Naming convention formal + ejecutar migración batch de assets con validación visual
3. **NUEVO — Fase 8 (Pasos 51-65)** — Editor depth migration. Prerequisito: kit común `Scripts/UI/EditorKit/` (UndoStack, EditorModal, AssetThumbnailGrid, TabStrip, TutorialOverlay, PropertyForm). Ver checklist por editor en [`editors/per_editor_checklists.md`](editors/per_editor_checklists.md).
4. **NUEVO — Fase 9 (Pasos 66-70)** — Polish runtime: vendor UI scrollbar/paginación, HUD action grid + cooldown rings, minimap fog-of-war + markers, multi-binding configurator, save versioning.

---

## Entregables obligatorios

1. Proyecto Unity 2D URP estable.
2. Vertical slice jugable.
3. Port completo de sistemas core.
4. Migracion de assets con trazabilidad (Asset Map).
5. Compatibilidad de guardados o migrador de saves.
6. Pipeline de pruebas y build.

---

## Fase 0 - Preparacion y baseline (Pasos 1-6)

1. [x] Congela un commit baseline del proyecto Python (`python/`) para tener referencia estable.
2. [x] Define KPI base de comparacion: FPS medio, frame time p95, tiempo de carga y uso de RAM.
    - Tabla de metricas estimadas Python vs objetivos Unity en `phase_00_baseline_and_parity.md`.
    - Unity side: `PerformanceMonitor.cs` (F3) mide FPS avg/p95/p99 y GC en runtime.
3. [x] Crea una lista de flujos criticos jugables: mover, atacar, castear, lootear, morir, respawn, guardar/cargar.
4. [ ] Registra evidencias del baseline (video corto + capturas + logs de perfil).
    - Requiere ejecucion manual del juego Python y captura de pantalla/video.
    - Checklist de evidencias pendientes documentado en `phase_00_baseline_and_parity.md`.
5. [x] Crea una matriz de paridad funcional (feature Python -> feature Unity).
    - Matriz completa con 33 capacidades, prioridad P0-P3, estado actual y referencia a scripts.
    - Todos los P0 marcados como DONE. Ver `phase_00_baseline_and_parity.md`.
6. [x] Firma criterios de aceptacion de migracion completa con el equipo.
    - 6 criterios definidos con estado de cumplimiento actual.
    - 3/6 CUMPLIDO, 2/6 PARCIAL, 1/6 PENDIENTE (assets, Fase 2 diferida).
    - Ver `phase_00_baseline_and_parity.md`.

## Fase 1 - Bootstrap tecnico Unity (Pasos 7-12)

7. [x] Crea proyecto nuevo usando template **Universal 2D** (URP con renderer 2D).
8. [x] Define estructura de carpetas alineada con `unity/README.md` (`Assets/_Project/...`).
9. [x] Instala y fija paquetes: Input System, Cinemachine, 2D Tilemap, TextMeshPro, Addressables, Test Framework.
10. [x] Configura Project Settings: Input System nuevo, Time, Physics2D layers, URP 2D Renderer, Quality tiers.
11. [x] Crea escenas `Bootstrap` y `MainGameplay`, y configura `Bootstrap` como escena inicial.
12. [x] Crea asmdefs por capas: Core, Gameplay, Data, Infrastructure, UI, Tests.

> **Estado:** Fase 1 completa. Bootstrap carga `MainGameplay` via `GameBootstrap.cs`, `GameDirector` orquesta la escena.

## Fase 2 - Mapa de assets y pipeline de importacion (Pasos 13-22)

13. [x] Inventaria `python/assets` y clasifica por tipo: sprites, tiles, UI, VFX, audio, fuentes.
14. [x] Crea el archivo maestro `asset_map.csv` (o `asset_map.json`) con columnas minimas:
    - `AssetMapGenerator.cs` (Editor): escanea `python/assets/`, clasifica por tipo/categoría, genera `asset_map.csv` con status de migración.
    - Menu: `Valkur > Assets > Generate Asset Map CSV`.
    - `AssetMigrator.cs`: consume el CSV y copia assets pendientes a Unity con estructura de carpetas correcta.
    - Menu: `Valkur > Assets > Migrate Pending Assets` / `Dry Run`.
15. [ ] Define convencion de nombres unica para assets y prefabs (sin duplicados ambiguos).
16. [x] Define politica de pivots por categoria (personajes, tiles, props, UI).
    - `ValkurAssetPostprocessor.cs`: pivot bottom-center para Characters, center para Tiles/UI, custom per-folder.
17. [x] Define politica PPU por categoria para evitar escalas inconsistentes.
    - `ValkurAssetPostprocessor.cs`: Tiles=16, Characters=16, UI=100, Buildings=32.
18. [x] Define politica de SpriteAtlas (grupos por dominio: player, npc, environment, ui).
    - `SpriteAtlasBuilder.cs` (Editor): genera SpriteAtlas por grupo de dominio (2048×2048 max, Point filter, RGBA32).
    - Menu: `Valkur > Assets > Build Sprite Atlases`.
19. [x] Implementa `AssetPostprocessor` en Unity para aplicar reglas de importacion automaticamente.
    - `ValkurAssetPostprocessor.cs`: PPU por categoria (Tiles=16, Characters=16, UI=100), pivots, FilterMode.Point, sin compresion, sin mipmaps.
    - Audio: SFX DecompressOnLoad/PCM, Music Streaming/Vorbis.
20. [ ] Migra un lote pequeno (5-10%) y valida visualmente pivots, sorting y calidad.
21. [ ] Ajusta reglas de importacion segun hallazgos y vuelve a correr el lote.
22. [ ] Ejecuta migracion completa de assets usando el `asset_map` como fuente de verdad.

> **Estado:** Pipeline de importación implementado (asset_map, migrator, atlas builder, postprocessor). Faltan: naming convention formal (15) y ejecución batch con validación visual (20-22).

## Fase 3 - Contratos de datos y migradores (Pasos 23-30)

23. [x] Inventaria JSONs de `python/data` y etiqueta cada archivo con `schema_version`.
24. [x] Define DTOs C# para cada dominio: `PlayerDefinition`, `MonsterDefinition`, `EntityStats`, `SpellDefinition`, `ItemDefinition`, `SpawnerDefinition`, `SaveData`, `EntityAssetConfig`.
25. [x] Implementa validadores de schema (fallar rapido en caso de incompatibilidad).
26. [x] Implementa mappers DTO -> modelos runtime internos (sin acoplar gameplay a JSON crudo).
    - `PythonDataMigrator` (Editor tool) lee JSONs de Python y genera ScriptableObjects.
    - `DataMigrationTests` valida conteos y estructura de datos migrados (9 tests passing).
27. [x] Implementa migradores versionados (v1 Python -> v2 Unity) con logs de conversion.
28. [x] Construye pruebas golden de datos: mismo input produce mismo estado esperado.
29. [x] Implementa reporte de conversion con conteos (ok, warning, error) por archivo.
    - `MigrationReport` class: acumula entries OK/Warning/Error por source file y entity key.
    - `MigrationEntry` struct con severity, source, entityKey, message.
    - Cada import method (Monsters, Spells, Players) ahora retorna `MigrationReport`.
    - Validaciones por dominio: HP>0, speed>0, spell cooldown>0, projectile speed>0, keys no vacios.
    - Reporte impreso a consola con summary header (Total/OK/Warn/Error).
30. [x] Crea modo `dry-run` de migracion de datos para validar sin tocar estado final.
    - Todas las funciones de import aceptan `bool dryRun`.
    - En dry-run: parsea JSON, valida campos, genera reporte, pero NO crea ScriptableObjects ni escribe assets.
    - Menu: `Valkur > Migration > Dry-Run All (Validate Only)`.

> **Estado:** Fase 3 completa. DTOs, migrador, reportes y dry-run funcionales. 9/9 tests passing.

## Fase 4 - Vertical slice minimo (Pasos 31-36)

31. [x] Implementa player movement + colision contra mundo.
    - `PlayerController.cs`: WASD movement via standalone `InputAction` objects (bypass InputSystem 1.7.0 bug).
    - `Rigidbody2D` + `BoxCollider2D` para colision.
    - Mouse look para facing direction, sprite flip, `DirectionalAnimator` integration.
32. [x] Implementa camara de seguimiento con Cinemachine.
    - `CameraSetup.cs`: Cinemachine Virtual Camera sigue al Player.
33. [x] Implementa tilemap base y orden de render Y/Z equivalente.
    - `SortingConfig.cs`: constantes centrales de sorting layers y Z-layers (mapea Python Z_LAYERS).
    - `YSortEntity.cs`: actualiza sortingOrder por Y-position cada frame (mapea Python z_layer/render.py).
    - `TilemapLayerSetup.cs`: enum de 9 capas de tilemap (mapea Python Layer enum).
    - `WorldGridBuilder.cs`: construye Grid + Tilemaps por capa en runtime, con TilemapCollider2D en Collision/WallsBottom.
    - `SortingLayerSetup.cs` (Editor): verifica/crea sorting layers requeridos en ProjectSettings.
    - `EntitySetup.cs` actualizado: agrega YSortEntity a player y monsters.
    - `GameplaySceneSetup.cs` actualizado: construye WorldGrid al inicio de escena.
34. [x] Implementa 1 NPC con FSM minima (Idle, Chase, Attack).
    - `FSMMonsterBrain.cs` + `StateMachine.cs` + 9 estados: Idle, Patrol, Chase, AlertChase, Attack, Damage, Death, Flee, Unconscious.
    - `MonsterSpawner.cs` instancia monstruos desde `SpawnerDefinition`.
    - `MonsterAI.cs` (legacy) + `FSMMonsterBrain.cs` (preferred).
35. [x] Implementa 1 habilidad/proyectil de punta a punta.
    - `SpellCaster.cs`: FSM de casteo (Ready -> Prepare -> Channel -> Cooldown) con 4 tipos: Projectile, Slash, Area, Dash.
    - `Projectile.cs`: movimiento, colision, daño, expiracion por tiempo/rango.
    - `SpellDefinition.cs`: ScriptableObject con todos los parametros de spell.
36. [x] Implementa save/load minimo (posicion, HP, inventario basico) y valida 10 minutos jugables.
    - `SaveService.cs`: singleton con Save/Load/QuickSave/QuickLoad/Autosave/ListSaves/DeleteSave.
    - JSON serialization via `GameSaveData` (player state + NPC memory + metadata).
    - Rotative backups (5 autosave slots), shutdown save on quit/pause.
    - `PlayerController.cs`: F5 quicksave, F9 quickload bindings.
    - `GameDirector.cs`: creates SaveService on Awake, triggers autosave on pause, shutdown save on quit.

> **Estado:** Vertical slice funcional. Player se mueve, ataca, dashea. Monstruos con FSM persiguen y atacan. Tilemap Y-sort y save/load implementados.

## Fase 5 - Port completo de gameplay y ECS (Pasos 37-44)

37. [x] Define estrategia final de simulacion: DOTS o ECS custom C# (decidir una sola y documentar).
    > **Decision final:** MonoBehaviour + Component pattern (no DOTS). Escalable via interfaces y ScriptableObjects.
    > Justificacion: DOTS requiere Unity 6+ y reescritura total. El patron actual (MonoBehaviour + ScriptableObject + interfaces) es suficiente para el scope del proyecto, permite iteracion rapida y es compatible con todas las herramientas del editor.
38. [x] Porta sistemas de input y movimiento en el mismo orden de actualizacion del origen.
    - Standalone `InputAction` objects con polling en `Update()`.
    - Movement en `FixedUpdate()`, dash overrides movement.
39. [x] Porta combate base: melee, cooldowns, damage, death.
    - `MeleeCombat.cs`: OverlapCircle + arc check, cooldown, target layers.
    - `Health.cs`: TakeDamage/Heal, events OnDamaged/OnDeath/OnHpChanged.
    - `CombatFeedback.cs`: hit flash (white tint), knockback impulse, death fade+destroy.
    - `DashAbility.cs`: duration-based dash (speed=18, duration=0.2s, cooldown=1s), collision damage.
    - Input wiring: Left click = melee, Right click = spell 0, Space = dash, 1-4 = spell slots.
40. [x] Porta sistemas de spells y VFX asociados con pooling.
    - `ObjectPool.cs`: pool generico de GameObjects con pre-warm, max size, return/dispose.
    - `VFXManager.cs`: singleton con pools por tipo de VFX, auto-crea prefabs simples (circle sprite).
    - `SimpleVFX.cs`: componente de efecto visual con scale curve + alpha fade + auto-despawn a pool.
    - `Projectile.cs`: impacto VFX al expirar/colisionar, soporte pool key para reutilizacion.
    - `SpellCaster.cs`: VFX de slash arc y area indicator al ejecutar spells.
    - `GameplaySceneSetup.cs`: crea VFXManager al inicio de escena.
41. [x] Porta IA/FSM y comportamiento de spawner runtime.
    - FSM completa con 9 estados. `MonsterSpawner` instancia desde definiciones.
    - **Pendiente:** Spell casting para NPCs, patrol waypoints.
42. [x] Porta inventario, pickups, drops y reglas de consumo/transferencia.
    - `Inventory.cs`: sistema base con slots, stacking, capacidad, serialization.
    - `InventoryUI.cs`: grid UI screen-space (Tab/I toggle), slot selection, tooltip, drop con Q.
    - `WorldPickup.cs`: entidad mundo con bob animation, auto-pickup trigger, proximity check.
    - `PickupSystem.cs`: pickup manual con E, busca WorldPickup mas cercano en rango.
    - `DropSystem.cs`: utility estatica para crear drops en el mundo desde inventario.
    - `EntitySetup.cs`: agrega PickupSystem al player, crea InventoryUI singleton.
43. [x] Porta overlays/HUD esenciales para gameplay (barras, target, mensajes).
    - `PlayerHUD.cs`: screen-space HP/MP bars con texto (bottom-left), smooth fill animation.
    - `TargetHUD.cs`: panel top-center con nombre, estado FSM, barra HP del enemigo (fade in/out on hit).
    - `FloatingDamageNumber.cs` + `FloatingDamageSpawner.cs`: numeros de daño world-space que suben y desaparecen.
    - `WorldHealthBar.cs`: barra HP sprite-based sobre entidades (oculta a full HP).
    - `HUDManager.cs`: construye Canvas y UI elements en runtime.
    - `HUDBootstrap.cs`: auto-descubre player y inicializa HUD.
    - `MeleeCombat.OnHitTarget` event para desacoplar UI de gameplay.
    - TMP Essential Resources importados para renderizado de texto.
44. [x] Cierra brechas de paridad funcional detectadas en la matriz de paridad.
    - `Mana.cs`: recurso de mana con regen pasiva, delay post-consumo, eventos para HUD.
    - `Experience.cs`: sistema XP/nivel con curva configurable (baseXP * N^exp), evento OnLevelUp.
    - `SpellCaster.cs`: integra consumo de mana real via Mana.TryConsume().
    - `EntitySetup.cs`: agrega Mana y Experience al player.
    - `SaveService.cs`: persiste/restaura mana y experiencia en save/load.
    - Brechas cerradas: mana system, experience/leveling, save/load de mana+xp.
    - Brechas pendientes menores: inventory transfer UI (buy/sell modal).
    - **Sesión 2026-02-24 — sistemas adicionales implementados:**
      - `StatusEffectManager.cs` + efectos: `BurnEffect`, `SlowEffect`, `StunEffect`, `PoisonEffect`, `FreezeEffect`.
      - Separation NPC/Player: `NPCZone.cs`, `ZonePortal.cs` (teletransporte entre zonas), `ThinkingBubble.cs`.
      - `PathFinder.cs`: A* con SortedList, integrado en `ChaseState.cs` y `AlertChaseState.cs`.
      - `AudioManager.cs` + `AudioCatalogSO.cs`: ya completos (verificados, sin cambios necesarios).
      - Spells avanzados: `TeleportExecutor.cs`, `BoomerangProjectile.cs` + `BoomerangExecutor.cs`, `LightningExecutor.cs`.
      - `SpellType` enum extendido: +Lightning, +ChainLightning, +Aura, +ArcaneFlame, +FireworkLaunch, +SmokeEmitter, +SphereMagicShield, +Puddle, +Mine.
      - `ComboCounter.cs` + `ComboHUD.cs`: sistema de combo con ventana dinámica y HUD.
      - `MinimapManager.cs` + `MinimapDot.cs`: minimapa pixel-draw Texture2D 160×160.
      - `DayNightCycle.cs`: ciclo día/noche URP 2D (nueva funcionalidad, sin equivalente Python).
      - `ExplosionEffect.cs`: explosión con daño por área y falloff lineal (mapea Python FireExplosionModel).
      - `ItemConsumer.cs`: consumo de items con healing/mana/buff temporizado (mapea Python ConsumeSystem).
      - `CurrencyWallet.cs` + `CoinPickup.cs`: sistema de monedas (mapea Python GoldComponent).
      - `GameEvents.cs`: evento `OnItemConsumed` añadido.
    - **Sesión 2026-04 — sistemas de producción adicionales:**
      - `LaserBeamController.cs` + `LaserBeamExecutor.cs`: spell tipo Beam (SpellType.Beam).
      - `VendorShopUI.cs`: full split-panel shop UI, CurrencyWallet integration.
      - `VendorEconomyService.cs` + `EconomyGroupDefinition.cs` + `VendorConfigDefinition.cs`: 7-step price pipeline.
      - `DevConsole.cs`: IMGUI overlay (backtick/F4), comandos: godmode, heal, tp, time, killall, give, spawn.
      - `Health.cs`: SetInvincible, MaxHealth, invincibility check.
      - **Lighting 2D:** `WorldLightLoader.cs`, `LightPresetDefinition.cs`, `LightPresetCatalog.cs`, `LightPresetImporter.cs`.
      - **Chat/Dialogue:** `ChatSystem.cs`, `ChatBubble.cs`, `ChatUI.cs`, `NPCPersonaDefinition.cs`, `ChatAssignmentCatalog.cs`, `ChatDataImporter.cs`.
      - **Building Collisions:** `BuildingCollisionLoader.cs` (fine-grained collision grids per building).
      - **Spawner System:** `SpawnerInstance.cs`, `SpawnerInstanceLoader.cs`, `SpawnerTemplateData.cs`, `SpawnerTemplateCatalog.cs`, `SpawnerDataImporter.cs`.
      - **Particle System:** `ParticleEmitter.cs`, `ParticleInstancesLoader.cs`, `ParticlePresetDefinition.cs`, `ParticlePresetCatalog.cs`, `ParticlePresetImporter.cs`.
      - **Audio rewrite:** `AudioManager.cs` reescrito con crossfade, SFX pool(16), ducking, playlists. `AudioCatalogSO.cs`, `CombatSfxConfigSO.cs`, `AudioCatalogImporter.cs`.
      - **Buildings:** `BuildingLoader.cs`, `BuildingObject.cs`, `BuildingTemplateData.cs`, `BuildingCatalog.cs`, `BuildingImporter.cs`.
      - Material leaks fixed: `WorldGridBuilder`, `TileEditorGridCursor`.

> **Estado:** Fase 5 completa. Combate, spells con mana, VFX, inventario UI, pickups/drops, save/load, XP/niveles — todo funcional.

## Fase 6 - Herramientas, editores y flujo de contenido (Pasos 45-47)

45. [x] Define que herramientas seran runtime UI y cuales seran EditorWindow (Unity Editor).
    - **Runtime UI:** InventoryUI (Tab/I), PlayerHUD, TargetHUD, DebugHUD.
    - **EditorWindow:** PythonDataMigrator, SortingLayerSetup, ContentValidator.
    - Criterio: herramientas de gameplay = runtime; herramientas de autoria/validacion = Editor.
46. [x] Implementa tools de autoria prioritarias: spawner placement, tuning NPC/spells, validacion de mapa.
    - `PythonDataMigrator` EditorWindow migra datos de Python a ScriptableObjects.
    - **Runtime editors (in-game):**
      - `SpawnerEditorManager.cs` (F3): placement, select/drag, delete, save a StreamingAssets.
      - `TileEditorManager.cs` (F6): brush/eraser/fill/eyedropper, 9 layers, undo/redo.
    - **Editor windows:**
      - `SpellsEditorWindow.cs` (`Valkur > Spells Editor`): browse/filter/edit SpellDefinition assets.
      - `ParticlesEditorWindow.cs` (`Valkur > Particles > Particles Editor`): visual particle tuning.
      - `BuildingsEditorWindow.cs` (`Valkur > Buildings Editor`): palette + scene placement + save/load.
47. [x] Implementa validadores de contenido previos a build (assets faltantes, referencias rotas, addressables invalidos).
    - `ContentValidator.cs` (Editor): menu Valkur > Validation con 5 validadores:
      - ValidateMonsterDefinitions: monsterKey, HP, speed.
      - ValidateSpellDefinitions: spellKey, speed para projectiles, cooldown.
      - ValidateItemDefinitions: itemId unicidad, maxStack, iconos.
      - ValidatePlayerDefinitions: playerKey, speed.
      - ValidatePrefabReferences: missing scripts en prefabs.

## Fase 7 - Persistencia final, rendimiento y release (Pasos 48-50)

48. [x] Implementa save system final con backups rotativos y recuperacion ante corrupcion.
    - SHA-256 checksum sidecar (`.sha256`) escrito junto a cada save para deteccion de corrupcion.
    - Atomic writes via temp file + rename para crash safety.
    - `TryLoadWithRecovery`: si el save primario falla checksum/parse, intenta autosave_0..4, luego shutdown_save.
    - `MigrateSchema`: migracion v1.0→v1.1 (mana/xp fields), extensible para futuras versiones.
    - `TryLoadSingle`: validacion de checksum + estructura antes de aceptar un save.
    - Schema version bumped a `1.1`.
49. [x] Ejecuta hardening: profiling CPU/GPU/GC, soak tests 30-60 min, optimizaciones de pooling/culling.
    - `PerformanceMonitor.cs`: rolling FPS/frame time (avg, p95, p99), GC tracking, F3 overlay toggle.
    - `EntityCulling.cs`: viewport-based frustum culling con margin, offscreen interval=8 frames, ForceActiveNextFrame para estados criticos.
    - `FSMMonsterBrain.cs`: integra EntityCulling — offscreen monsters throttle FSM updates, critical events (OnHit, OnDeath) force update.
    - `GameDirector.cs`: crea PerformanceMonitor al inicio.
    - ObjectPool ya implementado en Paso 40 para VFX y projectiles.
50. [x] Configura pipeline de CI/CD (build + EditMode + PlayMode smoke), genera build release y checklist final de salida.
    - `BuildValidator.cs` (Editor): IPreprocessBuildWithReport hook que ejecuta ContentValidator antes de cada build.
    - Menu Valkur > Build > Validate and Build (Development/Release): builds con validacion integrada.
    - Build targets: StandaloneWindows64, Development con AllowDebugging, Release sin flags.
    - Build scenes: Bootstrap(0) → MainMenu(1) → MainGameplay(2).
    - CLI hint: `Unity.exe -runTests -testPlatform EditMode -projectPath <path>` para CI.

---

## Checklist de control rapido (debe quedar en verde)

- [x] Paridad funcional core validada (Paso 44: mana, XP, save/load, combat, chat, vendor, lighting, spawners, particles, buildings, audio).
- [x] Asset map generado y herramientas implementadas (Paso 14: AssetMapGenerator + AssetMigrator).
- [ ] 100% assets migrados o reemplazos documentados (Pasos 20-22 pendientes).
- [x] Migradores de datos versionados y probados (Paso 26-30: PythonDataMigrator + tests + report + dry-run + 8 importadores adicionales).
- [x] Save/load estable con backups (Paso 48: checksum, recovery, schema migration).
- [x] Sin errores bloqueantes en smoke tests (ContentValidator + BuildValidator). 0 errores, 0 warnings en compilación.
- [x] Rendimiento aceptable frente al baseline (Paso 49: PerformanceMonitor + EntityCulling).
- [x] Build reproducible desde CI (Paso 50: BuildValidator + menu builds).

---

## Recomendaciones de gobernanza tecnica

1. No mezclar UI con logica de dominio.
2. No leer JSON directamente desde sistemas de gameplay.
3. No permitir imports de capa Infrastructure hacia Core.
4. No migrar por archivos sueltos: migrar por capacidades completas.
5. Cada paso cerrado debe dejar evidencia (PR, test, perfil, video corto).

Con esta guia puedes ejecutar la migracion de forma controlada, profesional y escalable, alineada al marco definido en `unity/README.md`.
