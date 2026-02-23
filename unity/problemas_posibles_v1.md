# Problemas Posibles por Defaults Ocultos — Valkur Unity

> **Fecha**: 2025-02-22
> **Última auditoría de estado**: 2026-02-23
> **Contexto**: Tras resolver el bug de tiles negros (causado por un default de URP que nadie configuró explícitamente), se realizó una auditoría exhaustiva del proyecto para identificar **todos los defaults ocultos de Unity que podrían causar problemas silenciosos**.
>
> Estos son problemas que **no generan errores de compilación ni warnings**, pero que causan comportamiento incorrecto en runtime.

---

## Categoría 1: RENDERING / URP

### P1.1 — ✅ RESUELTO: Sprite-Lit-Default requiere Light2D Global

- **Síntoma**: Tiles pintados aparecen como rectángulos negros
- **Causa**: URP asigna `Sprite-Lit-Default` por defecto a todos los `SpriteRenderer` y `TilemapRenderer`. Este shader multiplica el color del sprite por la contribución de luces 2D. Sin una `Light2D` de tipo Global → multiplicación por 0 → negro puro
- **Por qué es oculto**: No hay error, no hay warning. El componente se crea, el tile se coloca, todo "funciona" — excepto que se ve negro
- **Fix aplicado**: Forzar `Sprite-Unlit-Default` en `WorldGridBuilder.ApplyUnlitFallbackIfNeeded()`
- **Documentación**: `unity/Tile_editor_v1.md`

### P1.2 — ✅ RESUELTO: QualitySettings sí tiene URP asignado

- **Archivo verificado**: `ProjectSettings/QualitySettings.asset`
- **Estado actual**: Las calidades Very Low → Ultra tienen `customRenderPipeline` apuntando al asset URP (`guid: 681886c5eb7344803b6206f758bf0b1c`)
- **Resultado**: Cambiar calidad ya no implica perder URP
- **Severidad actual**: ✅ Cerrado

### P1.3 — ✅ RESUELTO (con fallback controlado): SpriteRenderers runtime priorizan URP

- **Archivos afectados**:
  - `EntitySpriteHelper.cs`
  - `WorldHealthBar.cs` → barras de vida
  - `FacingIndicator.cs` → flecha de dirección
  - `CombatRangeVisualizer.cs` → líneas de rango
  - `FireballVisual.cs` → visual de proyectil
  - `TileEditorGridCursor.cs` → cursor del tile editor
- **Estado actual**: Todos usan patrón `Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default")`
- **Resultado**: URP es la ruta principal; `Sprites/Default` queda como fallback defensivo
- **Severidad actual**: ✅ Cerrado (riesgo residual bajo por fallback intencional)

### P1.4 — ⚠️ PENDIENTE: Material leaks por `new Material()` sin cleanup

- **Archivos afectados (estado actual)**:
  - `WorldGridBuilder.cs` — crea `new Material(unlitShader)` sin cleanup explícito
  - `TileEditorGridCursor.cs` — crea material para `LineRenderer` sin cleanup explícito
  - `FacingIndicator.cs` — crea material y sí hace `Destroy` en `OnDestroy()`
  - `CombatRangeVisualizer.cs` — crea material y sí hace `DestroyImmediate` en `OnDestroy()`
  - `WorldHealthBar.cs` y `FireballVisual.cs` — material estático compartido
- **Problema**: `new Material()` crea un material en memoria que **no se destruye automáticamente** con el GameObject. Si el objeto se destruye sin hacer `Destroy(material)`, el material queda en memoria como leak
- **Impacto**: Memory leak gradual. En sesiones largas con muchos monstruos spawneados/destruidos, puede acumular cientos de materiales huérfanos
- **Fix recomendado**: Completar cleanup en `WorldGridBuilder` y `TileEditorGridCursor`
- **Severidad**: 🟡 Media (aún vigente)

---

## Categoría 2: PHYSICS 2D

### P2.1 — 🔴 ACTIVO: Layer Collision Matrix es "todo colisiona con todo"

- **Archivo**: `ProjectSettings/Physics2DSettings.asset`
- **Problema**: `m_LayerCollisionMatrix` es todo `f` (0xFFFFFFFF para cada layer) — **cada physics layer colisiona con cada otro layer**
- **Layers definidos**: Player (8), NPC (9), Projectile (10), World (11), Pickup (12), UIBlocker (13), Building (14), Spawner (15)
- **Impacto real AHORA**:
  - **Projectiles del jugador colisionan con el jugador** → el fireball puede golpear al caster
  - **NPCs colisionan con Pickups** → los drops en el suelo pueden empujar a los NPCs o bloquear pathfinding
  - **Spawners colisionan con todo** → los spawner triggers pueden interferir con movimiento
  - **UIBlocker colisiona con entidades** → si se usa un collider para bloquear clicks, también bloquea movimiento físico
  - **Player colisiona con Player** → si hay clones (bugs de DontDestroyOnLoad), se empujan mutuamente
- **Por qué es oculto**: No hay error. Las colisiones ocurren silenciosamente. Los síntomas son sutiles: "el monstruo se mueve raro", "el fireball no llega lejos", "el drop desaparece"
- **Fix recomendado**: Configurar la Layer Collision Matrix correctamente:
  ```
  Player     ↔ NPC, World, Building, Pickup (NO Projectile propio)
  NPC        ↔ Player, NPC, World, Building, Projectile (NO Pickup, NO Spawner)
  Projectile ↔ NPC, World, Building (NO Player, NO Projectile)
  Pickup     ↔ Player (NO NPC, NO World)
  World      ↔ Player, NPC, Projectile
  Building   ↔ Player, NPC, Projectile
  Spawner    ↔ (nada — solo trigger detection)
  UIBlocker  ↔ (nada — solo raycast)
  ```
- **Severidad**: 🔴 Crítica — afecta gameplay activamente

### P2.2 — ✅ RESUELTO: Physics2D queries NO hit triggers globalmente

- **Archivo verificado**: `ProjectSettings/Physics2DSettings.asset`
- **Estado actual**: `m_QueriesHitTriggers: 0`
- **Resultado**: Se eliminó el riesgo global de seleccionar triggers por default en queries
- **Severidad actual**: ✅ Cerrado

### P2.3 — ⚠️ PENDIENTE: Queries start in colliders

- **Archivo**: `ProjectSettings/Physics2DSettings.asset`
- **Problema**: `m_QueriesStartInColliders: 1` — si un raycast empieza dentro de un collider, lo detecta
- **Impacto**: Si el jugador hace un raycast desde su propia posición y tiene un collider, se detecta a sí mismo como target. Esto puede causar auto-targeting en combate
- **Fix recomendado**: Depende del diseño. Para raycasts de targeting, usar layer masks que excluyan al caster
- **Severidad**: 🟢 Baja (mitigado por layer masks en la mayoría de casos)

---

## Categoría 3: LAYERS Y TAGS

### P3.1 — ⚠️ PARCIAL: validación aplicada en puntos críticos, no en todo el proyecto

- **Archivos afectados**:
  - `EntitySetup.cs` → `LayerMask.NameToLayer("Player")`, `"NPC"`, `"Projectile"` — son `static readonly`, se evalúan una sola vez
  - `HUDBootstrap.cs` → `LayerMask.GetMask("NPC")`
  - `DropSystem.cs` → `LayerMask.NameToLayer("Default")`
- **Problema**: Si alguien renombra o elimina un layer en TagManager, `NameToLayer` devuelve `-1` silenciosamente. Asignar `gameObject.layer = -1` pone el objeto en layer 0 (Default), lo que cambia completamente su comportamiento de colisión y raycast
- **Por qué es oculto**: No hay error, no hay warning. El objeto simplemente está en el layer equivocado
- **Estado actual**:
  - ✅ `EntitySetup` usa `SafeNameToLayer(...)` con warning + fallback a Default
  - ✅ `ProjectilePrefabFactory` usa fallback seguro para `Projectile`
  - ⚠️ Siguen usos directos de `NameToLayer` en `HUDBootstrap` y `DropSystem`
- **Severidad**: 🟡 Media (mitigado, no cerrado)

### P3.2 — ✅ RESUELTO: Sorting layers desincronizados (Sprint 1)

- **Problema original**: `SortingConfig.cs` definía 11 sorting layers pero `TagManager.asset` solo tenía 9, con nombres diferentes
- **Fix aplicado**: Sprint 1 sincronizó ambos a 15 sorting layers idénticos

---

## Categoría 4: SERIALIZACIÓN / SAVE SYSTEM

### P4.1 — ✅ RESUELTO: JsonUtility no serializa Dictionary

- **Problema original**: `GameSaveData` tenía `Dictionary<string,string> metadata` que JsonUtility ignoraba silenciosamente
- **Fix aplicado**: Reemplazado por `List<SerializableKeyValue>` en `SaveData.cs`

### P4.2 — ⚠️ PENDIENTE: JsonUtility ignora propiedades (solo serializa campos)

- **Impacto general**: Si alguien añade una propiedad (`public int Foo { get; set; }`) a una clase `[Serializable]`, JsonUtility la ignora sin warning. Solo serializa campos (`public int foo;`)
- **Archivos en riesgo**: Cualquier clase en `Data/` que se serialice con JsonUtility
- **Fix recomendado**: Documentar la convención: "solo campos públicos o `[SerializeField]` en clases de datos". Considerar migrar a Newtonsoft.Json para datos complejos
- **Severidad**: 🟢 Baja (convención conocida)

### P4.3 — ✅ RESUELTO: Save files sí tienen migración de schema

- **Estado actual**:
  - `SaveSchemaMigrator.CURRENT_SCHEMA = "1.1"`
  - `SaveSchemaMigrator.Migrate(...)` implementado (incluye ruta 1.0 -> 1.1)
  - `SaveService.Load(...)` llama migración antes de restaurar estado
- **Severidad actual**: ✅ Cerrado

---

## Categoría 5: SCENE MANAGEMENT

### P5.1 — ✅ RESUELTO: transición de escenas centralizada con cleanup

- **Estado actual**:
  - Existe `SceneTransitionManager.LoadScene(sceneName)`
  - Resetea `Time.timeScale = 1f`
  - Limpia `EntityRegistry` y `GameEvents`
  - `MainMenuUI`, `DeathScreenUI` y `GameBootstrap` cargan escenas a través de `SceneTransitionManager`
- **Severidad actual**: ✅ Cerrado

### P5.2 — ✅ RESUELTO: AudioManager usa SingletonMonoBehaviour con Persist

- **Archivo verificado**: `Infrastructure/AudioManager.cs`
- **Estado actual**: Hereda de `SingletonMonoBehaviour<AudioManager>` y define `Persist => true`
- **Severidad actual**: ✅ Cerrado

---

## Categoría 6: INPUT SYSTEM

### P6.1 — ⚠️ PARCIAL: mejoró en varios módulos, aún no homogéneo

- **Problema**: Aún hay scripts con desactivación/dispose concentrado en `OnDestroy()` sin `OnDisable()` consistente
- **Impacto**: `NullReferenceException` o `MissingReferenceException` esporádicos durante transiciones de escena
- **Estado actual**:
  - ✅ `DebugHUD`, `InventoryUI`, `PickupSystem`, `PlayerController`, `SaveLoadInputHandler` tienen flujo seguro de disable/dispose
  - ⚠️ `CombatRangeVisualizer`, `MainMenuUI`, `DeathScreenUI`, `TileEditorManager` no aplican patrón homogéneo de `OnDisable()` + `OnDestroy()`
- **Severidad**: 🟢 Baja (riesgo de transición, no bloqueante)

---

## Categoría 7: CAMERA

### P7.1 — ⚠️ PENDIENTE: Camera.main null si cámara se crea tarde

- **Archivos**: `PlayerController.cs`, `TileEditorManager.cs`, `MouseTargetDetector.cs`, `EntityCulling.cs`, `MainMenuUI.cs`
- **Problema**: En Unity 2022.3 LTS, `Camera.main` ya está cacheada internamente, así que el impacto de performance es mínimo. Sin embargo, si no hay cámara con tag "MainCamera", devuelve `null` silenciosamente
- **Estado actual**: La mayoría de archivos cachean `Camera.main` en Start/Awake (correcto). `MouseTargetDetector` y `EntityCulling` tienen fallback con null check (correcto)
- **Riesgo real**: Si la cámara se crea después de que los scripts hagan Start(), `_mainCamera` queda null hasta el siguiente check
- **Severidad**: 🟢 Baja (bien manejado actualmente)

---

## Categoría 8: MEMORY / PERFORMANCE

### P8.1 — ⚠️ PENDIENTE: Resources.LoadAll carga TODO en memoria

- **Archivo**: `TileCatalog.BuildFromResources()`
- **Problema**: `Resources.LoadAll<Sprite>("Tiles")` carga las ~312 sprites de tiles en memoria de una vez. Esto es ~10MB de texturas que permanecen en memoria mientras el catálogo exista
- **Impacto**: Aceptable para PC, pero problemático para mobile o WebGL
- **Fix futuro**: Migrar a Addressables para carga bajo demanda por categoría
- **Severidad**: 🟢 Baja (aceptable para scope actual)

### P8.2 — ✅ RESUELTO: shader crítico URP incluido para builds

- **Archivos verificados**:
  - `ProjectSettings/GraphicsSettings.asset`
  - `Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/Shaders/2D/Sprite-Unlit-Default.shader.meta`
- **Estado actual**: `Always Included Shaders` incluye `guid: 13c02b14c4d048fa9653293d54f6e0e1`, que corresponde a `Sprite-Unlit-Default`
- **Resultado**: Se mitigó el riesgo principal de `Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")` devolviendo null en build
- **Severidad actual**: ✅ Cerrado

---

## Categoría 9: TILEMAP ESPECÍFICO

### P9.1 — ✅ RESUELTO: tile anchor alineado con pivot de sprites

- **Estado actual**: `WorldGridBuilder` define `tilemap.tileAnchor = new Vector3(0.5f, 0f, 0f)`
- **Resultado**: Alineación corregida en la construcción runtime del grid
- **Severidad actual**: ✅ Cerrado

### P9.2 — ✅ RESUELTO: CompositeCollider2D usa generationType Manual

- **Archivo verificado**: `WorldGridBuilder.cs` (layers Collision y WallsBottom)
- **Estado actual**: ambos colliders usan `CompositeCollider2D.GenerationType.Manual`
- **Resultado**: Se evita regeneración síncrona automática por cada edición
- **Severidad actual**: ✅ Cerrado

---

## Resumen por severidad

### 🔴/🟡 Activos (requieren acción)
| ID | Problema | Estado actual |
|----|----------|---------------|
| P1.4 | Material leaks por `new Material()` sin cleanup total | Pendiente |
| P2.1 | Layer Collision Matrix "todo con todo" | **Activo** |
| P3.1 | `NameToLayer` sin validación en todos los puntos | Parcial |
| P6.1 | Patrón `InputAction` no homogéneo en transiciones | Parcial |

### 🟢 Pendientes mitigados / aceptados por ahora
| ID | Problema | Estado actual |
|----|----------|---------------|
| P2.3 | Queries start in colliders | Pendiente (mitigado por masks) |
| P4.2 | JsonUtility ignora propiedades | Pendiente (convención conocida) |
| P7.1 | `Camera.main` puede ser null temporalmente | Bien manejado (riesgo bajo) |
| P8.1 | `Resources.LoadAll` carga masiva en TileCatalog | Aceptable para scope actual |

### ✅ Resueltos verificados
| ID | Problema | Estado actual |
|----|----------|---------------|
| P1.1 | Sprite-Lit-Default sin Light2D | Resuelto |
| P1.2 | URP por calidad | Resuelto |
| P1.3 | Shader runtime built-in como principal | Resuelto (URP primero + fallback) |
| P2.2 | Queries hit triggers | Resuelto (`m_QueriesHitTriggers: 0`) |
| P3.2 | Sorting layers desincronizados | Resuelto |
| P4.1 | Dictionary en JsonUtility | Resuelto |
| P4.3 | Migración de schema de save | Resuelto |
| P5.1 | Carga de escena sin cleanup centralizado | Resuelto |
| P5.2 | AudioManager singleton manual | Resuelto |
| P8.2 | Riesgo `Shader.Find()` para Sprite-Unlit en build | Resuelto (shader incluido) |
| P9.1 | Tile anchor vs sprite pivot mismatch | Resuelto |
| P9.2 | CompositeCollider2D synchronous | Resuelto |

---

## Estado final

**12 de 20 problemas resueltos, 8 pendientes (4 activos + 4 mitigados).**

### Prioridad sugerida para próximas revisiones (solo documental)
1. **P2.1** (crítico): matriz de colisión por capas
2. **P1.4** (media): cleanup de materiales runtime
3. **P3.1** (media): eliminar usos directos restantes de `NameToLayer`
4. **P6.1** (baja): homogeneizar lifecycle de `InputAction`
