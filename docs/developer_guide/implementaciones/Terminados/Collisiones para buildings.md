# Plan alto nivel – “Collision Brush” para edificios

## 1. Definir formato JSON  ✅ Completado
- Ruta/fichero: `data/buildings/buildings_collisions_data.json`  
- Esquema:
  ```json
  {
    "<building_id>": {
      "width": <ancho_en_tiles>,
      "height": <alto_en_tiles>,
      "collision": [
        ["#", ".", "#", "#", "."],
        ["#", ".", ".", "#", "."],
        …
      ]
    },
    …
  }
Usa "#" para sólido y "." para transitable.

## 2. Carga al iniciar  ✅ Completado
En el gestor de edificios (BuildingManager o similar) al cargar la lista de edificios:
Leer (o crear si no existe) el JSON completo.
Para cada building_id, inyectar collision_map: list[list[str]] en el objeto edificio.

## 3. Modelo y estado  ✅ Completado
En BuildingsEditorModel:
Añadir flags idénticos al tile‐editor:
collision_picker_open, collision_choice, collision_picker_rects, collision_picker_pos, dragging, etc.
Cada objeto edificio en memoria llevará su propia collision_map.

## 4. Vista  ✅ Completado
Reutilizar o adaptar _render_collision_picker de TileEditorView:
Generar picker con dos iconos ("#" vs ".").
En el render del edificio:
Dibujar un overlay semitransparente rojo/gris sobre cada tile del sprite según collision_map[y][x].

## 5. Eventos  ✅ Completado
En BuildingEditorEventHandler:
Interceptar clicks/drag para abrir/cerrar el collision picker.
Detectar click en picker y alternar collision_map[row][col].
Gestionar drag para pintar en arrastre.

## 6. Controlador  ⚠️ Lógica integrada en EventHandler
En BuildingEditorController:
Métodos start_collision_brush(), apply_collision_brush(mouse_pos) y flush_collision_brush().
En flush, volcar sólo los cambios al JSON global buildings_collisions_data.json.

## 7. Botón en toolbar  ❌ Pendiente (aún falta icono de colisión)
Añadir icono “collision brush” en la barra de herramientas del editor de edificios.
Toggle open/close igual al tile‐editor.

## 8. Persistencia  ✅ Completado
Un único save al finalizar el stroke de brush o al cerrar el editor:
python
CopyInsert
save_buildings_collisions(data_dir/"buildings_collisions_data.json", all_maps)

## 9. Tests unitarios  ⚠️ Manual OK, automatizados pendientes
- Prueba manual: al chocar con '#' de edificios, el jugador se detiene correctamente. ✅
- Tests pendientes para:
  - Carga/escritura de buildings_collisions_data.json
  - Aplicación de `collision_map` al inicializar buildings
  - `PlayerMovement.move` y `update_dash` con colisiones de buildings

## 10. Documentación  ✅ Completado
Actualizar README/wiki con:
Formato JSON.
Uso del “collision brush” en el editor de edificios.

---

## Actualización 2025-08: Split de colisiones (by_image / by_spawn_id / by_building_instance_id)

Para mejorar organización y evitar mezclas de ámbitos, las colisiones de edificios se separaron en tres ficheros bajo `data/buildings/`:

- `buildings_collisions_by_image.json` (CG, global por `image_path`)
- `buildings_collisions_by_spawn_id.json` (legado por `spawn_id`)
- `buildings_collisions_by_building_instance_id.json` (CU, por instancia/`id` de building)

Ejemplos de formato:

1) Global por imagen (CG):
```json
{
  "/virtual/dummy.png": {
    "width": 2,
    "height": 2,
    "collision": [["#", "."], [".", "#"]]
  }
}
```

2) Por instancia (CU):
```json
{
  "123": {
    "width": 2,
    "height": 2,
    "collision": [[".", "."], ["#", "#"]]
  }
}
```

### Prioridad de lectura (loader)
- Si `collider_scope == "CU"` y existe entrada en `by_building_instance_id`, se usa esa.
- En caso contrario, si existe entrada en `by_image` por `image_path` (o `assets.idle`), se usa esa.
- Como fallback de compatibilidad, si faltan los split, se intenta leer el combinado legado `buildings_collisions_data.json`.

### Escritura (saver)
- Si el UI está en modo CG, la persistencia salva en `by_image`.
- Si el UI está en modo CU, salva en `by_building_instance_id`.
- Visuales de spawner (marcados como `_is_spawner_visual`) se ignoran al guardar CG.

### Migración
Se añadió `scripts/split_buildings_collisions.py` que lee el combinado legado (si existe) y vuelca/mergea en los tres ficheros split. No elimina el combinado.

### Tests
Se añadieron pruebas que validan la lectura por prioridad y la escritura por scope (CG/CU), junto con el comportamiento de ignorar visuales de spawner.