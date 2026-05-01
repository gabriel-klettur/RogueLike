# Tile Editor v1 — Unity Runtime

> **Estado**: ✅ UI funcional, **pintado de tiles FUNCIONA**.
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

**Lo que funciona**: toda la UI, selección de tiles, categorías, cambio de layers, brush size, undo/redo, eyedropper, eraser, brush painting, fill tool, grid cursor.

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
| Brush painting | ✅ | **RESUELTO** — material cambiado a Sprite-Unlit-Default |
| Fill tool | ✅ | Funciona con material Unlit |
| Eraser | ✅ | Funciona correctamente |
| Eyedropper | ✅ | Funciona correctamente |
| Collision panel | ❌ | Pendiente (futuro) |
| Save/Load JSON | ❌ | Pendiente (futuro) |
| Tutorial panel | ❌ | Pendiente (futuro) |

---

## ✅ ISSUE RESUELTA: Tiles se pintaban en negro

### Síntoma original
Al pintar tiles con el brush, estos aparecían como rectángulos negros sólidos en el tilemap. El tile picker UI mostraba los sprites correctamente. `SetTile()` se ejecutaba sin errores.

### Causa raíz confirmada

**Combinación de Causa 2 + Causa 3**: El shader `Sprite-Lit-Default` (asignado por defecto por URP) requiere una `Light2D` de tipo **Global** para iluminar toda la escena. Aunque `GameplaySceneSetup.EnsureGlobalLight2D()` creaba un componente `Light2D` via reflexión, **la reflexión no lograba configurar el `lightType` como Global** — el Light2D quedaba como **Freeform** (tipo 0 por defecto), que solo ilumina dentro de un polígono pequeño, no toda la escena.

**Evidencia del diagnóstico runtime:**
```
material=Sprite-Lit-Default shader=Universal Render Pipeline/2D/Sprite-Lit-Default
Light2D count=1
  Light2D: 'Global Light 2D' active=True
```
- El Light2D existía y estaba activo
- El sprite, textura, sorting layer y renderer eran todos válidos
- Pero el `lightType` no era realmente Global — el nombre del GameObject era "Global Light 2D" pero eso es solo un label, no el tipo real del componente
- Con `Sprite-Lit-Default`, sin iluminación Global efectiva → multiplicación por 0 → **negro puro**

### Por qué la reflexión fallaba

En URP 14.0.12, `Light2D.lightType` es una propiedad pública de tipo `Light2D.LightType` (enum). La reflexión encontraba la propiedad, pero al hacer `SetValue(light, 1)` pasando un `int` en lugar del enum correcto, el setter podía no aplicar el cambio. Incluso con `Enum.ToObject()`, el comportamiento interno de URP puede requerir reinicialización del componente después de cambiar el tipo.

### Solución aplicada

**`WorldGridBuilder.ApplyUnlitFallbackIfNeeded()`** — Asigna `Sprite-Unlit-Default` a todos los `TilemapRenderer` incondicionalmente:
```csharp
var unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
var unlitMaterial = new Material(unlitShader);
foreach (var r in renderers)
    r.material = unlitMaterial;
```

`Sprite-Unlit-Default` renderiza sprites a brillo completo sin depender de ninguna luz 2D. Esto elimina completamente la dependencia en Light2D y la reflexión frágil.

### Lecciones aprendidas

1. **Reflexión para configurar componentes URP es frágil** — los setters de propiedades pueden tener side effects internos que no se activan via reflexión
2. **El nombre del GameObject no indica el tipo real del componente** — siempre verificar el estado interno
3. **Para tilemaps runtime, `Sprite-Unlit-Default` es más confiable** que depender de Light2D cuando no se tiene referencia directa al assembly URP
4. **Diagnósticos runtime son esenciales** — sin el log detallado del brush, habríamos seguido asumiendo que el Light2D era el problema principal

---

### Análisis de causas investigadas (referencia)

| # | Causa | Resultado | Detalle |
|---|-------|-----------|---------|
| 1 | Falta Light2D en escena | ❌ Descartada | Light2D existía (count=1, active=True) |
| 2 | Reflexión de Light2D falla silenciosamente | ✅ **CAUSA RAÍZ** | lightType quedaba como Freeform, no Global |
| 3 | Material Lit sin luz efectiva = negro | ✅ **CAUSA RAÍZ** | Sprite-Lit-Default × Freeform light = negro fuera del polígono |
| 4 | Sorting layer inválido | ❌ Descartada | Resuelto en Sprint 1 (15 layers sincronizados) |
| 5 | Sprite null/GC'd | ❌ Descartada | Diagnóstico confirmó sprite y texture válidos |
| 6 | Compresión incompatible | ❌ Descartada | No aplica con URP estándar |
| 7 | Pivot offset | ❌ Descartada | Causa offset visual, no negro |
| 8 | Z-fighting/culling | ❌ Descartada | Tiles visibles en posición correcta |

---

## Cronología de intentos de solución

| # | Intento | Resultado |
|---|---------|----------|
| 1 | Deshabilitar brush preview `SpriteRenderer` | ❌ El negro viene del tilemap, no del preview |
| 2 | Registrar `SpriteAtlasManager.atlasRequested` callback | ❌ El atlas no está en Resources |
| 3 | Eliminar `Atlas_Tiles.spriteatlas` completamente | ❌ Negro persiste sin atlas |
| 4 | Mover sprites a `Resources/Tiles/` y crear tiles en runtime | ❌ Tile picker funciona, negro persiste |
| 5 | Crear `TileCatalog.BuildFromResources()` (tiles runtime sin .asset) | ❌ Tile picker funciona, negro persiste |
| 6 | Añadir `Global Light 2D` via `using UnityEngine.Rendering.Universal` | ❌ Error de compilación: assembly reference faltante |
| 7 | Crear `Light2D` via reflexión (`System.Type.GetType`) | ❌ Compila, Light2D se crea pero lightType queda Freeform |
| 8 | Mejorar reflexión Light2D (property + field fallback + Enum.ToObject) | ❌ Light2D existe y activo, pero tiles siguen negros |
| 9 | Diagnóstico detallado en brush (sprite, texture, material, Light2D) | ✅ Confirmó que todo era válido excepto la iluminación efectiva |
| 10 | **Forzar `Sprite-Unlit-Default` en todos los TilemapRenderers** | ✅ **RESUELTO — tiles se renderizan correctamente** |

---

## Arquitectura

### Archivos principales
```
Scripts/Gameplay/Editors/Tile/
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
9. `tilemap.SetTile(pos, tile)` → TilemapRenderer renderiza con `Sprite-Unlit-Default`
10. `WorldGridBuilder.ApplyUnlitFallbackIfNeeded()` → aplica material Unlit a todos los renderers (1 frame después del build)

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

## Cambios realizados

### Commits relevantes
1. **fix(tile-editor): Disable brush preview SpriteRenderer** — eliminó el preview que se superponía
2. **fix(tile-editor): Delete SpriteAtlas** — eliminó `Atlas_Tiles.spriteatlas` y tile assets
3. **feat(tile-editor): Runtime tile catalog from Resources/Tiles/** — movió sprites a Resources, creó `BuildFromResources()`
4. **fix(tile-editor): Add Global Light 2D** — intento de crear Light2D para URP
5. **fix: Use reflection for Light2D** — evita dependency en assembly URP
6. **fix(tile-editor): defense-in-depth for black tiles** — reflexión robusta + diagnósticos
7. **fix(tile-editor): force Unlit material on all TilemapRenderers** — ✅ **FIX DEFINITIVO**

### Archivos modificados
- `TileCatalog.cs` — añadido `BuildFromResources()` estático
- `TileEditorManager.cs` — usa `BuildFromResources()`, deshabilitó brush preview, añadido `LogBrushDiagnostics()`
- `WorldGridBuilder.cs` — añadido `detectChunkCullingBounds`, añadido `ApplyUnlitFallbackIfNeeded()` coroutine
- `GameplaySceneSetup.cs` — añadido `EnsureGlobalLight2D()` con reflexión robusta (property + field fallback)
- `TilePaletteBuilder.cs` — actualizado path a `Resources/Tiles`
- Sprites movidos: `Art/Tiles/ready/` → `Resources/Tiles/`
- Eliminados: `Atlas_Tiles.spriteatlas`, todos los `.asset` en `TileAssets/`, `TileCatalog.asset`

---

## Pendientes futuros

| Prioridad | Feature | Descripción |
|-----------|---------|-------------|
| Media | Collision panel | Panel para pintar tiles de colisión |
| Media | Save/Load JSON | Guardar/cargar mapas editados |
| Media | Limpiar diagnósticos | Remover `LogBrushDiagnostics()` y logs verbose de Light2D una vez estable |
| Baja | Tutorial panel | Panel de ayuda con shortcuts |
| Baja | Brush preview | Re-habilitar preview con material Unlit |
| Baja | Light2D real | Añadir assembly reference a URP para usar Light2D directamente (si se necesita iluminación 2D) |
