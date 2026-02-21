**Especificaciones para la implementación de anclaje de edificios por zonas**

---

## 1. Propósito

Definir en detalle la solución para:

* Almacenar posiciones de edificios relativas a la zona donde se insertan (lobby, dungeon, ciudad...).
* Conservar compatibilidad con el sistema actual de posiciones absolutas.
* Permitir recalibración automática de edificios al cambiar `GLOBAL_WIDTH`/`GLOBAL_HEIGHT`.

## 2. Contexto

Actualmente:

* Los edificios se guardan con coordenadas absolutas en píxeles (`x`,`y`).
* Cada zona (`lobby`, `dungeon`) es de 50×50 tiles.
* Cambiar `GLOBAL_WIDTH`/`GLOBAL_HEIGHT` descoloca overlays y edificios.

Nuevo requisito: anclar edificios a la zona donde se insertan.

## 3. Requisitos funcionales

1. **Almacenamiento**: guardar para cada edificio:

   * `zone`: cadena (p.ej. "lobby", "dungeon", "city", ...).
   * `rel_tile_x`, `rel_tile_y`: posición en tiles relativa al origen de la zona.
   * (Opcional) mantener `x`, `y` absolutos para compatibilidad.
2. **Carga**:

   * Si existen `zone` y `rel_tile_*`, reconstruir posición en tiles:

     ```python
     ox, oy = zone_offsets[zone]
     b.tile_x = ox + entry["rel_tile_x"]
     b.tile_y = oy + entry["rel_tile_y"]
     b.x = b.tile_x * TILE_SIZE
     b.y = b.tile_y * TILE_SIZE
     ```
   * Si faltan datos relativos, usar modo "legacy" con `x`,`y`.
3. **Editor de edificios**:

   * Al arrastrar, detectar zona actual y calcular `rel_tile_*`.
   * Pasar `zone` y `zone_offset` a `save_buildings_to_json`.
4. **Recalibración en tiempo de ejecución**:

   * Detectar cambios en `GLOBAL_WIDTH`/`GLOBAL_HEIGHT`.
   * Proveer método `recalibrate_buildings(zone_offsets)` para volver a computar posiciones.
   * Opción de enganche automático vía setter de configuraciones.
5. **Extensibilidad**:

   * Facilitar incorporación de nuevas zonas (`city`, `desert`, `island`, ...).

## 4. Diseño del formato JSON

```jsonc
[
  {
    "zone": "lobby",
    "rel_tile_x": 12,
    "rel_tile_y": 34,
    "x": 960,              // opcional
    "y": 2720,             // opcional
    "image_path": "...",
    "solid": true,
    "scale": [w, h],
    "original_scale": [w0, h0],
    "split_ratio": 0.5,
    "z_bottom": 1,
    "z_top": 4,
    // campos legacy: pueden omitirse en nuevas entradas
  }
]
```

## 5. Proceso de carga

- **Firma modificada** de `load_buildings_from_json`:
  ```python
  def load_buildings_from_json(filepath, building_class, z_state=None, zone_offsets: Dict[str, Tuple[int,int]] = None):
      # .. cargar JSON ..
      for entry in data:
          zone = entry.get("zone")
          if zone and zone_offsets and "rel_tile_x" in entry:
              ox, oy = zone_offsets[zone]
              b.tile_x = ox + entry["rel_tile_x"]
              b.tile_y = oy + entry["rel_tile_y"]
              b.x, b.y = b.tile_x * TILE_SIZE, b.tile_y * TILE_SIZE
          else:
              # legacy: usar x,y absolutos
````

## 6. Recalibración en tiempo de ejecución

* **Método público** en gestor de edificios (o Game):

  ```python
  def recalibrate_buildings(self, zone_offsets: Dict[str, Tuple[int,int]]):
      for b in self.buildings:
          zone = b.meta["zone"]
          rel_x = b.meta["rel_tile_x"]
          rel_y = b.meta["rel_tile_y"]
          ox, oy = zone_offsets[zone]
          b.tile_x = ox + rel_x
          b.tile_y = oy + rel_y
          b.update_pixel_position()
  ```
* **Invocación**:

  * Opción A: automáticamente desde el setter de `GLOBAL_WIDTH/HEIGHT`.
  * Opción B: manual tras cambios de configuración.

\## 7. Guardado desde el editor

* **Guardar edificios** debe recibir:

  * `zone` y `zone_offset` actual.
  * Calcular `rel_tile_x = b.tile_x - ox`, `rel_tile_y = b.tile_y - oy`.
* **Firma** propuesta:

  ```python
  def save_buildings_to_json(buildings, filepath=None, z_state=None,
                             zone: str = None, zone_offset: Tuple[int,int] = None):
      for b in buildings:
          data = { ... }
          if zone and zone_offset:
              data.update({
                  "zone": zone,
                  "rel_tile_x": b.tile_x - zone_offset[0],
                  "rel_tile_y": b.tile_y - zone_offset[1],
              })
  ```

## 8. Extensibilidad y futuras zonas

* Mantener un diccionario global `ZONE_OFFSETS = { "lobby": (0,0), "dungeon": (50,0), "city": (0,50), ... }`.
* Agregar nuevas claves sin cambiar lógica central.

## 9. Plan de implementación

1. Actualizar modelo JSON en `json_handler.py`.
2. Modificar `load_buildings_from_json` (y tests).
3. Implementar `recalibrate_buildings`.
4. Ajustar Editor para pasar `zone`/`zone_offset` al guardar.
5. Documentar y migrar datos existentes.

## 10. Posibles mejoras

* UI en editor para seleccionar zona antes de arrastrar.
* Soporte de anidación (zonas dentro de áreas más grandes).
* Validación de límites: impedir `rel_tile_*` fuera de \[0,50).

---

*Fin de las especificaciones.*
