# Professionalization Audit - Valkur Unity Project

**Document type:** Technical quality audit  
**Audit date:** 2026-02-22  
**Last updated:** 2026-02-23  
**Scope:** 48 C# scripts, 6 asmdefs, ProjectSettings, end-to-end architecture  
**Objective:** Identify disconnected systems, fragility points, and technical debt to improve robustness and scalability.  
**Note:** Point-in-time audit; some findings may already be resolved in later commits.

---

## Session Context Card

### Architecture Map
```
Bootstrap.unity
  └─ GameBootstrap (DontDestroyOnLoad)
       └─ LoadScene("MainMenu")
            └─ MainMenuUI → LoadScene("MainGameplay")

MainGameplay.unity
  ├─ GameDirector (singleton, orchestrator)
  │    └─ PerformanceMonitor
  ├─ GameplaySceneSetup (scene bootstrap)
  │    ├─ WorldGridBuilder → Grid + 9 Tilemaps
  │    ├─ EnsureGlobalLight2D (URP reflection)
  │    ├─ VFXManager (singleton, pooled VFX)
  │    ├─ TileEditorManager (F6 toggle)
  │    ├─ SpawnPlayer → EntitySetup.ConfigurePlayer()
  │    └─ SpawnTestMonsters → EntitySetup.ConfigureMonster()
  ├─ HUDBootstrap (polls for Player tag)
  │    ├─ HUDManager → PlayerHUD + TargetHUD
  │    ├─ DebugHUD (F1)
  │    └─ DeathScreenUI
  └─ SaveService (DontDestroyOnLoad, autosave)
```

### Layer Map (TagManager.asset)
| Layer Index | Name        |
|-------------|-------------|
| 8           | Player      |
| 9           | NPC         |
| 10          | Projectile  |
| 11          | World       |
| 12          | Pickup      |
| 13          | UIBlocker   |
| 14          | Building    |
| 15          | Spawner     |

### Tag Map
Player, NPC, Monster, Projectile, Pickup, Building, Spawner, Portal, Vendor

### Sorting Layers (TagManager.asset — 9 layers)
Default → Ground → GroundDecoration → Buildings → Entities → Projectiles → VFX → UI_World → Overlay

### SortingConfig.cs Constants (11 layer names)
Background, Ground, FloorDecals, ObjectsLow, WallsBottom, Entities, Decorations, WallsTop, ObjectsHigh, Overhead, UIWorld

### Input Bindings
| Action         | Binding                    | System      |
|----------------|----------------------------|-------------|
| Move           | WASD / Arrows              | InputSystem |
| Look           | Mouse position             | InputSystem |
| Primary Attack | Left Click (fireball)      | InputSystem |
| Secondary      | Right Click (melee slash)  | InputSystem |
| Dash           | RCtrl / RShift             | InputSystem |
| Spells 1-4     | 1/2/3/4                    | InputSystem |
| QuickSave/Load | F5 / F9                    | InputSystem |
| Inventory      | Tab / I                    | InputSystem |
| Drop Item      | Q                          | InputSystem |
| Debug HUD      | F1                         | **Legacy Input** |
| Combat Ranges  | F2                         | **Legacy Input** |
| Perf Monitor   | F3                         | **Legacy Input** |
| Tile Editor    | F6 + all editor keys       | **Legacy Input** |
| Death Screen   | W/S/Arrows/Enter           | **Legacy Input** |

### HUD Wiring
- **PlayerHUD** ← Health.OnHpChanged (event)
- **PlayerHUD.MP** ← NOT wired to Mana component (hardcoded 100/100)
- **TargetHUD** ← MeleeCombat.OnHitTarget + MouseTargetDetector.OnTargetChanged
- **DebugHUD** ← polls FindGameObjectWithTag every frame
- **DeathScreenUI** ← Health.OnDeath (event)

---

## HALLAZGOS DE AUDITORÍA

### 🔴 CRÍTICO (Bugs activos / datos corruptos en producción)

---

#### C1. `GameSaveData.metadata` usa `Dictionary<string,string>` — JsonUtility NO lo serializa

**Archivo:** `Scripts/Data/SaveData.cs:77`
```csharp
public Dictionary<string, string> metadata = new Dictionary<string, string>();
```

**Impacto:** `JsonUtility.ToJson()` ignora silenciosamente los `Dictionary<>`. Cualquier metadata guardada se pierde al serializar. Al deserializar, el campo queda como `null` (no como diccionario vacío), lo que puede causar `NullReferenceException` si algún código futuro lee `metadata`.

**Remediación:** Reemplazar con `List<SerializableKeyValue>` o usar un serializador que soporte diccionarios (Newtonsoft JSON).

---

#### C2. Sorting Layers en `SortingConfig.cs` NO existen en `TagManager.asset`

**Archivo:** `Scripts/Core/SortingConfig.cs:17-25` vs `ProjectSettings/TagManager.asset:48-75`

| SortingConfig.cs constant | ¿Existe en TagManager? |
|---------------------------|------------------------|
| `Background`              | ❌ NO                  |
| `Ground`                  | ✅ Sí                  |
| `FloorDecals`             | ❌ NO                  |
| `ObjectsLow`              | ❌ NO                  |
| `WallsBottom`             | ❌ NO                  |
| `Entities`                | ✅ Sí                  |
| `Decorations`             | ❌ NO                  |
| `WallsTop`                | ❌ NO                  |
| `ObjectsHigh`             | ❌ NO                  |
| `Overhead`                | ❌ NO                  |
| `UIWorld`                 | ❌ NO (existe `UI_World`) |

**Impacto:** 9 de 11 sorting layers referenciados en código no existen en Unity. Los TilemapRenderers que usan estos nombres caen silenciosamente al layer `Default`, causando Z-fighting y orden de renderizado incorrecto. `TilemapLayerSetup.ApplyLayerSettings()` asigna layers fantasma a 7 de 9 capas de tilemap.

**Remediación:** Sincronizar `TagManager.asset` con todos los sorting layers de `SortingConfig.cs`, o actualizar `SortingConfig.cs` para usar los nombres que ya existen en TagManager.

---

#### C3. `SaveService.CollectSaveData()` escribe `schemaVersion = "1.0"` pero `WriteSaveFile()` lo sobreescribe a `"1.1"`

**Archivo:** `Scripts/Gameplay/SaveService.cs:313` y `:456`

**Impacto:** No es un bug funcional (WriteSaveFile gana), pero es código confuso que indica falta de single source of truth para la versión del schema. Si alguien modifica CollectSaveData sin pasar por WriteSaveFile, se guardaría con versión incorrecta.

**Remediación:** Eliminar la asignación en `CollectSaveData()` y documentar que `WriteSaveFile()` es el único punto de asignación de versión.

---

### 🟠 ALTO (Fragilidad arquitectónica / deuda técnica significativa)

---

#### H1. Mezcla de Input Systems — Legacy `Input.GetKeyDown` + New `InputAction`

**6 archivos** usan `Input.GetKeyDown`/`Input.GetKey`/`Input.GetMouseButton` (legacy):
- `TileEditorManager.cs` (19 usos)
- `DeathScreenUI.cs` (3 usos)
- `MainMenuUI.cs` (3 usos)
- `PerformanceMonitor.cs` (1 uso)
- `CombatRangeVisualizer.cs` (1 uso)
- `DebugHUD.cs` (1 uso)

**4 archivos** usan `InputAction` (new Input System):
- `PlayerController.cs`
- `InventoryUI.cs`

**Impacto:** Si Project Settings tiene `Active Input Handling = Input System Package (New)`, las llamadas a `Input.GetKeyDown` **no funcionan**. Si está en `Both`, funciona pero con overhead duplicado y comportamiento inconsistente. Esto es una fuente de bugs silenciosos.

**Remediación:** Migrar todos los inputs a `InputAction` standalone o crear un `InputService` centralizado.

---

#### H2. Reflection masiva para wiring de componentes en runtime

**5 archivos** usan reflection (`GetField`/`SetValue`/`GetProperty`):

| Archivo | Uso | Riesgo |
|---------|-----|--------|
| `HUDManager.cs` | `SetPrivateField()` para wiring de UI references | Rompe si se renombra un campo |
| `WorldGridBuilder.cs` | Set `layer` y `collisionOnly` fields | Rompe si se renombra |
| `GameplaySceneSetup.cs` | Light2D via reflection (URP) | Rompe si cambia API de URP |
| `ZoneManager.cs` | `AudioManager.PlayMusic` via reflection | Rompe si cambia firma |

**Impacto:** Cualquier refactor de nombres de campos rompe silenciosamente el wiring sin error de compilación. Los bugs solo aparecen en runtime.

**Remediación:**
- `HUDManager`: Añadir métodos públicos `SetReferences()` en `PlayerHUD` y `TargetHUD` en lugar de reflection.
- `WorldGridBuilder`: Añadir método público `Configure(TilemapLayer, bool)` en `TilemapLayerSetup`.
- `GameplaySceneSetup`: Crear asmdef reference a URP o usar `Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")` como fallback check.
- `ZoneManager`: Definir interfaz `IAudioService` en Core y resolver via service locator.

---

#### H3. `FindObjectOfType` / `FindGameObjectWithTag` en Update loops (O(n) per frame)

**22 archivos** usan Find* calls. Los más críticos en hot paths:

| Archivo | Método | Frecuencia |
|---------|--------|------------|
| `DebugHUD.cs:183` | `FindObjectsOfType<Transform>()` | **Cada frame** (cuando visible) |
| `PerformanceMonitor.cs:168` | `FindObjectsOfType<Transform>()` | **Cada frame** (OnGUI) |
| `CombatRangeVisualizer.cs:96` | `FindObjectsOfType<FSMMonsterBrain>()` | **Cada frame** (LateUpdate) |
| `NPCInteractable.cs:33` | `FindGameObjectWithTag("Player")` | **Cada frame** (Update) |
| `ZoneManager.cs:48` | `FindGameObjectWithTag("Player")` | **Cada frame** (Update) |
| `DebugHUD.cs:161` | `FindGameObjectsWithTag("Monster")` | **Cada frame** |

**Impacto:** `FindObjectsOfType<Transform>()` es extremadamente costoso (recorre toda la jerarquía). En una escena con 500+ GameObjects, esto puede consumir 1-2ms por frame solo en búsquedas.

**Remediación:** Cache de referencias en `Start()` o usar un registry pattern (ej: `EntityRegistry.AllMonsters`).

---

#### H4. PlayerHUD.MP NO está conectado al componente `Mana`

**Archivo:** `Scripts/UI/HUD/PlayerHUD.cs:127` y `HUDBootstrap.cs`

`PlayerHUD` tiene `SetMana(int, int)` pero **nadie lo llama**. El `DebugHUD` muestra `MP: 100/100` hardcodeado (línea 127). El componente `Mana` existe en el player y tiene eventos `OnManaChanged`, pero no hay suscripción en ningún HUD.

**Impacto:** La barra de MP del HUD nunca se actualiza. El jugador no tiene feedback visual de su mana real.

**Remediación:** En `HUDBootstrap`, obtener `Mana` del player y suscribir `OnManaChanged` → `PlayerHUD.SetMana()`.

---

#### H5. `FloatingDamageSpawner` crea GameObjects sin pooling

**Archivo:** `Scripts/Gameplay/Combat/FloatingDamageSpawner.cs:46-50`
```csharp
var go = new GameObject($"DmgNum_{amount}");
go.AddComponent<FloatingDamageNumber>();
```

**Impacto:** Cada hit crea un nuevo GameObject que luego se destruye. En combate intenso (10+ mobs), esto genera GC pressure significativo. El proyecto ya tiene `ObjectPool` y `VFXManager` con pooling, pero los damage numbers no los usan.

**Remediación:** Usar `VFXManager` o un pool dedicado para floating damage numbers.

---

#### H6. `EntitySetup` es una God Class estática con responsabilidades mezcladas

**Archivo:** `Scripts/Gameplay/EntitySetup.cs` (293 líneas)

Responsabilidades actuales:
1. Configuración de player (layer, tag, sprite, health, combat, spells, dash, inventory, mana, XP, pickup, UI, facing indicator, combat visualizer, fireball prefab)
2. Configuración de monster (layer, tag, brain, combat, sprite, health, damage numbers, health bar, Y-sort)
3. Creación de fireball spell definition (ScriptableObject en runtime)
4. Creación de fireball projectile prefab (GameObject en runtime)
5. Creación de placeholder sprites (Texture2D en runtime)
6. Gestión de material unlit
7. Creación de singletons (InventoryUI, CombatRangeVisualizer)

**Impacto:** Cualquier cambio en cualquier sistema requiere modificar este archivo. Viola Single Responsibility Principle. Los prefabs y ScriptableObjects creados en runtime no son inspeccionables en el editor.

**Remediación:** Extraer a:
- `PlayerConfigurator` / `MonsterConfigurator`
- Prefabs reales en `Assets/_Project/Prefabs/`
- ScriptableObjects reales en `Assets/_Project/Data/`
- `PlaceholderAssetFactory` para sprites/materials temporales

---

#### H7. No hay interfaz ni Service Locator — todo es singleton con acoplamiento directo

**Singletons actuales (10):**
| Singleton | DontDestroyOnLoad? | Cleanup en OnDestroy? |
|-----------|--------------------|-----------------------|
| `GameDirector` | ❌ No | ❌ No (solo nulls Instance) |
| `SaveService` | ✅ Sí | ✅ Sí |
| `HUDManager` | ❌ No | ✅ Sí |
| `VFXManager` | ❌ No | ✅ Sí |
| `TileEditorManager` | ❌ No | ✅ Sí |
| `InventoryUI` | ❌ No | ✅ Sí |
| `CombatRangeVisualizer` | ❌ No | ✅ Sí |
| `PerformanceMonitor` | ❌ No | ✅ Sí |
| `DeathScreenUI` | ❌ No | ✅ Sí |
| `TileRegistry` | N/A (plain C#) | N/A |

**Impacto:** Sin `DontDestroyOnLoad`, los singletons se destruyen al cambiar de escena. Si `MainGameplay` se recarga (ej: restart desde DeathScreen), se crean duplicados antes de que el Awake guard los destruya, causando un frame de estado inconsistente. `GameDirector` no limpia `Instance` en `OnDestroy` si es destruido por scene unload.

**Remediación:** Implementar un `ServiceLocator` en Core, o al mínimo estandarizar el patrón singleton con un `SingletonMonoBehaviour<T>` base class.

---

### 🟡 MEDIO (Mejoras de calidad / mantenibilidad)

---

#### M1. `MonsterAI.cs` es código legacy duplicado — `FSMMonsterBrain` lo reemplaza

**Archivo:** `Scripts/Gameplay/MonsterAI.cs` (247 líneas)

El archivo se auto-desactiva si `FSMMonsterBrain` está presente (línea 54). Todo el código de `MonsterAI` está duplicado y mejorado en `FSMMonsterBrain` + estados FSM.

**Remediación:** Eliminar `MonsterAI.cs` y actualizar cualquier referencia residual.

---

#### M2. `DebugHUD` muestra MP hardcodeado y no usa el componente `Mana`

**Archivo:** `Scripts/UI/HUD/DebugHUD.cs:127`
```csharp
_sb.AppendLine($"MP:   100/100  (100%)");
```

**Remediación:** Obtener `Mana` del player y mostrar valores reales.

---

#### M3. `DashAbility.knockbackOnHit` tiene `#pragma warning disable CS0414`

**Archivo:** `Scripts/Gameplay/Combat/DashAbility.cs:19-21`

El campo está declarado pero nunca leído. El pragma suprime la advertencia en lugar de resolver el problema.

**Remediación:** Implementar el knockback on hit o eliminar el campo.

---

#### M4. `WorldHealthBar` usa sorting layer `"UI_World"` pero `SortingConfig` define `"UIWorld"`

**Archivo:** `Scripts/Gameplay/Combat/WorldHealthBar.cs:33`
```csharp
private const string SORTING_LAYER = "UI_World";
```
vs `SortingConfig.cs:25`:
```csharp
public const string LAYER_UI_WORLD = "UIWorld";
```

**Impacto:** Inconsistencia de nombres. `"UI_World"` SÍ existe en TagManager (es el nombre correcto), pero `SortingConfig.LAYER_UI_WORLD = "UIWorld"` NO coincide. Cualquier código que use la constante de SortingConfig para UI_World fallará.

**Remediación:** Alinear `SortingConfig.LAYER_UI_WORLD` con el nombre real en TagManager: `"UI_World"`.

---

#### M5. Múltiples Canvas creados en runtime sin EventSystem compartido

Cada sistema UI crea su propio Canvas:
- `HUDManager` → sortingOrder 100
- `InventoryUI` → sortingOrder 200
- `DebugHUD` → sortingOrder 200
- `DeathScreenUI` → sortingOrder 500
- `TileEditorUI` → sortingOrder 300

`DeathScreenUI` crea un `EventSystem` si no existe, pero los demás no lo verifican.

**Remediación:** Crear un `UIRoot` singleton que provea un Canvas compartido o al menos un EventSystem garantizado.

---

#### M6. `CombatFeedback.OnDeath` referencia `MonsterAI` directamente

**Archivo:** `Scripts/Gameplay/Combat/CombatFeedback.cs:96-97`
```csharp
var ai = GetComponent<MonsterAI>();
if (ai != null) ai.enabled = false;
```

**Impacto:** Acoplamiento directo a clase legacy. Si se elimina `MonsterAI`, este código falla silenciosamente (GetComponent retorna null, no crash, pero la intención se pierde).

**Remediación:** Usar una interfaz `IDisableOnDeath` o simplemente confiar en `FSMMonsterBrain` que ya maneja death via FSM events.

---

#### M7. `SaveService` no restaura items del inventario

**Archivo:** `Scripts/Gameplay/SaveService.cs:426-427`
```csharp
// Item restoration requires ItemDefinition lookup — deferred to catalog system
```

**Impacto:** Save/Load guarda la estructura del inventario pero NO restaura los items reales. Después de un load, el inventario aparece vacío.

**Remediación:** Implementar un `ItemCatalog` runtime que permita lookup por `itemId` → `ItemDefinition`, y usarlo en `ApplySaveData`.

---

### 🔵 BAJO (Polish / mejoras menores)

---

#### L1. `Mana.Update()` tiene variable `regenInt` no usada (línea 48)

#### L2. `GameBootstrap.InitializeCoreServices()` es un stub vacío con comentarios TODO

#### L3. `GameDirector` no tiene lógica de pausa real (solo `Time.timeScale`)

#### L4. No hay tests automatizados en el proyecto Unity (carpeta Tests/ vacía)

#### L5. `VFXManager.CreateSimpleVFXPrefab` crea texturas procedurales cada vez — debería cachear

---

## DEPENDENCY GRAPH (asmdefs)

```
Valkur.Core          (0 deps)
    ↑
Valkur.Data          (→ Core)
    ↑
Valkur.Infrastructure (→ Core, Data)
    ↑
Valkur.Gameplay      (→ Core, Data, InputSystem, TMP, Cinemachine)
    ↑
Valkur.UI            (→ Core, Gameplay, Data, TMP)
    ↑
Valkur.Editor        (→ Core, Gameplay, Data) [Editor only]
```

**Violaciones detectadas:**
1. ✅ No hay dependencias circulares — correcto.
2. ⚠️ `Valkur.UI` depende de `Valkur.Gameplay` — esto acopla UI a gameplay. Idealmente UI debería depender solo de interfaces/eventos en Core.
3. ⚠️ `ZoneManager` (Gameplay) necesita `AudioManager` (Infrastructure) pero no puede referenciar el asmdef → usa reflection. Esto indica que la capa de dependencias necesita un bus de eventos o service locator en Core.
4. ⚠️ `Valkur.Infrastructure` no tiene ningún script visible — el asmdef existe pero parece vacío o con archivos no rastreados.

---

## PLAN DE REMEDIACIÓN PRIORIZADO

### Sprint 1 — Bugs Críticos (1-2 días)
| # | Hallazgo | Acción |
|---|----------|--------|
| 1 | C2 | Sincronizar sorting layers en TagManager.asset con SortingConfig.cs |
| 2 | C1 | Reemplazar `Dictionary<string,string>` en SaveData con tipo serializable |
| 3 | M4 | Alinear `SortingConfig.LAYER_UI_WORLD` → `"UI_World"` |
| 4 | H4 | Conectar Mana → PlayerHUD.SetMana() en HUDBootstrap |
| 5 | M2 | Conectar Mana → DebugHUD display |

### Sprint 2 — Fragilidad Arquitectónica (3-5 días)
| # | Hallazgo | Acción |
|---|----------|--------|
| 6 | H1 | Migrar todos los inputs legacy a InputAction |
| 7 | H2 | Eliminar reflection: añadir métodos públicos de configuración |
| 8 | H7 | Crear `SingletonMonoBehaviour<T>` base class |
| 9 | H3 | Implementar EntityRegistry para eliminar Find* en hot paths |
| 10 | H5 | Poolear FloatingDamageNumbers via VFXManager |

### Sprint 3 — Limpieza y Escalabilidad (3-5 días)
| # | Hallazgo | Acción |
|---|----------|--------|
| 11 | H6 | Refactorizar EntitySetup en configuradores específicos + prefabs reales |
| 12 | M1 | Eliminar MonsterAI.cs legacy |
| 13 | M5 | Crear UIRoot singleton con EventSystem compartido |
| 14 | M7 | Implementar ItemCatalog para restauración de inventario en load |
| 15 | M6 | Limpiar CombatFeedback de referencias legacy |

### Sprint 4 — Polish y Testing (2-3 días)
| # | Hallazgo | Acción |
|---|----------|--------|
| 16 | L4 | Crear EditMode tests mínimos (combat, health, inventory, save/load) |
| 17 | L1-L5 | Limpiar dead code y stubs |
| 18 | — | Implementar service locator en Core para desacoplar capas |

---

## REGRESSION CHECKLIST (verificar después de cada sprint)

- [ ] `Image.Type.Filled` bars tienen sprite asignado
- [ ] Components añadidos post-`Initialize()` sincronizan estado inicial
- [ ] Prefab templates inactivos crean clones inactivos (activar explícitamente)
- [ ] World-space UI bars usan sorting layer/material correcto
- [ ] URP compatibility: no GL-only render paths
- [ ] Sorting layers en código coinciden con TagManager.asset
- [ ] Input funciona tanto con legacy como new Input System (o solo new)
- [ ] Save/Load preserva todos los datos del player (HP, MP, XP, inventory items)
- [ ] No hay NullReferenceException en console al iniciar escena
- [ ] HUD muestra valores reales de HP y MP
