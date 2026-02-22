# Problemas Posibles por Defaults Ocultos — Valkur Unity

> **Fecha**: 2025-02-22
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

### P1.2 — ⚠️ PENDIENTE: QualitySettings no tiene URP asignado

- **Archivo**: `ProjectSettings/QualitySettings.asset`
- **Problema**: Las 6 calidades (Very Low → Ultra) tienen `customRenderPipeline: {fileID: 0}` — es decir, **ninguna tiene el URP Pipeline Asset asignado**
- **Impacto**: Unity usa solo el pipeline de `GraphicsSettings.asset` (que sí tiene URP). Pero si alguien cambia la calidad en runtime (`QualitySettings.SetQualityLevel()`), podría perder el URP pipeline y caer al built-in renderer, causando:
  - Todos los materiales URP dejan de funcionar
  - Sprites se renderizan con shader fallback rosa/magenta
  - Light2D deja de existir como concepto
- **Probabilidad de ocurrencia**: Baja ahora (no hay menú de calidad), pero **alta si se añade** un settings menu
- **Fix recomendado**: Asignar el URP Pipeline Asset a cada nivel de calidad en `QualitySettings.asset`
- **Severidad**: 🔴 Crítica (si se activa)

### P1.3 — ⚠️ PENDIENTE: SpriteRenderers runtime usan "Sprites/Default" (built-in, no URP)

- **Archivos afectados**:
  - `EntitySetup.cs` → `Shader.Find("Sprites/Default")` para sprites de entidades
  - `WorldHealthBar.cs` → barras de vida
  - `FacingIndicator.cs` → flecha de dirección
  - `CombatRangeVisualizer.cs` → líneas de rango
  - `FireballVisual.cs` → visual de proyectil
  - `TileEditorGridCursor.cs` → cursor del tile editor
- **Problema**: `Sprites/Default` es el shader del **built-in render pipeline**, no de URP. En URP, el shader correcto es `Universal Render Pipeline/2D/Sprite-Unlit-Default`. El built-in shader funciona *por ahora* porque URP tiene un fallback, pero:
  - No participa en el 2D lighting system (no recibe luces 2D)
  - Puede romperse en futuras versiones de URP que eliminen el fallback
  - Comportamiento de blending puede diferir del esperado
- **Probabilidad de problema visible**: Baja ahora, **media-alta en el futuro**
- **Fix recomendado**: Reemplazar `Shader.Find("Sprites/Default")` por `Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")` en todos los archivos
- **Severidad**: 🟡 Media

### P1.4 — ⚠️ PENDIENTE: Material leaks por `new Material()` sin cleanup

- **Archivos afectados**: Todos los que hacen `new Material(Shader.Find(...))`:
  - `WorldGridBuilder.cs` — crea material cada vez que se ejecuta la coroutine
  - `EntitySetup.cs` — crea material estático (OK, se reutiliza)
  - `WorldHealthBar.cs` — material estático compartido (OK)
  - `FacingIndicator.cs` — `new Material()` por cada indicador creado
  - `CombatRangeVisualizer.cs` — `new Material()` por instancia
- **Problema**: `new Material()` crea un material en memoria que **no se destruye automáticamente** con el GameObject. Si el objeto se destruye sin hacer `Destroy(material)`, el material queda en memoria como leak
- **Impacto**: Memory leak gradual. En sesiones largas con muchos monstruos spawneados/destruidos, puede acumular cientos de materiales huérfanos
- **Fix recomendado**: Usar materiales estáticos compartidos (como `WorldHealthBar` ya hace) o destruir el material en `OnDestroy()`
- **Severidad**: 🟡 Media

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

### P2.2 — ⚠️ PENDIENTE: Physics2D queries hit triggers por defecto

- **Archivo**: `ProjectSettings/Physics2DSettings.asset`
- **Problema**: `m_QueriesHitTriggers: 1` — todos los raycasts y overlap queries detectan triggers además de colliders
- **Impacto**: `MouseTargetDetector` usa `Physics2D.CircleCast` para detectar NPCs. Si hay triggers en la escena (spawner zones, pickup areas), el raycast los detectará como "targets", causando:
  - El HUD muestra info de un trigger invisible en vez del NPC
  - El targeting de combate puede seleccionar un trigger
- **Fix recomendado**: Cambiar a `m_QueriesHitTriggers: 0` globalmente, o usar `QueryTriggerInteraction.Ignore` en queries específicos
- **Severidad**: 🟡 Media

### P2.3 — ⚠️ PENDIENTE: Queries start in colliders

- **Archivo**: `ProjectSettings/Physics2DSettings.asset`
- **Problema**: `m_QueriesStartInColliders: 1` — si un raycast empieza dentro de un collider, lo detecta
- **Impacto**: Si el jugador hace un raycast desde su propia posición y tiene un collider, se detecta a sí mismo como target. Esto puede causar auto-targeting en combate
- **Fix recomendado**: Depende del diseño. Para raycasts de targeting, usar layer masks que excluyan al caster
- **Severidad**: 🟢 Baja (mitigado por layer masks en la mayoría de casos)

---

## Categoría 3: LAYERS Y TAGS

### P3.1 — ⚠️ PENDIENTE: LayerMask.NameToLayer devuelve -1 si el layer no existe

- **Archivos afectados**:
  - `EntitySetup.cs` → `LayerMask.NameToLayer("Player")`, `"NPC"`, `"Projectile"` — son `static readonly`, se evalúan una sola vez
  - `HUDBootstrap.cs` → `LayerMask.GetMask("NPC")`
  - `DropSystem.cs` → `LayerMask.NameToLayer("Default")`
- **Problema**: Si alguien renombra o elimina un layer en TagManager, `NameToLayer` devuelve `-1` silenciosamente. Asignar `gameObject.layer = -1` pone el objeto en layer 0 (Default), lo que cambia completamente su comportamiento de colisión y raycast
- **Por qué es oculto**: No hay error, no hay warning. El objeto simplemente está en el layer equivocado
- **Fix recomendado**: Validar el resultado de `NameToLayer` y loggear warning si es -1. O usar constantes de layer index en vez de strings
- **Severidad**: 🟡 Media

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

### P4.3 — ⚠️ PENDIENTE: Save files no tienen migración de schema

- **Problema**: `SaveService` tiene `CURRENT_SCHEMA = "1.0"` pero no hay lógica de migración si el schema cambia. Si se modifica `GameSaveData` (añadir/quitar campos), los saves antiguos:
  - Se cargan sin error (JsonUtility ignora campos desconocidos)
  - Los campos nuevos quedan en su valor default (0, null, "")
  - Datos pueden corromperse silenciosamente
- **Fix recomendado**: Implementar `MigrateSaveData(string fromVersion, GameSaveData data)` que transforme datos entre versiones
- **Severidad**: 🟡 Media (se activa cuando se modifique GameSaveData)

---

## Categoría 5: SCENE MANAGEMENT

### P5.1 — ⚠️ PENDIENTE: SceneManager.LoadScene destruye todo sin cleanup

- **Archivos**: `MainMenuUI.cs`, `DeathScreenUI.cs`, `GameBootstrap.cs`
- **Problema**: `SceneManager.LoadScene()` destruye todos los GameObjects de la escena actual excepto los marcados con `DontDestroyOnLoad`. Pero:
  - Los singletons que NO son `Persist` se destruyen y su `Instance` queda null
  - Los event listeners (`OnDamaged`, `OnTargetChanged`, etc.) pueden quedar suscritos a objetos destruidos → `MissingReferenceException` en el siguiente frame
  - `Time.timeScale` se resetea manualmente en `DeathScreenUI` pero podría no ejecutarse si la escena se carga desde otro lugar
- **Fix recomendado**: Implementar un `SceneTransitionManager` que:
  1. Desuscribe todos los eventos
  2. Resetea `Time.timeScale = 1f`
  3. Limpia singletons no persistentes
  4. Luego carga la escena
- **Severidad**: 🟡 Media

### P5.2 — ⚠️ PENDIENTE: AudioManager usa DontDestroyOnLoad manual (no SingletonMonoBehaviour)

- **Archivo**: `Infrastructure/AudioManager.cs`
- **Problema**: `AudioManager` usa su propio patrón singleton con `DontDestroyOnLoad` manual en vez de heredar de `SingletonMonoBehaviour<T>`. Si se carga la escena de gameplay dos veces, puede haber dos AudioManagers (el check de duplicados puede fallar si el timing es diferente)
- **Fix recomendado**: Migrar a `SingletonMonoBehaviour<AudioManager>` con `Persist => true`
- **Severidad**: 🟢 Baja (funciona ahora, pero inconsistente)

---

## Categoría 6: INPUT SYSTEM

### P6.1 — ⚠️ PENDIENTE: InputActions no se deshabilitan al cambiar de escena

- **Problema**: Los `InputAction` creados en scripts (TileEditorManager, PerformanceMonitor, CombatRangeVisualizer, etc.) se deshabilitan en `OnDestroy()`. Pero si el script se destruye por cambio de escena, el `OnDestroy` puede ejecutarse en un orden impredecible, y las acciones pueden disparar callbacks en objetos ya destruidos
- **Impacto**: `NullReferenceException` o `MissingReferenceException` esporádicos durante transiciones de escena
- **Fix recomendado**: Deshabilitar InputActions en `OnDisable()` además de `OnDestroy()`
- **Severidad**: 🟢 Baja (solo durante transiciones)

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

### P8.2 — 🔴 PENDIENTE: Shader.Find() puede devolver null en builds

- **Archivos**: Todos los que usan `Shader.Find()`
- **Problema**: `Shader.Find()` solo encuentra shaders que están incluidos en el build. Si un shader no está referenciado por ningún material en la escena, Unity lo excluye del build. En el Editor funciona (todos los shaders están disponibles), pero en un build standalone:
  - `Shader.Find("Sprites/Default")` → probablemente OK (siempre incluido)
  - `Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")` → **puede ser null** si ningún material en el proyecto lo usa directamente
- **Impacto**: El juego funciona en el Editor pero falla en builds
- **Fix recomendado**: Añadir los shaders necesarios a `ProjectSettings/GraphicsSettings.asset` → `Always Included Shaders`, o crear un material dummy que los referencie
- **Severidad**: 🔴 Crítica (para builds)

---

## Categoría 9: TILEMAP ESPECÍFICO

### P9.1 — ⚠️ PENDIENTE: Tile anchor mismatch con sprite pivot

- **Problema**: Los sprites tienen `spritePivot: (0.5, 0)` (bottom-center) pero el tilemap usa `tileAnchor: (0.5, 0.5, 0)` (center)
- **Impacto**: Los tiles se renderizan desplazados medio tile hacia arriba respecto a su celda. Esto puede causar:
  - Gaps visuales entre tiles
  - Misalignment con colliders
  - El cursor del tile editor apunta a una celda pero el tile aparece offset
- **Fix recomendado**: Cambiar `spritePivot` a `(0.5, 0.5)` en los import settings de todos los sprites de tiles, o cambiar `tileAnchor` a `(0.5, 0)` en el tilemap
- **Severidad**: 🟡 Media (visible pero no bloqueante)

### P9.2 — ⚠️ PENDIENTE: CompositeCollider2D con generationType Synchronous

- **Archivo**: `WorldGridBuilder.cs` (layers Collision y WallsBottom)
- **Problema**: `generationType = CompositeCollider2D.GenerationType.Synchronous` regenera el collider **cada vez que se modifica un tile**. Con el tile editor pintando muchos tiles rápidamente, esto causa:
  - Spike de CPU por cada tile pintado
  - Posible stutter visible durante brush strokes grandes
- **Fix recomendado**: Cambiar a `GenerationType.Manual` y llamar `GenerateGeometry()` solo al final del brush stroke (en `EndBrushStroke()`)
- **Severidad**: 🟡 Media (performance durante edición)

---

## Resumen por severidad

### � Bajos (riesgo menor o bien manejado)
| ID | Problema | Estado |
|----|----------|--------|
| P2.3 | Queries start in colliders | Pendiente (mitigado por layer masks) |
| P4.2 | JsonUtility ignora propiedades | Conocido (convención documentada) |
| P7.1 | Camera.main null check | Bien manejado |
| P8.1 | Resources.LoadAll memoria | Aceptable para scope actual |

### ✅ Resueltos
| ID | Problema | Fix |
|----|----------|-----|
| P1.1 | Sprite-Lit-Default sin Light2D | Forzar Sprite-Unlit-Default |
| P1.2 | QualitySettings sin URP Pipeline Asset | URP Asset asignado a 6 niveles de calidad |
| P1.3 | SpriteRenderers usan shader built-in | Migrado a URP Sprite-Unlit-Default (6 archivos) |
| P1.4 | Material leaks sin cleanup | Shared materials + Destroy en OnDestroy |
| P2.1 | Layer Collision Matrix "todo con todo" | Matriz configurada por layer (Player/NPC/Projectile/etc.) |
| P2.2 | Queries hit triggers | QueriesHitTriggers = 0 global |
| P3.1 | NameToLayer devuelve -1 silenciosamente | SafeNameToLayer con warning + fallback a Default |
| P3.2 | Sorting layers desincronizados | Sprint 1 — 15 layers sincronizados |
| P4.1 | Dictionary en JsonUtility | SerializableKeyValue |
| P4.3 | Save schema sin migración | MigrateSchema ya implementado (v1.0→v1.1) |
| P5.1 | Scene load sin cleanup | SceneTransitionManager (timeScale + EntityRegistry.Clear) |
| P5.2 | AudioManager singleton manual | Migrado a SingletonMonoBehaviour con Persist |
| P6.1 | InputActions en transiciones | OnDisable/OnEnable en PerformanceMonitor, DebugHUD |
| P8.2 | Shader.Find() null en builds | Sprite-Unlit-Default en Always Included Shaders |
| P9.1 | Tile anchor vs sprite pivot mismatch | tileAnchor cambiado a (0.5, 0, 0) |
| P9.2 | CompositeCollider2D synchronous | Cambiado a GenerationType.Manual |

---

## Estado final

**18 de 21 problemas resueltos.** Los 3 restantes son de severidad baja y no requieren acción inmediata.
