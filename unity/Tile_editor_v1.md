# Tile Editor v1 — Unity Runtime

> **Estado**: UI funcional, **pintado de tiles NO funciona** (renderiza negro).
> **Última actualización**: 2025-02-22
> **Versión Unity**: 2022.3 LTS | **URP**: 14.0.12 | **2D Feature**: 2.0.1

## Screenshot actual

![Tile Editor v1 — tiles pintan en negro](image/Tile_editor_v1/1771782433795.png)

---

## Estado actual

El Tile Editor runtime funciona en Unity con toggle F6. La UI replica los paneles del editor Python original:
- **Panel izquierdo**: toolbar (B/E/F/I/S), selector de layer (< >), brush size (1-5), preview del tile seleccionado, category tabs (grid 2 columnas), tile picker grid scrollable, status bar
- **Panel derecho**: View panel (hovered/selected/choice tile info), Layers panel (9 layers con nombres)
- **Indicador de layer** activo en la parte inferior central ("0: Ground")
- **Border overlay** dorado + label "TILE EDITOR — BRUSH" en la parte superior
- **Grid cursor** (LineRenderer dorado) que sigue al mouse y muestra el tamaño del brush

**Lo que funciona**: toda la UI, selección de tiles, categorías, cambio de layers, brush size, undo/redo, eyedropper, eraser (borra tiles existentes), grid cursor.

**Lo que NO funciona**: al pintar tiles con el brush, estos aparecen como **rectángulos negros sólidos** en el tilemap.

---

## Funcionalidades implementadas

| Feature | Estado | Notas |
|---------|--------|-------|
| Toggle F6 | ✅ | Abre/cierra el editor |
| Toolbar (B/E/F/I/S) | ✅ | Brush, Eraser, Fill, Eyedropper, Select |
| Layer selector (< >) | ✅ | 9 layers, resetea a Ground al abrir |
| Brush size (1-5) | ✅ | Botones -/+ y números directos |
| Tile Picker | ✅ | Grid scrollable, carga runtime desde Resources/Tiles/ |
| Category tabs | ✅ | Grid 2 columnas, "All" por defecto, 8 categorías |
| Selected tile preview | ✅ | Sprite + nombre del tile seleccionado |
| View panel | ✅ | Hovered/Selected/Choice tile info + layer info |
| Layers panel | ✅ | 9 layers con nombres |
| Grid cursor | ✅ | LineRenderer dorado, cambia color por tool |
| Border overlay | ✅ | Borde dorado + label herramienta activa |
| Undo/Redo | ✅ | Ctrl+Z / Ctrl+Shift+Z (max 50 operaciones) |
| Brush painting | ❌ | **BUG ABIERTO: tiles se pintan en negro** |
| Fill tool | ❌ | Mismo bug de renderizado negro |
| Eraser | ✅ | Funciona correctamente |
| Eyedropper | ✅ | Funciona correctamente |
| Collision panel | ❌ | Pendiente (futuro) |
| Save/Load JSON | ❌ | Pendiente (futuro) |
| Tutorial panel | ❌ | Pendiente (futuro) |

---

## 🔴 ISSUE ABIERTA: Tiles se pintan en negro

### Síntoma
Al pintar tiles con el brush, estos aparecen como rectángulos negros sólidos en el tilemap, en lugar de mostrar la textura del sprite. El tile picker UI muestra los sprites correctamente. El `SetTile()` se ejecuta sin errores. Los tiles se colocan en la posición correcta pero su textura no se renderiza.

### Datos del diagnóstico (debug log)
```
tile=tileset3_r1_c1 type=Tile sprite=tileset3_r1_c1 ppu=32 color=RGBA(1,1,1,1)
tilemap=Ground sortLayer=Ground sortOrder=0 rendererEnabled=True
mat=Default (Instance) shader=Universal Render Pipeline/2D/Sprite-Lit-Default
tilemapColor=RGBA(1,1,1,1)
```

---

## 🔬 Análisis exhaustivo de causas posibles

### Causa 1: FALTA LIGHT2D EN LA ESCENA (probabilidad: MUY ALTA ⭐⭐⭐⭐⭐)

**El proyecto usa URP 14.0.12 con Renderer2D.** Verificado:
- `Renderer2D.asset`: `m_DefaultMaterialType: 0` → **Lit** (requiere Light2D)
- `m_DefaultLitMaterial` apunta a `Sprite-Lit-Default`
- El shader `Universal Render Pipeline/2D/Sprite-Lit-Default` multiplica el color del sprite por la luz 2D
- **Sin Light2D → multiplicación por 0 → negro puro**

El UI Canvas no se ve afectado porque usa `CanvasRenderer` (pipeline separado), por eso el tile picker muestra los sprites correctamente.

**Estado actual**: `GameplaySceneSetup.EnsureGlobalLight2D()` intenta crear Light2D via reflexión, pero puede fallar silenciosamente por:
- El `lightType` property en URP 14.x puede no ser accesible via `GetProperty("lightType")` — en algunas versiones es un campo serializado `m_LightType`, no una propiedad pública
- La reflexión puede encontrar el tipo pero fallar al setear propiedades sin lanzar excepción

### Causa 2: REFLEXIÓN DE LIGHT2D FALLA SILENCIOSAMENTE (probabilidad: ALTA ⭐⭐⭐⭐)

En `GameplaySceneSetup.EnsureGlobalLight2D()`:
```csharp
var lightTypeProp = light2DType.GetProperty("lightType");
if (lightTypeProp != null)
    lightTypeProp.SetValue(light, 1); // 1 = Global
```

Problemas potenciales:
1. En URP 14.x, `Light2D.lightType` es de tipo `Light2D.LightType` (enum), no `int`. Pasar `1` como int puede no hacer el cast implícito correctamente.
2. El nombre de la propiedad puede ser `lightType` (lowercase) en versiones antiguas pero `LightType` en nuevas.
3. Si `GetProperty` retorna null, el Light2D se crea pero queda como **Freeform** (tipo 0) en vez de **Global** (tipo 1), lo que solo ilumina un área pequeña, no toda la escena.
4. No hay log de error si la propiedad no se encuentra — falla silenciosamente.

### Causa 3: MATERIAL INCORRECTO EN TILEMAPRENDERER (probabilidad: MEDIA ⭐⭐⭐)

`WorldGridBuilder.CreateTilemapLayer()` no asigna material explícitamente. Unity asigna el default del Renderer2D, que es `Sprite-Lit-Default`. Si la Light2D no funciona, una solución directa es cambiar a `Sprite-Unlit-Default`.

El `Renderer2D.asset` ya tiene referencia al material unlit:
```yaml
m_DefaultUnlitMaterial: {fileID: 2100000, guid: 9dfc825aed78fcd4ba02077103263b40, type: 2}
```

### Causa 4: SORTING LAYER INVÁLIDO → RENDERER SILENCIOSAMENTE INVISIBLE (probabilidad: BAJA ⭐⭐)

**RESUELTO en Sprint 1**: `TagManager.asset` ahora tiene los 15 sorting layers que coinciden con `SortingConfig.cs`. Antes faltaban `FloorDecals`, `ObjectsLow`, `WallsBottom`, `Decorations`, `WallsTop`, `ObjectsHigh`, `Overhead`.

Cuando un `TilemapRenderer` referencia un sorting layer que no existe en TagManager, Unity lo mueve silenciosamente a `Default` con sortingOrder 0. Esto podría causar que se renderice detrás de todo (pero no negro). **Ya no debería ser un problema.**

### Causa 5: TILE.SPRITE ES NULL O INVÁLIDO DESPUÉS DE CREATEINSTANCE (probabilidad: BAJA ⭐⭐)

`TileCatalog.BuildFromResources()` crea tiles con `ScriptableObject.CreateInstance<Tile>()` y asigna `tile.sprite = sprite`. Si el sprite se descarga de memoria (garbage collected) antes de que el tilemap lo use, el tile renderizaría sin textura (negro).

Esto es poco probable porque `Resources.LoadAll<Sprite>()` mantiene los sprites en memoria mientras haya referencia, y el catálogo los retiene. Pero vale la pena verificar con diagnóstico.

### Causa 6: COMPRESIÓN DE TEXTURA INCOMPATIBLE (probabilidad: MUY BAJA ⭐)

Los sprites tienen `textureCompression: 0` en DefaultTexturePlatform (None) pero `textureCompression: 1` en Standalone (Normal Quality). Si la compresión genera un formato incompatible con el shader 2D Lit, podría renderizar negro. Muy improbable con URP estándar.

### Causa 7: SPRITE PIVOT OFFSET (probabilidad: MUY BAJA ⭐)

Los sprites tienen `spritePivot: {x: 0.5, y: 0}` (bottom-center) y el tilemap usa `tileAnchor: (0.5, 0.5, 0)`. Esto causaría un offset visual (tiles desplazados medio tile hacia arriba) pero NO negro. No es la causa del bug.

### Causa 8: Z-FIGHTING O CAMERA CULLING (probabilidad: MUY BAJA ⭐)

Si los tilemaps están en una posición Z que la cámara no renderiza, aparecerían invisibles (no negros). El tilemap se crea en z=0 y la cámara debería verlo. Descartado.

---

### Resumen de probabilidades

| # | Causa | Probabilidad | Fix |
|---|-------|-------------|-----|
| 1 | Falta Light2D en escena | ⭐⭐⭐⭐⭐ | Crear Light2D correctamente o usar material Unlit |
| 2 | Reflexión de Light2D falla silenciosamente | ⭐⭐⭐⭐ | Mejorar reflexión con diagnóstico + fallback a Unlit |
| 3 | Material Lit sin luz = negro | ⭐⭐⭐ | Asignar Sprite-Unlit-Default a TilemapRenderers |
| 4 | Sorting layer inválido | ⭐⭐ | RESUELTO (Sprint 1) |
| 5 | Sprite null/GC'd | ⭐⭐ | Verificar con diagnóstico |
| 6 | Compresión incompatible | ⭐ | Verificar import settings |
| 7 | Pivot offset | ⭐ | No causa negro |
| 8 | Z-fighting/culling | ⭐ | No causa negro |

---

## Cronología de intentos de solución

| # | Intento | Resultado |
|---|---------|-----------|
| 1 | Deshabilitar brush preview `SpriteRenderer` | No resolvió — el negro viene del tilemap, no del preview |
| 2 | Registrar `SpriteAtlasManager.atlasRequested` callback | No funcionó — el atlas no está en Resources |
| 3 | Eliminar `Atlas_Tiles.spriteatlas` completamente | No resolvió — el negro persiste sin atlas |
| 4 | Mover sprites a `Resources/Tiles/` y crear tiles en runtime | Tile picker funciona, pero el negro persiste |
| 5 | Crear `TileCatalog.BuildFromResources()` (tiles runtime sin .asset) | Tile picker funciona, negro persiste |
| 6 | Añadir `Global Light 2D` via `using UnityEngine.Rendering.Universal` | Error de compilación: assembly reference faltante |
| 7 | Crear `Light2D` via reflexión (`System.Type.GetType`) | Compila, pero reflexión puede fallar silenciosamente (ver Causa 2) |
| 8 | **[NUEVO] Doble estrategia: mejorar reflexión Light2D + fallback Unlit material** | Pendiente |
| 9 | **[NUEVO] Diagnóstico detallado en brush para confirmar causa en runtime** | Pendiente |

---

## Plan de solución (implementación actual)

### Estrategia: defensa en profundidad (3 capas)

**Capa 1 — Mejorar reflexión de Light2D** (`GameplaySceneSetup.cs`):
- Buscar propiedad `lightType` Y campo `m_LightType` como fallback
- Usar el enum correcto `Light2D.LightType.Global` via reflexión
- Añadir logs explícitos de éxito/fallo en cada paso
- Verificar que el Light2D creado realmente tiene tipo Global

**Capa 2 — Fallback a material Unlit** (`WorldGridBuilder.cs`):
- Si no hay Light2D en la escena, asignar `Sprite-Unlit-Default` a cada TilemapRenderer
- Buscar shader por nombre: `"Universal Render Pipeline/2D/Sprite-Unlit-Default"`
- Esto garantiza renderizado correcto sin depender de luces

**Capa 3 — Diagnóstico en brush** (`TileEditorManager.cs`):
- Al pintar, loggear: tile.sprite != null, sprite.texture != null, tilemap material, Light2D count
- Permite confirmar la causa raíz exacta en runtime

---

## Arquitectura

### Archivos principales
```
Scripts/Gameplay/TileEditor/
├── TileEditorManager.cs       — Orquestador principal, input, undo/redo, toggle F6
├── TileEditorState.cs         — Estado mutable (tool, layer, tile, brush size)
├── TileEditorUI.cs            — UI programática completa (Canvas, paneles, botones)
├── TileEditorBorderOverlay.cs — Borde dorado de pantalla + label herramienta
├── TileEditorGridCursor.cs    — Cursor de grid (LineRenderer + fill quad)
├── TileBrush.cs               — Operaciones: Paint, Erase, FloodFill, Pick
├── TileCatalog.cs             — ScriptableObject + BuildFromResources() runtime
└── TileRegistry.cs            — Singleton de lookup de tiles por nombre

Scripts/Editor/
└── TilePaletteBuilder.cs      — Genera tile assets, palette, y catálogo (editor-only)

Scripts/Gameplay/Rendering/
├── WorldGridBuilder.cs        — Crea Grid + 9 Tilemaps en runtime
└── TilemapLayerSetup.cs       — Configura sorting layers por tilemap

Scripts/Gameplay/
└── GameplaySceneSetup.cs      — Bootstrap: crea grid, light2D, VFX, tile editor, player
```

### Flujo de datos (actual — runtime)
1. `GameplaySceneSetup.Start()` → crea `WorldGridBuilder` (Grid + 9 Tilemaps)
2. `GameplaySceneSetup` → intenta crear `Global Light 2D` via reflexión
3. `GameplaySceneSetup` → crea `TileEditorManager`
4. `TileEditorManager.Start()` → `TileCatalog.BuildFromResources()` (carga sprites de `Resources/Tiles/{category}/`, crea `Tile` instances en runtime)
5. `TileEditorUI.Initialize()` → construye toda la UI programáticamente
6. Usuario presiona F6 → editor se activa
7. Usuario selecciona tile del picker → `_state.SelectedTile = entry.tile`
8. Usuario pinta → `TileBrush.Paint(tilemap, cellPos, tile, brushSize)`
9. `tilemap.SetTile(pos, tile)` → **TilemapRenderer renderiza NEGRO** (falta Light2D)

### Sprites
- **Ubicación**: `Assets/_Project/Resources/Tiles/{category}/` (movidos desde `Art/Tiles/ready/`)
- **Formato**: PNG, 32x32 pixels, PPU=32 (1 tile = 1 world unit)
- **Categorías**: grass_dirt, grass_rock, ocean_grass, rock_water, sand_grass, sand_ocean, sand_ocean_2, sand_rock
- **Total**: ~312 sprites (incluye subcarpetas `_slices/`)
- **SpriteAtlas**: ELIMINADO (`Atlas_Tiles.spriteatlas` fue borrado)
- **Import settings**: `spriteMode=1`, `filterMode=0` (Point), `textureCompression=0` (None en default), `isReadable=0`, `spritePivot=(0.5, 0)` (bottom-center)

### Layers (TilemapLayerSetup.TilemapLayer)
| Index | Nombre | Sorting Layer | Notas |
|-------|--------|---------------|-------|
| 0 | Ground | Ground | Layer por defecto del editor |
| 1 | FloorDecals | FloorDecals | |
| 2 | Collision | (renderer disabled) | Solo colisión |
| 3 | ObjectsLow | ObjectsLow | |
| 4 | WallsBottom | WallsBottom | + TilemapCollider2D |
| 5 | Decorations | Decorations | |
| 6 | WallsTop | WallsTop | |
| 7 | ObjectsHigh | ObjectsHigh | |
| 8 | OverheadDetails | Overhead | |

### Sorting Layers (TagManager.asset — actualizado Sprint 1)
```
Default, Background, Ground, FloorDecals, ObjectsLow, WallsBottom, Entities,
Decorations, WallsTop, ObjectsHigh, Projectiles, VFX, Overhead, UI_World, Overlay
```
**Estado**: ✅ Sincronizado con `SortingConfig.cs` (15 layers, todos presentes).

### Configuración URP
- **Pipeline**: `com.unity.render-pipelines.universal` 14.0.12
- **2D Feature**: `com.unity.feature.2d` 2.0.1
- **Renderer**: `Renderer2D.asset` con `m_DefaultMaterialType: 0` (Lit)
- **Lit Material**: `Sprite-Lit-Default` (requiere Light2D)
- **Unlit Material**: `Sprite-Unlit-Default` (guid: `9dfc825aed78fcd4ba02077103263b40`)

---

## Cambios realizados (sesiones anteriores)

### Commits relevantes
1. **fix(tile-editor): Disable brush preview SpriteRenderer** — eliminó el preview que se superponía
2. **fix(tile-editor): Delete SpriteAtlas** — eliminó `Atlas_Tiles.spriteatlas` y tile assets
3. **feat(tile-editor): Runtime tile catalog from Resources/Tiles/** — movió sprites a Resources, creó `BuildFromResources()`
4. **fix(tile-editor): Add Global Light 2D** — intento de crear Light2D para URP
5. **fix: Use reflection for Light2D** — evita dependency en assembly URP

### Archivos modificados
- `TileCatalog.cs` — añadido `BuildFromResources()` estático
- `TileEditorManager.cs` — usa `BuildFromResources()`, eliminó debug logging, deshabilitó brush preview
- `WorldGridBuilder.cs` — añadido `detectChunkCullingBounds`
- `GameplaySceneSetup.cs` — añadido `EnsureGlobalLight2D()` via reflexión
- `TilePaletteBuilder.cs` — actualizado path a `Resources/Tiles`
- Sprites movidos: `Art/Tiles/ready/` → `Resources/Tiles/`
- Eliminados: `Atlas_Tiles.spriteatlas`, todos los `.asset` en `TileAssets/`, `TileCatalog.asset`

---

## Pendientes futuros

| Prioridad | Feature | Descripción |
|-----------|---------|-------------|
| **ALTA** | Fix black tiles | Implementar defensa en profundidad (Light2D + Unlit fallback + diagnóstico) |
| Media | Collision panel | Panel para pintar tiles de colisión |
| Media | Save/Load JSON | Guardar/cargar mapas editados |
| Baja | Tutorial panel | Panel de ayuda con shortcuts |
| Baja | Brush preview | Re-habilitar preview con material correcto |
