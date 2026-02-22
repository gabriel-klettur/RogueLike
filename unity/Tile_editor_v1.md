# Tile Editor v1 — Unity Runtime

## Estado actual

El Tile Editor runtime funciona en Unity con toggle F6. La UI replica los paneles del editor Python original:
- Panel izquierdo: toolbar, selector de layer, brush size, preview del tile seleccionado, category tabs, tile picker grid, status bar
- Panel derecho: View panel (hovered/selected/choice tile info), Layers panel (9 layers con visibilidad)
- Indicador de layer activo en la parte inferior central
- Border overlay dorado + label de herramienta activa
- Grid cursor (LineRenderer) que sigue al mouse y muestra el tamaño del brush

## Funcionalidades implementadas

| Feature | Estado | Notas |
|---------|--------|-------|
| Toggle F6 | ✅ | Abre/cierra el editor |
| Toolbar (B/E/F/I/S) | ✅ | Brush, Eraser, Fill, Eyedropper, Select |
| Layer selector (< >) | ✅ | 9 layers, resetea a Ground al abrir |
| Brush size (1-5) | ✅ | Botones -/+ y números directos |
| Tile Picker | ✅ | Grid scrollable con 312 tiles |
| Category tabs | ✅ | Grid 2 columnas, "All" por defecto |
| Selected tile preview | ✅ | Sprite + nombre del tile seleccionado |
| View panel | ✅ | Hovered/Selected/Choice tile info |
| Layers panel | ✅ | 9 layers con nombres |
| Grid cursor | ✅ | LineRenderer dorado, cambia color por tool |
| Border overlay | ✅ | Borde dorado + label "TILE EDITOR — BRUSH" |
| Undo/Redo | ✅ | Ctrl+Z / Ctrl+Shift+Z |
| Brush painting | ⚠️ | **BUG: tiles se pintan en negro** |
| Eraser | ✅ | Funciona correctamente |
| Fill tool | ⚠️ | Mismo bug de renderizado negro |
| Eyedropper | ✅ | Funciona correctamente |
| Collision panel | ❌ | Pendiente |
| Save/Load JSON | ❌ | Pendiente |
| Tutorial panel | ❌ | Pendiente |

## Bug conocido: Tiles se pintan en negro

### Síntoma
Al pintar tiles con el brush, estos aparecen como rectángulos negros sólidos en el tilemap, en lugar de mostrar la textura del sprite.

### Diagnóstico realizado
- `SetTile()` se ejecuta correctamente en el tilemap correcto (Ground, sortLayer=Ground, rendererEnabled=True)
- El tile tiene sprite válido (`sprite=tileset3_r1_c1`, `ppu=32`, `color=RGBA(1,1,1,1)`)
- La textura del sprite es `sactx-0-1024x512-Uncompressed-Atlas_Tiles` — una textura de **SpriteAtlas**
- El tile picker UI muestra los sprites correctamente (los componentes `Image` de Unity resuelven atlas sprites)
- El `TilemapRenderer` NO resuelve los sprites atlas-packed correctamente al colocarlos via `SetTile()` en runtime

### Causa raíz identificada
`Atlas_Tiles.spriteatlas` empaquetaba todos los sprites de `Assets/_Project/Art/Tiles/ready/` en una textura atlas. Cuando se colocan tiles en un `Tilemap` via `SetTile()` en runtime, el `TilemapRenderer` no puede resolver la textura atlas-packed → renderiza negro.

### Intentos de solución

1. **Brush preview deshabilitado** — Se eliminó el `SpriteRenderer` preview que se superponía. No resolvió el problema base.
2. **SpriteAtlasResolver callback** — Se intentó registrar `SpriteAtlasManager.atlasRequested` pero el atlas no está en Resources, así que no se puede cargar en runtime.
3. **Eliminar SpriteAtlas** — Se eliminó `Atlas_Tiles.spriteatlas` y todos los tile assets/catalog para forzar regeneración sin atlas.

### Solución pendiente (próximo paso)
Después de eliminar el SpriteAtlas:
1. Unity debe reimportar los sprites de `ready/` (ahora usarán sus texturas PNG originales)
2. Ejecutar **Valkur > Atlas > Generate Tile Assets** (regenera .asset tiles con sprites sin atlas)
3. Ejecutar **Valkur > Atlas > Generate Tile Catalog (Runtime)** (regenera el catálogo)
4. **Play + F6** — los tiles deberían renderizarse con sus texturas originales

Si esto no funciona, alternativas:
- Crear tiles en runtime con `Texture2D.LoadImage()` desde los PNGs
- Usar `Addressables` para cargar sprites sin atlas
- Investigar si hay un shader/material issue en el `TilemapRenderer` creado en runtime

## Arquitectura

### Archivos principales
```
Scripts/Gameplay/TileEditor/
├── TileEditorManager.cs    — Orquestador principal, input, undo/redo
├── TileEditorState.cs      — Estado mutable (tool, layer, tile, brush size)
├── TileEditorUI.cs         — UI programática (Canvas, paneles, botones)
├── TileEditorBorderOverlay.cs — Borde dorado de pantalla
├── TileEditorGridCursor.cs — Cursor de grid (LineRenderer)
├── TileBrush.cs            — Operaciones de pintado (Paint, Erase, Fill, Pick)
├── TileCatalog.cs          — ScriptableObject con entries de tiles
└── TileRegistry.cs         — Singleton de lookup de tiles

Scripts/Editor/
└── TilePaletteBuilder.cs   — Genera tile assets y catálogo desde sprites

Scripts/Gameplay/Rendering/
├── WorldGridBuilder.cs     — Crea Grid + 9 Tilemaps en runtime
└── TilemapLayerSetup.cs    — Configura sorting layers por tilemap
```

### Flujo de datos
1. `TilePaletteBuilder` (editor) → genera `.asset` tiles desde sprites PNG
2. `TilePaletteBuilder` → genera `TileCatalog.asset` en Resources/
3. `TileEditorManager.Start()` → `Resources.Load("TileCatalog")`
4. `TileEditorUI` → muestra tiles del catálogo en el picker
5. Usuario selecciona tile → `_state.SelectedTile = entry.tile`
6. Usuario pinta → `TileBrush.Paint(tilemap, cellPos, tile, brushSize)`
7. `tilemap.SetTile(pos, tile)` → TilemapRenderer renderiza el tile

### Layers (TilemapLayerSetup.TilemapLayer)
| Index | Nombre | Sorting Layer |
|-------|--------|---------------|
| 0 | Ground | Ground |
| 1 | FloorDecals | FloorDecals |
| 2 | Collision | (renderer disabled) |
| 3 | ObjectsLow | ObjectsLow |
| 4 | WallsBottom | WallsBottom |
| 5 | Decorations | Decorations |
| 6 | WallsTop | WallsTop |
| 7 | ObjectsHigh | ObjectsHigh |
| 8 | OverheadDetails | Overhead |
