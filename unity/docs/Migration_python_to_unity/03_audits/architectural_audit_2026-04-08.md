# Valkur Architectural Audit Report

**Document type:** Full architecture audit  
**Audit date:** 2026-04-08  
**Baseline comparison:** `architectural_audit_2026-02-22.md` + `professionalization_audit_2026-02-22.md`  
**Scope:** Folder structure, assemblies, code patterns, performance, tests, project settings  
**Compile errors at audit time:** 0  

---

## 0. Métricas de Línea Base

| Métrica | Feb 2026 | Abr 2026 | Δ |
| --------- | ---------- | ---------- | --- |
| Total archivos `.cs` | 90 | **294** | +204 |
| Assemblies (`.asmdef`) | 5 | **6** (+Editor) | +1 |
| Singletons (`static Instance`) | 9 | **4** | -5 ✅ |
| Archivos >400 LOC (sin partials) | 4 | **~5** | ≈ |
| God Classes (>3 responsabilidades) | 5 | **3** | -2 ✅ |
| `FindObjectOfType` en hot paths | 3 | **2** | -1 ✅ |
| ServiceLocator registrations | 0 | **2** (Audio, VFX) | +2 ✅ |
| Tests EditMode | ~0 | **18 archivos / ~135 casos** | +135 ✅ |
| Tests PlayMode | 0 | **0** | — ❌ |
| Errores de compilación | 0 | **0** | ✅ |

---

## 1. Comparación con Auditoría Previa (Feb 2026)

### Issues Críticos Previos — Estado Actual

| ID | Issue (Feb 2026) | Estado Abr 2026 | Notas |
| ---- | ------------------ | ------------------ | ------- |
| C1 | `GameSaveData.metadata` usa `Dictionary` → no serializa con JsonUtility | ✅ **RESUELTO** | Reemplazado con `SerializableKeyValue[]` |
| C2 | Sorting Layers: 9 de 11 capas en código no existían en TagManager | ✅ **RESUELTO** | TagManager sincronizado: 14 sorting layers, todos alineados con `SortingConfig.cs` |
| C3 | Schema version duplicado en `SaveService` | ✅ **RESUELTO** | `SaveSchemaMigrator.CURRENT_SCHEMA` es fuente única de verdad |

### Issues de Alta Severidad Previos — Estado Actual

| ID | Issue (Feb 2026) | Estado Abr 2026 |
| ---- | ------------------ | ------------------ |
| H1 | Legacy `Input.GetKeyDown` mezclado con InputSystem (19 usos en TileEditor) | ⚠️ **PARCIAL** — Reducido a ~2 usos en Chat (ChatUI, ChatSystem). TileEditor migrado. |
| H2 | Reflection (5 archivos) | ⚠️ **PARCIAL** — DayNightCycle y Light2D reflection persiste, pero cacheado en static. |
| H3 | `FindObjectOfType` en hot paths (DebugHUD, CombatRangeVisualizer, NPCInteractable) | ✅ **RESUELTO** — Los 3 casos originales corregidos. Nuevo: `NPCSeparationSystem.FixedUpdate`. |
| H4 | `PlayerHUD.Mana` no conectado a componente Mana | ❌ **SIN RESOLVER** |
| H5 | `FloatingDamageNumber` sin pooling | ❌ **SIN RESOLVER** |
| H6 | `EntitySetup` God Class (300 líneas) | ⚠️ **PARCIAL** — Refactorizado (Bootstrap/, AnimationBinder), pero sigue siendo factory centralizado. |
| H7 | 10 Singletons con acoplamiento | ✅ **MEJORADO** — De 9 a 4 singletons; ServiceLocator activo para Audio/VFX. |

---

## 2. Auditoría Estructural (Carpetas y Organización)

### 2.1 Estructura de Carpetas — Calificación: ✅ A

```text
Assets/
├── _Project/              ✅ Carpeta raíz del proyecto — limpia
│   ├── Art/               ✅ Sprites organizados por dominio (Buildings, Characters, Items, NPC, Tiles, UI, VFX)
│   ├── Audio/             ✅ Music/ y SFX/ separados
│   ├── Data/              ✅ Catalogs/, LightPresets/, RuntimeJson/
│   ├── Prefabs/           ✅ Prefabs de juego
│   ├── Resources/         ✅ Uso mínimo (AudioCatalog, 2 placeholders, Tiles, Buildings, UI)
│   ├── Scripts/           ✅ 6 assemblies bien separados
│   │   ├── Core/          ✅ Bootstrap, ServiceLocator, singletons, eventos, pooling
│   │   ├── Data/          ✅ ScriptableObjects, DTOs, catalogs
│   │   ├── Editor/        ✅ Importadores, editores, validators (platform: Editor)
│   │   ├── Gameplay/      ✅ Sub-carpetas por dominio (Combat, Spells, World, etc.)
│   │   ├── Infrastructure/✅ AudioManager (3 partials)
│   │   └── UI/            ✅ HUD, MainMenu, PauseMenu, Loading, DeathScreen
│   └── Settings/          ✅ URP renderer assets
├── Scenes/                ✅ Ubicación estándar Unity (1 escena)
├── Screenshots/           ⚠️ Debería estar en _Project/ o fuera del build
├── Settings/              ✅ Auto-generado por Unity
├── StreamingAssets/       ✅ Datos JSON runtime (Maps, Particles, Lights, etc.)
├── Tests/                 ✅ EditMode y PlayMode con .asmdef propios
└── TextMesh Pro/          ✅ Auto-generado
```

**Hallazgos:**

| Sev. | Hallazgo | Impacto | Acción |
| ------ | ---------- | --------- | -------- |
| 🟡 **BAJO** | `Screenshots/` en Assets root en vez de `_Project/` | Se incluirá en builds | Mover a `_Project/Screenshots/` o excluir del build |
| ✅ | `Resources/` tiene uso mínimo (3 assets + carpetas) | No infla el build | Correcto |
| ✅ | Sin archivos sueltos en `_Project/` root | Limpio | — |
| ✅ | Sin carpetas vacías detectadas | Organizado | — |

### 2.2 Assemblies (.asmdef) — Calificación: ✅ A+

```text
Valkur.Core ←── Unity.InputSystem
    ↓
Valkur.Data ←── Valkur.Core
    ↓
Valkur.Infrastructure ←── Valkur.Core, Valkur.Data
    ↓
Valkur.Gameplay ←── Valkur.Core, Valkur.Data, Valkur.Infrastructure,
                     Unity.InputSystem, Unity.TextMeshPro, Cinemachine, PixelPerfect
    ↓
Valkur.UI ←── Valkur.Core, Valkur.Gameplay, Valkur.Data, Valkur.Infrastructure,
               Unity.TextMeshPro, Unity.InputSystem
    ↓
Valkur.Editor ←── Valkur.Core, Valkur.Gameplay, Valkur.Data
                   [includePlatforms: Editor]
```

| Verificación | Estado |
| ------------- | -------- |
| Dependencias circulares | ✅ Ninguna |
| Violaciones de capas | ✅ Ninguna |
| `Valkur.Editor` con platform filter `Editor` | ✅ Correcto |
| Tests con `.asmdef` propios | ✅ `Valkur.Tests.EditMode` + `Valkur.Tests.PlayMode` |
| Tests con `defineConstraints: UNITY_INCLUDE_TESTS` | ✅ Correcto |
| `allowUnsafeCode: false` en todos | ✅ Correcto |
| Namespaces root definidos | ✅ Correcto (`Valkur.Core`, `Valkur.Data`, etc.) |

### 2.3 Convenciones de Nombrado — Calificación: ⚠️ B

**Estadísticas:**

| Categoría | Cantidad |
| ----------- | ---------- |
| Archivos PascalCase + nombre descriptivo | 227 (77%) |
| Partial classes (`.NombreParte.cs`) | 67 (23%) |
| Sufijos genéricos (`Helpers`, `Internals`, `Processing`) | 18 |
| Conflictos de nombre | 1 (`TileEditorUI.Builder.cs` vs `TileEditorUIBuilder.cs`) |
| Archivos en carpeta incorrecta | 3 (`VendorShopUI.*.cs` en `Enemies/`) |
| Conflicto con API Unity | 1 (`ParticleEmitter.ParticleSystem.cs`) |

**Hallazgos:**

| Sev. | Hallazgo | Archivos | Acción |
| ------ | ---------- | ---------- | -------- |
| 🟠 **MEDIO** | `VendorShopUI.cs`, `.Builder.cs`, `.Rows.cs` están en `Gameplay/Enemies/` en vez de `Gameplay/Vendors/` | 3 | Mover a `Vendors/` |
| 🟠 **MEDIO** | Conflicto de nombres: `TileEditorUI.Builder.cs` (partial de TileEditorUI) coexiste con `TileEditorUIBuilder.cs` (clase separada) | 2 | Unificar o renombrar |
| 🟡 **BAJO** | 18 archivos con sufijos genéricos (`Helpers`, `Internals`, `Processing`, `Rendering`) | 18 | Renombrar a algo descriptivo |
| 🟡 **BAJO** | `ParticleEmitter.ParticleSystem.cs` colisiona con nombre de API Unity | 1 | Renombrar a `ParticleEmitter.SystemSetup.cs` |
| ✅ | Partial classes bien usados (separación lógica coherente) | 67 | Patrón aceptable |

---

## 3. Auditoría de Código y Patrones

### 3.1 ServiceLocator vs Singletons — Calificación: ⚠️ B-

**Estado actual del patrón de inyección de dependencias:**

| Componente | Patrón Usado | Ideal |
| ------------ | ------------- | ------- |
| `AudioManager` | ✅ `ServiceLocator.Register<IAudioService>` | Correcto |
| `VFXManager` | ✅ `ServiceLocator.Register<IVFXService>` | Correcto |
| `GameDirector` | ❌ `public static GameDirector Instance` | Migrar a ServiceLocator |
| `GameSettings` | ❌ `private static GameSettings _instance` (no MonoBehaviour) | Migrar a ServiceLocator |
| `PauseMenuUI` | ❌ `public static PauseMenuUI Instance` + DontDestroyOnLoad | Migrar a ServiceLocator |
| `TileRegistry` | ❌ `private static TileRegistry _instance` (lazy, no MonoBehaviour) | Aceptable (pure data) |

**Suscripción de ServiceLocator (18 llamadas):**

- `Register<T>`: 2 (Audio, VFX)
- `Get<T>` / `TryGet<T>`: 16 (CombatAudioSystem, SpellCaster, PauseMenuUI, MainMenuUI, GameplaySceneSetup, etc.)

**Hallazgos:**

| Sev. | Hallazgo | Impacto | Acción |
| ------ | ---------- | --------- | -------- |
| 🟠 **MEDIO** | `GameDirector`, `GameSettings`, `PauseMenuUI` bypasean ServiceLocator con static Instance | Patrón inconsistente; difícil de testear | Registrar en ServiceLocator; mantener Instance como wrapper de conveniencia |
| 🟡 **BAJO** | Sin política centralizada de `DontDestroyOnLoad` — 5 sitios diferentes lo llaman manualmente | Fragmentado | Consolidar en `GameBootstrap` o `SingletonMonoBehaviour.Persist` |

### 3.2 God Classes — Calificación: ⚠️ B-

**Top 5 clases más grandes (agregando partials):**

| Rank | Clase | LOC Estimadas | Partials | Responsabilidades | Verdict |
| ------ | ------- | --------------- | ---------- | ------------------- | --------- |
| 1 | `MainMenuUI` | ~1700 | 8 | Menú principal, carrusel, selector de clase, opciones, sonido, inputs, carga, audio init | 🔴 **GOD CLASS** |
| 2 | `PauseMenuUI` | ~1400 | 7 | Menú pausa, opciones, sonido, inputs, carga, canvas setup, singleton | 🔴 **GOD CLASS** |
| 3 | `GameplaySceneSetup` | ~700 | 4 | Grid, zona, luz, VFX, audio, chat, vendor, spawner, player, monsters | 🟠 **ORQUESTADOR LARGO** |
| 4 | `MainMenuUI.Options` | 680 | 1 | Panel opciones con 3 sub-paneles, 8 rows sonido, 4 tabs inputs | 🟡 **LARGO** |
| 5 | `GameplaySceneSetup.Systems` | 397 | 1 | 25+ métodos `EnsureXxx()` | 🟡 **REPETITIVO** |

**Mejoras vs febrero:**

- ✅ `SaveService` (582→ refactorizado en `SaveFileManager` + `GameStateCollector` + `GameStateRestorer` + `SaveLoadInputHandler` + `SaveSchemaMigrator` + `PendingSaveLoad`) — **6 archivos especializados**
- ✅ `SpellCaster` (326→ extraído `ISpellExecutor` strategy pattern con 20+ ejecutores dedicados)
- ✅ `TileEditorManager` (734→ separado en `TileEditorInputHandler` + `TileEditorUndoSystem` + partials)
- ❌ `MainMenuUI` y `PauseMenuUI` siguen siendo God Classes (crecieron con Options, Sounds, Inputs)

### 3.3 Acoplamiento y Cohesión — Calificación: ⚠️ B

**`FindObjectOfType` en hot paths (runtime):**

| Sev. | Archivo | Línea | Contexto | Impacto |
| ------ | --------- | ------- | ---------- | --------- |
| 🔴 **CRÍTICO** | `NPCSeparationSystem.cs` | L35 | `FindObjectsOfType<FSMMonsterBrain>()` en `FixedUpdate()` | O(n) cada physics frame. Con 50+ NPCs = 1-2ms/frame |
| 🟠 **MEDIO** | `ZonePortal.cs` | L69, L86 | `FindObjectOfType<WorldGridBuilder/ZoneManager>()` en colisión | Innecesario; cachear en Awake |
| 🟠 **MEDIO** | `MinimapDot.cs` | L27 | `FindObjectOfType<MinimapManager>()` en OnEnable | 100+ calls en escena con muchas entidades |
| 🟠 **MEDIO** | `ParticleInstancesLoader.cs` | L187 | `FindObjectOfType<ZoneManager>()` desde snapshot | Lookup caro en render path |
| 🟡 **BAJO** | `CoinPickup.cs` | L64 | `FindGameObjectWithTag("Player")` en Start | Debería usar `EntityRegistry.PlayerTransform` |

**Reflection en código runtime (no Editor):**

| Archivo | Uso | Cached? | Riesgo |
| --------- | ----- | --------- | -------- |
| `DayNightCycle.cs` | Light2D property setters via PropertyInfo | ✅ Static cache | Bajo |
| `GameplaySceneSetup.Systems.cs` | Global Light2D creation via reflection | ✅ One-time | Medio (breaking en URP updates) |
| `WorldLightLoader.cs` | Point Light2D creation via reflection | ⚠️ Per-light (5 property sets) | Medio |

**`Camera.main` — Estado:**

- ✅ **15/15 usos correctamente cacheados** o en code paths one-time
- ✅ 0 usos repetidos en Update loops

### 3.4 Sistema de Eventos — Calificación: ✅ A

**`GameEvents.cs`** — 9 eventos estáticos con fire methods + `Clear()`:

| Evento | Suscriptores | Desuscripción | Estado |
| -------- | ------------- | --------------- | -------- |
| `OnEntityDamaged` | CombatAudioSystem | OnDisable `-=` | ✅ Balanceado |
| `OnEntityDied` | (unused currently) | — | ✅ Listo para uso |
| `OnHitDealt` | ComboCounter, CombatAudioSystem | OnDisable `-=` | ✅ Balanceado |
| `OnPlayerDamaged` | (unused currently) | — | ✅ Listo |
| `OnPlayerDied` | (unused currently) | — | ✅ Listo |
| `OnXpGained` | (unused currently) | — | ✅ Listo |
| `OnLevelUp` | (unused currently) | — | ✅ Listo |
| `OnItemPickedUp` | (unused currently) | — | ✅ Listo |
| `OnItemConsumed` | (unused currently) | — | ✅ Listo |

**Eventos locales (Component-level)**:

- `Health.OnDamaged` → FloatingDamageSpawner, CombatFeedback ✅ Balanceados
- `Health.OnDeath` → CombatFeedback ✅ Balanceado
- `Health.OnHpChanged` → WorldHealthBar ✅ Balanceado
- InputAction `.Enable()/.Disable()` → PickupSystem, SaveLoadInputHandler, PlayerController ✅ Correcto

**Memory leaks por suscripciones:** ✅ **0 detectados** — Todos los `+=` tienen su correspondiente `-=` en OnDisable/OnDestroy.

### 3.5 Manejo de Errores — Calificación: ✅ A-

| Métrica | Valor |
| --------- | ------- |
| Total `try-catch` blocks | 20 |
| Con `Debug.LogError` | 8 |
| Con `Debug.LogWarning` | 3 |
| Fallback silencioso intencional (conversiones) | 9 |
| Catch vacíos | **0** ✅ |
| `#pragma warning disable` | 10 (todos con scope y restore) ✅ |
| `Debug.Log` en Update loops | **0** ✅ |

### 3.6 Serialización y Persistencia — Calificación: ✅ A

| Verificación | Estado |
| ------------- | -------- |
| `Dictionary<>` en `SaveData.cs` | ✅ 0 — Usa `SerializableKeyValue[]` |
| Schema migration pipeline | ✅ `SaveSchemaMigrator` v1.0 → v1.1 |
| `JsonUtility` con tipos no soportados | ✅ 0 — 21 usos, todos con tipos compatibles |
| Backup rotation | ✅ Implementado en `SaveFileManager` |
| Checksum SHA-256 | ✅ Calculado y validado |
| Recovery fallback | ✅ Backup auto-load si checksum falla |

---

## 4. Auditoría de Rendimiento y Escalabilidad

### 4.1 Anti-patterns de Rendimiento — Calificación: ✅ A-

| Patrón | Instancias | Estado |
| -------- | ----------- | -------- |
| `new Material()` | 14 | ✅ 11/14 con cleanup en OnDestroy. 3 pendientes de verificación (EntitySpriteHelper, FireballVisual, ParticleEmitter) |
| `new Mesh()` | 0 | ✅ Limpio |
| `Camera.main` sin cachear | 0 | ✅ Todo cacheado |
| String interpolation `$""` en Update | 0 | ✅ Limpio |
| `.ToString()` en Update | 1 (DebugHUD, condicional) | ✅ Aceptable |
| LINQ en hot paths | 0 | ✅ Limpio |
| `Debug.Log` en Update | 0 | ✅ Limpio |

**Material leaks pendientes (3 verificaciones):**

| Archivo | Línea | Shader | Estado |
| --------- | ------- | -------- | -------- |
| `EntitySpriteHelper.cs` | L39 | Sprite-Unlit-Default | ⚠️ Verificar cleanup |
| `FireballVisual.cs` | L92 | Sprite-Unlit-Default | ⚠️ Verificar cleanup |
| `ParticleEmitter.ParticleSystem.cs` | L196, L209 | Varios | ⚠️ Verificar cleanup |

### 4.2 Object Pooling — Calificación: ⚠️ B-

**Pool existente:** `ObjectPool.cs` — Stack-based, pre-warm, hard cap, active tracking. API sólida.

**Uso actual:**

| Sistema | ¿Usa Pool? | Frecuencia | Impacto |
| --------- | ----------- | ----------- | --------- |
| Projectiles (fireball) | ⚠️ Parcial — prefab pool via `ProjectilePrefabFactory` | ~2/sec | Medio |
| Boomerang spell | ❌ `Instantiate()` raw | ~1/sec | Medio |
| Projectile spells | ❌ `Instantiate()` raw | ~2/sec | Medio |
| Floating damage numbers | ❌ `new GameObject()` | ~5/sec | 🔴 **ALTO** |
| Monster spawn | ❌ `Instantiate()` raw | ~0.5/sec | Medio (pero acumula) |
| VFX particles | ✅ Cached/reutilizados | — | Bajo |

### 4.3 Physics2D — Calificación: ⚠️ C+

**Layer Collision Matrix:**

| Verificación | Estado |
| ------------- | -------- |
| Sorting layers vs SortingConfig.cs | ✅ **14/14 alineados** (issue C2 resuelto) |
| Physics layers definidos (8-15) | ✅ 8 layers correctos |
| Collision matrix selectiva | ❌ **PERSISTE ALL-TO-ALL en layers 0-7** |
| Queries con layerMask | ✅ **100% — Todos pasan layerMask explícito** |

> ⚠️ **Nota:** La collision matrix tiene `ff` (all-to-all) en los layers default (0-7). Los layers custom 8-15 tienen valores parcialmente configurados (`015a`, `004f`, `004a`, `0107`). Aunque en la práctica todos los physics queries usan layerMask explícito (lo que mitiga el efecto), la matrix sigue generando physics broadphase innecesario entre layers que no deberían colisionar.

### 4.4 Escalabilidad de Datos — Calificación: ✅ A-

| Catálogo | Estructura | Lookup | Escala |
| ---------- | ----------- | -------- | -------- |
| `SpellCatalog` | Array + lazy `Dictionary<string, T>` | O(1) | ✅ |
| `MonsterCatalog` | List + lazy `Dictionary<string, T>` | O(1) | ✅ |
| `AudioCatalogSO` | Arrays + lazy dictionaries | O(1) | ✅ |
| `ChatAssignmentCatalog` | List + lazy `Dictionary` | O(1) | ✅ |
| `SpawnerTemplateCatalog` | List + lazy `Dictionary` | O(1) | ✅ |
| `LightPresetCatalog` | List + lazy `Dictionary` | O(1) | ✅ |
| `PlayerClassCatalog` | Static array | O(n) | ⚠️ (n=5, aceptable) |
| `BuildingCatalog` | List, foreach | O(n) | ❌ Sin cachear |
| `ParticlePresetCatalog` | List, foreach | O(n) | ❌ Sin cachear |

---

## 5. Auditoría de Testing

### 5.1 Cobertura — Calificación: ⚠️ C+

**EditMode Tests — 18 archivos, ~135 test cases:**

| Sistema | Archivo de Test | ~Casos | Cobertura |
| --------- | ---------------- | -------- | ----------- |
| Health | HealthTests.cs | 12 | ✅ Buena |
| Inventory | InventoryTests.cs | 10+ | ✅ Buena |
| SpellCaster | SpellCasterTests.cs | 20+ | ✅ Comprehensive |
| FSM (StateMachine) | FSMTests.cs | 10+ | ✅ Buena |
| SpatialHash | SpatialHashTests.cs | 8 | ✅ Buena |
| SaveData | SaveDataTests.cs | 5 | ✅ Básica |
| GameSettings | GameSettingsTests.cs | 8 | ✅ Buena |
| CombatTests | CombatTests.cs | 9 | ✅ Init y cooldowns |
| ZoneManager | ZoneManagerTests.cs | 7 | ✅ Buena |
| VendorNPC | VendorNPCTests.cs | 5 | ✅ Precios |
| DataMigration | DataMigrationTests.cs | 6 | ✅ Parity checks |
| DirectionalAnimator | DirectionalAnimatorTests.cs | 2 | ⚠️ Mínima |
| MainMenuUI | MainMenuUITests.cs | 4 | ⚠️ Solo screens |
| PauseMenuUI | PauseMenuUITests.cs | 5 | ⚠️ Solo state |
| TileBrush | TileBrushTests.cs | 2 | ⚠️ Mínima |
| PlayerSelection | PlayerSelectionStateTests.cs | 3 | ✅ Selección |
| PendingSaveLoad | PendingSaveLoadTests.cs | 6 | ✅ Queue tracking |
| Bootstrap | BootstrapTests.cs | 3 | ✅ Smoke tests |

**PlayMode Tests:** ❌ **0 archivos** — Directorio existe pero vacío.

**Sistemas sin tests:**

| Sistema | Riesgo | Prioridad |
| --------- | -------- | ----------- |
| PlayerController (movimiento, input) | Medio | P2 |
| Spell executors (20+ tipos) | Alto | P1 |
| Monster AI states (Patrol, Chase, Attack, etc.) | Alto | P1 |
| World loading (WorldLoader, OverlayLoader) | Medio | P2 |
| Audio system (AudioManager) | Bajo | P3 |
| Chat system | Bajo | P3 |
| Projectile lifecycle | Medio | P2 |
| Save/Load integration | Alto | P1 |

### 5.2 Calidad de Tests

| Aspecto | Estado |
| --------- | -------- |
| SetUp/TearDown para cleanup | ✅ Correcto |
| Destrucción de GameObjects | ✅ Correcto |
| Patrón AAA (Arrange, Act, Assert) | ✅ Seguido |
| Tests independientes (sin estado compartido) | ✅ Correcto |
| Coroutine/async testing | ❌ Ausente |
| Performance benchmarks | ❌ Ausente |

---

## 6. Configuración del Proyecto

### 6.1 Input System — Calificación: ⚠️ B

| Verificación | Estado |
| ------------- | -------- |
| New Input System activo | ✅ Sí |
| Legacy InputManager axes | ⚠️ 11 axes definidos (Horizontal, Vertical, Fire1-3, Jump, Mouse X/Y, ScrollWheel, Submit, Cancel) |
| Legacy API en código runtime | ⚠️ 2 usos: `ChatUI.cs` L72, `ChatSystem.cs` L214 |
| Proyecto en modo "Both" | ⚠️ Probablemente (ambos sistemas activos) |

**Recomendación:** Migrar los 2 usos legacy restantes a InputAction y configurar proyecto a "Input System Package (New)" exclusivo.

### 6.2 Otras Settings

| Setting | Valor | Correcto |
| --------- | ------- | ---------- |
| Unity version | 2022.3.62f1 | ✅ LTS |
| Gravity 2D | (0, 0) | ✅ Top-down correcto |
| vSync | 1 (60 FPS) | ✅ |
| Auto Sync Transforms | false | ✅ (mejor perf) |
| Queries Hit Triggers | false | ✅ |
| Quality levels | 6 niveles con URP 2D | ✅ |

---

## 7. Resumen de Hallazgos por Severidad

### 🔴 CRÍTICO (2)

| # | Hallazgo | Archivo | Impacto | Esfuerzo |
| --- | ---------- | --------- | --------- | ---------- |
| C1 | `NPCSeparationSystem.FixedUpdate()` llama `FindObjectsOfType<FSMMonsterBrain>()` cada physics frame | `NPCSeparationSystem.cs:35` | O(n²) physics overhead, ~1-2ms/frame con 50+ NPCs | Bajo |
| C2 | 0 PlayMode tests — ningún test de integración runtime | `Tests/PlayMode/` | Regresiones en gameplay no detectadas | Medio |

### 🟠 ALTO (6)

| # | Hallazgo | Archivo | Impacto | Esfuerzo |
| --- | ---------- | --------- | --------- | ---------- |
| H1 | `MainMenuUI` (~1700 LOC) es God Class con 8 responsabilidades | `Scripts/UI/MainMenu/MainMenuUI*.cs` | Difícil de mantener, testear y extender | Alto |
| H2 | `PauseMenuUI` (~1400 LOC) es God Class con 7 responsabilidades | `Scripts/UI/PauseMenu/PauseMenuUI*.cs` | Mismo problema que H1 | Alto |
| H3 | `PlayerHUD.Mana` no conectado a componente `Mana` (hardcoded 100/100) | `PlayerHUD.cs` | Jugador ve MP incorrecto | Bajo |
| H4 | `FloatingDamageNumber` usa `new GameObject()` sin pooling (~5/sec) | `CombatFeedback.cs:68` | GC spikes en combate intenso | Medio |
| H5 | Projectiles (Boomerang, Projectile) usan `Instantiate()` sin pool | `BoomerangExecutor.cs:23`, `ProjectileExecutor.cs:18` | GC pressure en combate con spells | Medio |
| H6 | Collision matrix layers 0-7 siguen all-to-all (mitiga layerMask en queries) | `Physics2DSettings.asset` | Broadphase physics innecesario | Bajo |

### 🟡 MEDIO (9)

| # | Hallazgo | Archivo | Impacto |
| --- | ---------- | --------- | --------- |
| M1 | `VendorShopUI.*.cs` (3 archivos) en carpeta `Enemies/` en vez de `Vendors/` | `Gameplay/Enemies/VendorShopUI*.cs` | Confusión de dominio |
| M2 | `TileEditorUI.Builder.cs` y `TileEditorUIBuilder.cs` — conflicto de nombres | `Gameplay/TileEditor/` | Ambigüedad |
| M3 | `GameDirector`, `GameSettings`, `PauseMenuUI` bypasean ServiceLocator | `Core/`, `UI/PauseMenu/` | Patrón inconsistente, difícil de testear |
| M4 | 3 posibles material leaks sin verificar cleanup | `EntitySpriteHelper`, `FireballVisual`, `ParticleEmitter` | Memory leaks potenciales |
| M5 | `ZonePortal.cs` usa `FindObjectOfType` en code paths de colisión | `World/ZonePortal.cs:69,86` | Lookups caros innecesarios |
| M6 | `MinimapDot.cs` usa `FindObjectOfType` en OnEnable (100+ entidades) | `UI/HUD/MinimapDot.cs:27` | Perf con muchas entidades |
| M7 | `BuildingCatalog.GetById()` y `ParticlePresetCatalog.GetById()` son O(n) | `Data/BuildingCatalog.cs`, `Data/ParticlePresetCatalog.cs` | No escala a 100+ entries |
| M8 | 2 usos legacy de Input API (`ChatUI`, `ChatSystem`) | `Chat/ChatUI.cs:72`, `Chat/ChatSystem.cs:214` | Romperá si se desactiva legacy input |
| M9 | Light2D reflection fragile (3 archivos) — puede romper en URP updates | `DayNightCycle`, `GameplaySceneSetup.Systems`, `WorldLightLoader` | Mantenimiento delicado |

### 🟢 BAJO / INFORMATIVO (4)

| # | Hallazgo | Notas |
| --- | ---------- | ------- |
| L1 | `Screenshots/` en Assets root | Mover a `_Project/` o excluir |
| L2 | 18 partials con sufijos genéricos (`Helpers`, `Internals`, etc.) | Renombrar para claridad |
| L3 | `ParticleEmitter.ParticleSystem.cs` colisiona con nombre Unity | Renombrar |
| L4 | 5 eventos `GameEvents` declarados pero sin suscriptores activos | Normal en migración gradual |

---

## 8. Calificaciones por Área

| Área | Nota | Detalles |
| ------ | ------ | --------- |
| **Estructura de carpetas** | ✅ A | Organización profesional, `_Project/` limpio, Resources mínimo |
| **Assemblies (.asmdef)** | ✅ A+ | Grafo limpio, sin circulares, namespaces correctos |
| **Convenciones de nombrado** | ⚠️ B | 77% correcto, conflictos menores, archivos mal ubicados |
| **Patrón de DI (ServiceLocator)** | ⚠️ B- | Bien implementado pero adoptado solo parcialmente (2/6 servicios) |
| **God Classes** | ⚠️ B- | 3 refactorizadas (SaveService, SpellCaster, TileEditor), 2 persisten (MainMenuUI, PauseMenuUI) |
| **Acoplamiento** | ⚠️ B | 1 crítico (NPCSeparation), eventos bien implementados, Camera cacheada |
| **Sistema de eventos** | ✅ A | 0 memory leaks, patrón OnEnable/OnDisable consistente |
| **Manejo de errores** | ✅ A- | 0 catch vacíos, logging apropiado, pragmas scoped |
| **Serialización** | ✅ A | Issue C1 resuelto, tipos seguros, schema migration funcional |
| **Rendimiento** | ✅ A- | Materials mayormente limpios, Camera cacheada, 0 LINQ/string en hot paths |
| **Pooling** | ⚠️ B- | Pool API sólida pero sub-utilizada (3 sistemas sin pool) |
| **Physics2D** | ⚠️ C+ | Layers correctos, queries con mask, pero matrix maldefinida |
| **Escalabilidad de datos** | ✅ A- | 7/9 catálogos O(1), 2 pendientes |
| **Cobertura de tests** | ⚠️ C+ | 135 EditMode cases (bueno), 0 PlayMode (malo), 60% sistemas sin tests |
| **Configuración de proyecto** | ✅ B+ | Unity LTS correcto, legacy input casi eliminado |

### Nota Global: **B+ (78/100)**

**Progreso desde Feb 2026:** +15 puntos (de ~63 a 78)

---

## 9. Plan de Acción Priorizado

### Sprint 1 — Críticos y Quick Wins (1-2 días)

| # | Tarea | Hallazgo | Esfuerzo |
| --- | ------- | ---------- | ---------- |
| 1 | Cache NPC list en `NPCSeparationSystem` — usar registro de entidades | C1 | 30 min |
| 2 | Conectar `Mana.OnManaChanged` → `PlayerHUD.SetMana()` | H3 | 30 min |
| 3 | Migrar `ChatUI`/`ChatSystem` de legacy Input a InputAction | M8 | 1 hora |
| 4 | Agregar lazy `Dictionary` a `BuildingCatalog` y `ParticlePresetCatalog` | M7 | 30 min |
| 5 | Cache `WorldGridBuilder`/`ZoneManager` en `ZonePortal.Awake()` | M5 | 15 min |
| 6 | Usar `MinimapManager.Instance` en `MinimapDot` en vez de FindObjectOfType | M6 | 15 min |

### Sprint 2 — Pooling y Estructura (2-3 días)

| # | Tarea | Hallazgo | Esfuerzo |
| --- | ------- | ---------- | ---------- |
| 7 | Pool para FloatingDamageNumber (usar ObjectPool existente) | H4 | 2 horas |
| 8 | Pool para Boomerang y Projectile executors | H5 | 2 horas |
| 9 | Mover `VendorShopUI.*.cs` a `Gameplay/Vendors/` | M1 | 30 min |
| 10 | Resolver conflicto `TileEditorUI.Builder` vs `TileEditorUIBuilder` | M2 | 1 hora |
| 11 | Verificar material cleanup en EntitySpriteHelper, FireballVisual, ParticleEmitter | M4 | 1 hora |
| 12 | Registrar `GameDirector`, `GameSettings` en ServiceLocator | M3 | 1 hora |

### Sprint 3 — Testing (3-5 días)

| # | Tarea | Hallazgo | Esfuerzo |
| --- | ------- | ---------- | ---------- |
| 13 | Crear PlayMode tests para spell casting loop (mana + cooldown + execution) | C2 | 4 horas |
| 14 | Crear PlayMode tests para combat hit flow (damage → event → feedback) | C2 | 3 horas |
| 15 | Crear PlayMode tests para monster FSM (idle → chase → attack → death) | C2 | 4 horas |
| 16 | Crear PlayMode tests para save/load roundtrip | C2 | 3 horas |

### Sprint 4 — God Classes (5-10 días, opcional)

| # | Tarea | Hallazgo | Esfuerzo |
| --- | ------- | ---------- | ---------- |
| 17 | Extraer `MainMenuUI` → `MenuCarousel`, `ClassSelectorUI`, `OptionsMenuUI`, `InputBindingUI` | H1 | 8 horas |
| 18 | Extraer `PauseMenuUI` → `PauseOptionsUI`, `SoundSettingsUI`, `InputDisplayUI`, `LoadGameUI` | H2 | 8 horas |

---

## Apéndice A — Arquitectura Actual (Mapa Completo)

```text
Bootstrap.unity
  └─ GameBootstrap (DontDestroyOnLoad)
       └─ LoadScene("MainMenu")

MainMenu.unity
  └─ MainMenuUI (singleton)
       ├─ Carousel (5 imágenes de fondo)
       ├─ ClassSelector (5 clases, portraits, stats)
       ├─ Options (Inputs, Sonido, Volver)
       └─ LoadPanel (save slots)
       └─ AudioManager via ServiceLocator<IAudioService>

MainGameplay.unity
  ├─ GameDirector (singleton, orchestrator)
  │    └─ PerformanceMonitor
  ├─ GameplaySceneSetup (scene bootstrap)
  │    ├─ WorldGridBuilder → Grid + 9 Tilemaps
  │    ├─ ZoneManager → 24 zones, 8×3 grid
  │    ├─ WorldLoader → Overlays + Collision grids + Buildings + Spawners + Particles + Lights
  │    ├─ EnsureGlobalLight2D (URP reflection)
  │    ├─ VFXManager (ServiceLocator<IVFXService>)
  │    ├─ AudioManager (ServiceLocator<IAudioService>)
  │    ├─ CombatAudioSystem → GameEvents subscription
  │    ├─ ChatSystem + ChatUI
  │    ├─ VendorEconomyService
  │    ├─ SpawnerEditorManager (F3)
  │    ├─ TileEditorManager (F6)
  │    ├─ MapEditorManager (F7)
  │    ├─ SpawnPlayer → EntitySetup.ConfigurePlayer()
  │    └─ SpawnTestMonsters → EntitySetup.ConfigureMonster()
  ├─ HUDBootstrap
  │    ├─ HUDManager → PlayerHUD + TargetHUD + MinimapManager
  │    ├─ DebugHUD (F1)
  │    ├─ ComboHUD
  │    └─ DeathScreenUI
  ├─ PauseMenuUI (DontDestroyOnLoad, ESC toggle)
  ├─ LoadingScreenController (DontDestroyOnLoad)
  ├─ DayNightCycle
  ├─ NPCSeparationSystem
  └─ SaveService (autosave, F5/F9)
```

## Apéndice B — Listado de Assemblies y Dependencias

```text
Valkur.Core (15 archivos)
  → Unity.InputSystem

Valkur.Data (35 archivos)
  → Valkur.Core

Valkur.Infrastructure (3 archivos)
  → Valkur.Core, Valkur.Data

Valkur.Gameplay (~155 archivos)
  → Valkur.Core, Valkur.Data, Valkur.Infrastructure
  → Unity.InputSystem, Unity.TextMeshPro, Cinemachine, PixelPerfect

Valkur.UI (~32 archivos)
  → Valkur.Core, Valkur.Gameplay, Valkur.Data, Valkur.Infrastructure
  → Unity.TextMeshPro, Unity.InputSystem

Valkur.Editor (~58 archivos)
  → Valkur.Core, Valkur.Gameplay, Valkur.Data
  [Platform: Editor only]

Valkur.Tests.EditMode (18 archivos)
  → All 5 assemblies + TestRunner
  [Constraint: UNITY_INCLUDE_TESTS]

Valkur.Tests.PlayMode (0 archivos)
  → All 5 assemblies + TestRunner
  [Constraint: UNITY_INCLUDE_TESTS]
```
