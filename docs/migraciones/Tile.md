## Para soportar varias capas de overlay—por ejemplo suelos, muros bajos, muros altos, decorados, colisión—tendremos que evolucionar este modelo hacia un tiles[layer][row][col], en lugar de un solo tiles[row][col].

## Plan de alto nivel para migrar vuestro sistema de tiles a uno basado en capas (“Ground”, “Floor Decals”, “Collision”, “Objects Low”, “Walls Bottom”, “Decorations”, “Walls Top”, “Objects High”, “Overhead Details”):

## Fase 0 – Análisis y definición

### 1. Inventario de estructuras de datos actuales  
- **JSON de overlays**  
  – Ruta: `data/zones/overlays/*.overlay.json`  
  – Formato: matriz 2D de strings (cada string = código de tile, p. ej. `"abc"`)  
- **Modelo en código**  
  – [Map](cci:2://file:///c:/proyects/RogueLike/src/roguelike_engine/map/model/map_model.py:6:0-15:115) ([map/model/map_model.py](cci:7://file:///c:/proyects/RogueLike/src/roguelike_engine/map/model/map_model.py:0:0-0:0)):  
    • `matrix: List[str]` – diseño estático (“########”, “#…#”, …)  
    • `tiles: List[List[Tile]]` – objetos `Tile` por celda  
    • `overlay: Optional[List[List[str]]]` – capa única de overlay (actual)  
- **Loader de mapas**  
  – [TextMapLoader](cci:2://file:///c:/proyects/RogueLike/src/roguelike_engine/map/model/loader/text_loader_strategy.py:11:0-67:37) ([map/model/loader/text_loader_strategy.py](cci:7://file:///c:/proyects/RogueLike/src/roguelike_engine/map/model/loader/text_loader_strategy.py:0:0-0:0)) lee matrix + overlay, hace fallback al antiguo y ajusta dimensiones.  
- **Renderizado**  
  – `map/view/*` dibuja en un único loop todos los `tiles[row][col]`.  
- **Colisión & eventos**  
  – `map/controller` y `map/events` usan lógica fija sobre overlay único o sobre `tiles`.  
- **Serialización / guardado**  
  – Saver recurre a `save_overlay` en el mismo JSON de overlay.

---

### 2. Puntos críticos a reforzar  
1. **Renderizado**  
   - Pasar de un solo loop a iterar por _capa_ y ajustar Z-ordering / blending.  
2. **Colisiones**  
   - Extraer lógica de “overlay único” y apuntar a la futura capa `Collision`.  
3. **Editor de mapas**  
   - UI actual no permite seleccionar capa → habrá que añadir selector de capa y validaciones.  
4. **Compatibilidad JSON**  
   - Loader/saver debe seguir soportando el viejo formato y validar dimensiones por capa.

---

### Fase 1 – Modelo de datos y formato

#### Estructura interna  
- Definir `enum Layer { Ground = 0, FloorDecals = 1, Collision = 2, ObjectsLow = 3, WallsBottom = 4, Decorations = 5, WallsTop = 6, ObjectsHigh = 7, OverheadDetails = 8 }`.  
- Cambiar modelo interno (`Map` o `TileMap`) para almacenar:  
  - `matrix: List[str]`  
  - `layers: Dict[Layer, List[List[str]]]`  
  - `tiles_by_layer: Dict[Layer, List[List[Tile]]]`  
  - `metadata: Dict`  
  - `name: str`  
  - Campos legacy: `overlay: List[List[str]]` y `tiles: List[List[Tile]]` calculados automáticamente en `__post_init__` usando `layers[Ground]`.

#### Formato de archivo  
- JSON de zonas con múltiples capas:  
```json
{
  "layers": {
    "Ground": [[…],[…]],
    "FloorDecals": [[…],…],
    …
  }
}
```
- Backward compatibility: si no existe `layers`, el loader trata el array antiguo como `layers.Ground`.  
- Validar que todas las capas tengan dimensiones `width x height`.  
- Tras ajustar dimensiones, el loader mezcla en `layers[Ground]` los overlays de cada zona (`global_map_settings.zone_offsets`).

#### Serialización  
- Usar `save_layers(mapName, layersDict)` para guardar múltiples capas.  
- `save_overlay` queda deprecado como compatibilidad con el formato antiguo.  

### Fase 2 – Deserialización y API interna
- **Parser de JSON**  
  - Leer el bloque `layers` del JSON, manteniendo compatibilidad con el formato legacy (`overlay`).  
  - Validar que todas las matrices de `layers` tengan dimensiones `width x height`.  
  - Inicializar `layers[Ground]` con strings vacíos si faltan datos.  
- **API de Map**  
  - Constructor recibe `matrix`, `layers`, `tiles_by_layer`, `metadata`, `name` y calcula legacy `overlay` y `tiles` en `__post_init__`.  
  - Métodos públicos:  
    - `get_tile(layer: Layer, x: int, y: int) -> Optional[Tile]`  
    - `set_tile(layer: Layer, x: int, y: int, code: str) -> None`  
    - `get_layer(layer: Layer) -> List[List[str]]`  
    - `get_tiles_for_layer(layer: Layer) -> List[List[Tile]]`  
  - Adaptar lógica de colisión, pathfinding y eventos para usar `Layer.Collision` en lugar de overlay único.

### Fase 3 – Renderizado por capas
Pipeline de dibujado
• Reemplazar render loop único por:
python
CopyInsert
for layer in [Ground, FloorDecals, ObjectsLow, …, OverheadDetails]:
    drawLayer(layer)
• Ajustar Z-ordering, blending y opacidades especiales (decals/overhead).
Optimización
• Agrupar sprites por tileset/layer para minimizar draw calls.

### Fase 4 – Herramientas y editor de mapas
Editor
• Añadir selector de capa en UI.
• Permitir pintar/seleccionar tiles en capa activa.
Validaciones
• Inhabilitar dibujo en capas no permitidas (por ejemplo, Collision sólo cierto brush).
• Previsualizar orden real de pintado.

### Fase 5 – Script de migración de datos
Conversión automática
• Escribir script (Python/JS) que tome JSON antiguo y genere la estructura por capas.
• Mapear campos viejos a capa “Ground” u “Overlay” según convenga.
Pruebas en lote
• Ejecutar contra todos los .overlay.json.
• Reportar errores de formato o inconsistencias.

### Fase 6 – Integración y pruebas
Pruebas unitarias
• Test de parsing, de getTile/setTile, del render por capas.
Pruebas visuales
• Comparar capturas antes/después.
• Validar colisiones y eventos sobre la nueva capa de colisión.
QA y validación final
• Revisión de performance (FPS, uso de memoria).
• Ajustes de orden y blending si es necesario.

### Fase 7 – Documentación y despliegue
Actualizar README / docs
• Explicar nuevo formato JSON y API de capas.
• Guía de migración y uso del editor.
Comunicación
• Informar al equipo de desarrolladores y diseñadores de niveles.
• Coordinar despliegue en staging y posterior lanzamiento.
Este plan te permite avanzar de forma incremental, manteniendo compatibilidad mientras introduces la abstracción de capas, y cubriendo todas las piezas del pipeline (datos, render, herramientas y QA). Si quieres afinar detalles o calendarizar, dime y lo pulimos.
