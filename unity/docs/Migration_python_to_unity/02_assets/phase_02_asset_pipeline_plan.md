# Phase 02 Asset Pipeline Plan (Tilemap + Content Authoring)

**Document type:** Phase plan (assets)  
**Last updated:** 2026-02-23  
**Status:** In progress (phase partially complete)

Technical planning document for Phase 02. It captures architecture analysis, implementation options, and the recommended production path for assets and tilemap workflows.

---

## 1. Diagnostico: Como funciona en Python

### 1.1 Estructura de datos (JSON-driven)

El proyecto Python tiene una arquitectura **100% data-driven** donde todo el contenido del juego
se define en archivos JSON bajo `python/data/`:

| Dominio | Archivo(s) | Contenido |
|---------|-----------|-----------|
| **Tiles** | `data/tiles/tiles.json` | Registry de ~500 tile keys -> paths relativos |
| **Monstruos** | `data/entities/new_hostiles.json` | Clases con stats, FSM set, assets (sprite sets con scales/tints) |
| **Neutrales** | `data/entities/new_neutrals.json` | Vendors, NPCs amigables |
| **Jugadores** | `data/entities/new_players.json` | Clases jugables con stats y sprite sets |
| **Spells** | `data/spells/spells.json` | Definiciones completas: tipo, timings, VFX, damage |
| **Spawners** | `data/spawners/spawners_templates.json` | Templates de spawner con waves y triggers |
| **Mundos** | `data/worlds/{world}/zones/` | Zonas con offsets, overlays (tile grids por capa), collisions |
| **Items** | `data/inventory/` | ~288 items con iconos |

### 1.2 Estructura de assets (sprites, audio)

```
python/assets/
  audio/          -> ambient/, music/, sfx/
  buildings/      -> 17 building sprites
  characters/     -> Barbarian/, dwarf/, elven/, mague/, valkyrie/ (sprite sheets)
  tiles/          -> dungeon_*.png, floor_*.png, multi_tiles/ (tilesets cortados)
  explosions/     -> VFX sprites
  ui/             -> UI elements
```

### 1.3 Sistema de Tiles en Python

**Critico entender esto:** Python NO usa un tilemap engine. Usa un sistema custom donde:

1. **Tilesets grandes** (ej: `floor_1.png` = 2.7MB) se cortan en runtime en tiles individuales
2. **`tiles.json`** es un registry plano: `"floor" -> "floor"`, `"multi_tiles/tiles/grass/multi_141" -> ...`
3. **Overlays** (`lobby.overlay.json`) almacenan grids 2D por capa (`Ground`, `Walls`, etc.) donde cada celda es un tile key string
4. **Collisions** (`lobby.json`) son grids 2D de `"#"` (wall) y `"."` (walkable)
5. El **Tile Editor** (F4) permite pintar tiles en el mapa en runtime, modificando los overlays

### 1.4 Editores en Python (runtime, in-game)

Python tiene **5 editores runtime** accesibles con hotkeys durante el juego:

| Editor | Hotkey | Arquitectura | Funcion |
|--------|--------|-------------|---------|
| **Tiles Editor** | F4 | MVC (State/Controller/View/Events) | Pintar tiles por capa, picker, brush sizes |
| **Entities Editor** | F6 | MVC completo con subpaneles | Crear/editar/spawn monstruos, editar stats, assets |
| **Map Editor** | F7 | MVC con camera persistence | Editar zonas, offsets, layout del mundo |
| **Spawner Editor** | F8 | MVC con templates/instances | Crear spawner templates, colocar instancias |
| **Spells Editor** | -- | MVC | Editar definiciones de spells |

Todos siguen el patron: **Model -> Controller -> View -> EventHandler**, con persistencia a JSON.

---

## 2. Pregunta clave: Tilemap de Unity o assets sueltos?

### 2.1 Tu pregunta es excelente y la respuesta es: **SI, necesitas Tilemap, pero no como crees**

Tener los 600+ sprites sueltos en `Art/Tiles/` sin un Tilemap es como tener ladrillos sin cemento.
No sirven para nada asi. Necesitas un sistema que los organice.

### 2.2 Comparativa honesta

| Aspecto | Assets sueltos (actual) | Unity Tilemap | SO Tile Registry |
|---------|------------------------|---------------|-------------------------------|
| **Rendimiento** | Cada tile = 1 draw call | Batching automatico | Si usa Tilemap internamente |
| **Edicion visual** | Imposible | Tile Palette nativo | Custom editor + Tilemap |
| **Colisiones** | Manual | TilemapCollider2D | TilemapCollider2D |
| **Escalabilidad** | No escala | Chunks, streaming | Chunks, streaming |
| **Paridad con Python** | No hay equivalente | Parcial | Total (overlay -> tilemap) |
| **Editor custom** | N/A | Tile Palette limitado | EditorWindow custom |
| **Creacion runtime** | N/A | SetTile() API | SetTile() + registry |

### 2.3 Mi recomendacion como experto Unity

**Opcion recomendada: Hybrid Tilemap + ScriptableObject Registry**

La razon:

1. **Unity Tilemap** te da rendering batched, colisiones automaticas, y una API (`SetTile`) que mapea
   perfectamente al sistema de overlays de Python
2. **ScriptableObject Registry** (`TileDefinition`) te da la capa de datos que Python tiene en `tiles.json`:
   metadata, categorias, tags, collision type — todo editable desde el Inspector
3. **Custom EditorWindow** (Tile Palette extendido) te permite crear un editor tan potente como el de Python
   pero con las ventajas del Unity Editor (undo, multi-select, preview)

**NO recomiendo:**
- Solo Tilemap nativo sin registry -> pierdes la capa de datos que Python tiene
- Solo assets sueltos -> pierdes rendimiento y editabilidad
- Addressables para tiles -> overengineering para este scope

---

## 3. Arquitectura propuesta para Fase 2

### 3.1 Capa de datos: Asset Registry (ScriptableObjects)

```
Assets/_Project/Data/
  Catalogs/
    Monsters/          <- YA EXISTE (24 MonsterDefinition)
    Players/           <- YA EXISTE (10 PlayerDefinition)
    Spells/            <- YA EXISTE (76 SpellDefinition)
    Items/             <- NUEVO: ItemDefinition con iconos, stats, categorias
    Tiles/             <- NUEVO: TileDefinition con sprite ref, collision type, layer
  TilePalettes/        <- NUEVO: Unity Tile assets para Tilemap
  Worlds/              <- NUEVO: WorldDefinition + ZoneDefinition (overlay data)
```

### 3.2 Nuevos ScriptableObjects

#### TileDefinition.cs
```csharp
[CreateAssetMenu(menuName = "Valkur/Data/Tile Definition")]
public class TileDefinition : ScriptableObject
{
    public string tileKey;           // "floor", "dungeon_1", etc.
    public Sprite sprite;            // Referencia directa al sprite
    public TileCategory category;    // Ground, Wall, Decoration, Water, etc.
    public CollisionType collision;  // None, Solid, Trigger
    public string[] tags;            // "indoor", "dungeon", "grass"
    public TileBase unityTile;       // Tile asset para Unity Tilemap (auto-generado)
}
```

#### WorldDefinition.cs
```csharp
[CreateAssetMenu(menuName = "Valkur/Data/World Definition")]
public class WorldDefinition : ScriptableObject
{
    public string worldKey;          // "base", "chaos_world"
    public ZoneDefinition[] zones;
}
```

#### ZoneDefinition.cs
```csharp
[CreateAssetMenu(menuName = "Valkur/Data/Zone Definition")]
public class ZoneDefinition : ScriptableObject
{
    public string zoneName;          // "lobby", "Forest"
    public Vector2Int offset;        // (50, 50) — offset en tiles
    public Vector2Int size;          // (50, 50) — tamano en tiles
    public string musicKey;          // Audio clip key
    public TextAsset overlayJson;    // Referencia al overlay JSON (o datos inline)
    public TextAsset collisionJson;  // Referencia al collision grid
}
```

### 3.3 Pipeline de importacion de Tiles

El problema actual: los tiles en `Art/Tiles/` son **tilesets completos** (imagenes grandes de 1-3MB).
Python los corta en runtime. Unity necesita que esten **pre-cortados como sprites individuales**.

**Pipeline propuesto:**

```
1. Tileset PNG (floor_1.png, 2.7MB)
     | [Sprite Editor: Slice -> Grid by Cell Size]
     v
2. Sprites individuales (floor_1_0, floor_1_1, ...)
     | [TileImporter EditorWindow: auto-genera TileDefinition + Tile asset]
     v
3. TileDefinition ScriptableObject + Unity Tile asset
     | [TilePalette: agrupa tiles por categoria para pintar]
     v
4. Tilemap en escena (pintado manual o cargado desde overlay JSON)
```

### 3.4 Conversion de Overlays Python -> Unity Tilemap

```
Python overlay JSON:
{
  "layers": {
    "Ground": [["floor", "floor", "dungeon_1", ...], ...],
    "Walls":  [["#", "", "#", ...], ...]
  }
}

-> Unity: WorldLoader lee overlay JSON
  -> Para cada capa, SetTile(position, tileRegistry[tileKey].unityTile)
  -> Resultado: Tilemap poblado programaticamente
```

### 3.5 Sprite Atlasing

| Grupo | Contenido | Razon |
|-------|-----------|-------|
| `Atlas_Tiles_Ground` | Todos los floor/grass tiles | Batch rendering de suelo |
| `Atlas_Tiles_Dungeon` | Dungeon tiles | Batch rendering de dungeon |
| `Atlas_Tiles_Walls` | Wall tiles | Batch rendering de paredes |
| `Atlas_Characters` | Player + NPC sprites | Batch rendering de entidades |
| `Atlas_UI` | Iconos, barras, botones | Batch rendering de UI |
| `Atlas_VFX` | Particulas, explosiones | Batch rendering de efectos |

---

## 4. Vision a futuro: Editor in-game

Tu objetivo final es tener editores in-game como en Python. Esto es perfectamente viable en Unity,
pero hay que elegir bien **que va en el Unity Editor y que va in-game**.

### 4.1 Estrategia de editores

| Funcionalidad | Python | Unity: Editor | Unity: Runtime |
|--------------|--------|--------------|----------------|
| **Pintar tiles** | Tile Editor (F4) | Tile Palette (nativo) | Runtime Tile Painter |
| **Crear monstruos** | Entities Editor (F6) | Inspector + SO | Runtime Entity Creator |
| **Editar stats** | Properties Panel | Inspector | Runtime Stats Editor |
| **Colocar spawners** | Spawner Editor (F8) | Custom EditorWindow | Runtime Spawner Placer |
| **Editar spells** | Spells Editor | Inspector + SO | Runtime Spell Editor |
| **Editar mapa/zonas** | Map Editor (F7) | Scene View | Runtime Zone Editor |
| **Asignar assets** | Picker panels | Drag and drop Inspector | Runtime Asset Picker |

### 4.2 Fase 2 NO incluye los editores runtime

Los editores runtime son Fase 6+ (herramientas). Fase 2 se enfoca en:
1. **Tener todos los assets correctamente importados y organizados**
2. **Tener un registry de ScriptableObjects que mapee 1:1 con Python**
3. **Tener el pipeline de Tilemap funcionando** (overlay -> tilemap)
4. **Tener las politicas de importacion definidas y automatizadas**

Los editores runtime se construiran SOBRE esta base.

---

## 5. Plan de implementacion detallado

### Paso 14: Asset Map maestro

Crear `asset_map.json` con inventario completo de assets Python -> Unity:
- Recorrer `python/assets/` y `python/data/` programaticamente
- Generar JSON con: source_path, target_path, asset_type, migration_status
- Esto es la fuente de verdad para toda la migracion

### Paso 15: Convencion de nombres

| Categoria | Patron | Ejemplo |
|-----------|--------|---------|
| Tiles | `tile_{tileset}_{index}` | `tile_floor_001`, `tile_dungeon_003` |
| Monsters | `monster_{key}` | `monster_barbol`, `monster_skeleton` |
| Players | `player_{class}` | `player_valkyrie`, `player_barbarian` |
| Spells | `spell_{key}` | `spell_fireball`, `spell_slash` |
| Items | `item_{id}` | `item_health_potion`, `item_sword_iron` |
| Audio SFX | `sfx_{category}_{name}` | `sfx_combat_hit`, `sfx_ui_click` |
| Audio Music | `music_{zone}_{name}` | `music_dungeon_ambient` |
| Prefabs | `pfb_{type}_{name}` | `pfb_monster_barbol`, `pfb_projectile_fireball` |

### Paso 16: Politica de pivots

| Categoria | Pivot | Razon |
|-----------|-------|-------|
| Tiles | Center (0.5, 0.5) | Tilemap espera center pivot |
| Characters | Bottom-Center (0.5, 0) | Pies en el suelo, Y-sort correcto |
| Props/Buildings | Bottom-Center (0.5, 0) | Consistente con characters |
| UI | Center (0.5, 0.5) | Layout system espera center |
| VFX/Particles | Center (0.5, 0.5) | Emision desde centro |
| Projectiles | Center (0.5, 0.5) | Rotacion desde centro |

### Paso 17: Politica PPU (Pixels Per Unit)

**Confirmado:** `TILE_SIZE = 32` en Python (`config_tiles.py`). Zonas = 50x50 tiles.

| Categoria | PPU | Razon |
|-----------|-----|-------|
| Tiles | 32 | TILE_SIZE=32px -> 1 tile = 1 Unity unit |
| Characters | 32 | Consistente con tiles, scale via transform |
| UI | 100 | Unity UI default, alta resolucion |
| VFX | 32 | Consistente con world space |

### Paso 18: SpriteAtlas groups

Crear 6 SpriteAtlas assets (ver seccion 3.5). Configurar:
- Max Texture Size: 2048 (4096 para tiles si necesario)
- Format: RGBA32 (no compression para pixel art)
- Filter Mode: Point (no filtering)
- Padding: 2px (evitar bleeding)

### Paso 19: Ya implementado (ValkurAssetPostprocessor)

### Paso 20-21: Migracion incremental

1. Slice tilesets en Sprite Editor (o script automatico)
2. Generar TileDefinition ScriptableObjects via migrador
3. Crear Tile assets para Unity Tilemap
4. Poblar un Tilemap de prueba con overlay data de "lobby"
5. Validar visualmente: pivots, escala, sorting, colisiones

### Paso 22: Migracion completa

- Ejecutar pipeline completo para todos los tilesets
- Generar TileDefinitions para los ~500 tile keys
- Convertir overlays de todas las zonas del mundo "base"
- Validar con ContentValidator extendido

---

## 6. Preguntas abiertas para discusion

### 6.1 Tamano real de tiles? -- RESUELTO

**Confirmado:** `TILE_SIZE = 32` en `config_tiles.py`. Zonas = 50x50 tiles (`zone_width=50, zone_height=50`).
`RENDERED_SPRITE_SIZE = [64, 64]` para entidades, `ORIGINAL_SPRITE_SIZE = [128, 128]` para sprite sheets.
PPU debe ser **32** para tiles, y los sprites de entidades se escalan via transform.

### 6.2 Prioridad de mundos?

Solo el mundo "base" tiene overlays completos (9 zonas con overlay).
`chaos_world` tiene 6 items, `order_world` tiene 1.
**Propuesta:** Migrar solo "base" en Fase 2. Los demas mundos se migran cuando se creen editores runtime.

### 6.3 Items inventory?

Hay 288 archivos en `data/inventory/`. Necesitan ItemDefinition SOs?
**Pendiente tu confirmacion.**

### 6.4 Runtime tile editing es requisito para Fase 2?

**Propuesta:** NO. Fase 2 = pipeline + data foundation. Los editores runtime (tile painter, entity creator,
spawner placer) se construyen en una fase posterior, SOBRE la base de ScriptableObjects y Tilemap
que Fase 2 establece.

---

## 7. Resumen ejecutivo

| Decision | Eleccion | Justificacion |
|----------|----------|---------------|
| **Tilemap** | Si, Unity Tilemap + TileDefinition SO | Rendimiento, colisiones, editabilidad |
| **Registry** | ScriptableObject por cada tipo de contenido | Paridad con Python JSON, editable en Inspector |
| **Tilesets** | Pre-slice en import, no runtime | Rendimiento, compatible con Tile Palette |
| **Overlays** | Convertir JSON -> Tilemap programatico | WorldLoader lee overlay y puebla Tilemap |
| **Atlasing** | 6 SpriteAtlas por dominio | Batch rendering, memoria controlada |
| **Editores** | Fase 2 = pipeline + data; Fase 6+ = editores runtime | Fundacion primero, herramientas despues |
| **Asset Map** | JSON generado programaticamente | Trazabilidad completa Python -> Unity |

---

*Professionalized reference document. Keep this plan synchronized with `../01_execution/roadmap_50_steps.md` before closing Phase 02.*