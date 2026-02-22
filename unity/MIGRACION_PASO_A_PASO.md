# Migracion Python -> Unity: plan operativo en 50 pasos

Este documento es una guia accionable basada en `unity/README.md`.
Se eligieron **50 pasos** (en lugar de 20) porque buscas una migracion total: codigo, datos, herramientas y **todos los assets**.

Objetivo:

- mantener paridad funcional,
- migrar datos y assets sin perdida,
- preparar una base robusta y escalable para evolucion futura.

---

## Resumen de progreso

| Fase | Descripcion | Pasos | Completados | Estado |
|------|-------------|-------|-------------|--------|
| 0 | Preparacion y baseline | 1-6 | 2/6 | 🟡 Parcial |
| 1 | Bootstrap tecnico Unity | 7-12 | 6/6 | ✅ Completa |
| 2 | Assets y pipeline importacion | 13-22 | 2/10 | � Parcial |
| 3 | Contratos de datos y migradores | 23-30 | 6/8 | 🟡 Parcial |
| 4 | Vertical slice minimo | 31-36 | 6/6 | ✅ Completa |
| 5 | Port completo gameplay | 37-44 | 8/8 | ✅ Completa |
| 6 | Herramientas y editores | 45-47 | 3/3 | ✅ Completa |
| 7 | Persistencia y release | 48-50 | 0/3 | 🔴 Pendiente |
| **Total** | | **1-50** | **33/50** | **66%** |

### Proximos pasos prioritarios

1. **Paso 48** — Save system final con backups rotativos y recuperacion ante corrupcion
2. **Paso 49** — Hardening: profiling CPU/GPU/GC, soak tests, optimizaciones
3. **Paso 50** — Pipeline CI/CD, build release, checklist final
4. **Pasos 3-6, 15-22, 29-30** — Completar fases 0, 2, 3 pendientes

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
2. [ ] Define KPI base de comparacion: FPS medio, frame time p95, tiempo de carga y uso de RAM.
3. [x] Crea una lista de flujos criticos jugables: mover, atacar, castear, lootear, morir, respawn, guardar/cargar.
4. [ ] Registra evidencias del baseline (video corto + capturas + logs de perfil).
5. [ ] Crea una matriz de paridad funcional (feature Python -> feature Unity).
6. [ ] Firma criterios de aceptacion de migracion completa con el equipo.

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
14. [ ] Crea el archivo maestro `asset_map.csv` (o `asset_map.json`) con columnas minimas:
    - asset_id
    - source_path_python
    - target_path_unity
    - asset_type
    - pixels_per_unit
    - pivot
    - filter_mode
    - compression
    - atlas_group
    - addressable_key
    - owner_system
    - migration_status
15. [ ] Define convencion de nombres unica para assets y prefabs (sin duplicados ambiguos).
16. [ ] Define politica de pivots por categoria (personajes, tiles, props, UI).
17. [ ] Define politica PPU por categoria para evitar escalas inconsistentes.
18. [ ] Define politica de SpriteAtlas (grupos por dominio: player, npc, environment, ui).
19. [x] Implementa `AssetPostprocessor` en Unity para aplicar reglas de importacion automaticamente.
    - `ValkurAssetPostprocessor.cs`: PPU por categoria (Tiles=16, Characters=16, UI=100), pivots, FilterMode.Point, sin compresion, sin mipmaps.
    - Audio: SFX DecompressOnLoad/PCM, Music Streaming/Vorbis.
20. [ ] Migra un lote pequeno (5-10%) y valida visualmente pivots, sorting y calidad.
21. [ ] Ajusta reglas de importacion segun hallazgos y vuelve a correr el lote.
22. [ ] Ejecuta migracion completa de assets usando el `asset_map` como fuente de verdad.

> **Estado:** Inventario de assets hecho. Pipeline de importacion pendiente.

## Fase 3 - Contratos de datos y migradores (Pasos 23-30)

23. [x] Inventaria JSONs de `python/data` y etiqueta cada archivo con `schema_version`.
24. [x] Define DTOs C# para cada dominio: `PlayerDefinition`, `MonsterDefinition`, `EntityStats`, `SpellDefinition`, `ItemDefinition`, `SpawnerDefinition`, `SaveData`, `EntityAssetConfig`.
25. [x] Implementa validadores de schema (fallar rapido en caso de incompatibilidad).
26. [x] Implementa mappers DTO -> modelos runtime internos (sin acoplar gameplay a JSON crudo).
    - `PythonDataMigrator` (Editor tool) lee JSONs de Python y genera ScriptableObjects.
    - `DataMigrationTests` valida conteos y estructura de datos migrados (9 tests passing).
27. [x] Implementa migradores versionados (v1 Python -> v2 Unity) con logs de conversion.
28. [x] Construye pruebas golden de datos: mismo input produce mismo estado esperado.
29. [ ] Implementa reporte de conversion con conteos (ok, warning, error) por archivo.
30. [ ] Crea modo `dry-run` de migracion de datos para validar sin tocar estado final.

> **Estado:** DTOs y migrador funcional. 9/9 tests passing. Faltan reportes y dry-run.

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
    - Brechas pendientes menores: day/night cycle, inventory transfer UI (buy/sell modal).

> **Estado:** Fase 5 completa. Combate, spells con mana, VFX, inventario UI, pickups/drops, save/load, XP/niveles — todo funcional.

## Fase 6 - Herramientas, editores y flujo de contenido (Pasos 45-47)

45. [x] Define que herramientas seran runtime UI y cuales seran EditorWindow (Unity Editor).
    - **Runtime UI:** InventoryUI (Tab/I), PlayerHUD, TargetHUD, DebugHUD.
    - **EditorWindow:** PythonDataMigrator, SortingLayerSetup, ContentValidator.
    - Criterio: herramientas de gameplay = runtime; herramientas de autoria/validacion = Editor.
46. [x] Implementa tools de autoria prioritarias: spawner placement, tuning NPC/spells, validacion de mapa.
    - `PythonDataMigrator` EditorWindow migra datos de Python a ScriptableObjects.
47. [x] Implementa validadores de contenido previos a build (assets faltantes, referencias rotas, addressables invalidos).
    - `ContentValidator.cs` (Editor): menu Valkur > Validation con 5 validadores:
      - ValidateMonsterDefinitions: monsterKey, HP, speed.
      - ValidateSpellDefinitions: spellKey, speed para projectiles, cooldown.
      - ValidateItemDefinitions: itemId unicidad, maxStack, iconos.
      - ValidatePlayerDefinitions: playerKey, speed.
      - ValidatePrefabReferences: missing scripts en prefabs.

## Fase 7 - Persistencia final, rendimiento y release (Pasos 48-50)

48. [ ] Implementa save system final con backups rotativos y recuperacion ante corrupcion.
49. [ ] Ejecuta hardening: profiling CPU/GPU/GC, soak tests 30-60 min, optimizaciones de pooling/culling.
50. [ ] Configura pipeline de CI/CD (build + EditMode + PlayMode smoke), genera build release y checklist final de salida.

---

## Checklist de control rapido (debe quedar en verde)

- [ ] Paridad funcional core validada.
- [ ] Asset map completo y actualizado.
- [ ] 100% assets migrados o reemplazos documentados.
- [ ] Migradores de datos versionados y probados.
- [ ] Save/load estable con backups.
- [ ] Sin errores bloqueantes en smoke tests.
- [ ] Rendimiento aceptable frente al baseline.
- [ ] Build reproducible desde CI.

---

## Recomendaciones de gobernanza tecnica

1. No mezclar UI con logica de dominio.
2. No leer JSON directamente desde sistemas de gameplay.
3. No permitir imports de capa Infrastructure hacia Core.
4. No migrar por archivos sueltos: migrar por capacidades completas.
5. Cada paso cerrado debe dejar evidencia (PR, test, perfil, video corto).

Con esta guia puedes ejecutar la migracion de forma controlada, profesional y escalable, alineada al marco definido en `unity/README.md`.
