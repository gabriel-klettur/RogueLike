# Tiles Title Panel Tests

Este directorio contiene los tests `pytest` para el módulo `tiles_title`:

## test_tiles_tiles_state.py
- Verifica valores por defecto de `TilesTitleState`:
  - `title` inicial vacío y actualización.

## test_tiles_tiles_events.py
- Verifica `TilesTitleEventHandler`:
  - La inicialización asigna `controller` y `state`.
  - `handle_event` devuelve `None` para eventos desconocidos.

## test_tiles_tiles_controller.py
- Verifica `TilesTitleController`:
  - Propiedades `editor_state` y `state` en init.
  - `view` es instancia de `TilesTilesView`.
  - `render` delega en `view.render`.

## test_tiles_tiles_view.py
- Verifica `TilesTilesView.render`:
  - Dibuja fondo semi-transparente y texto blanco.
  - Usa título por defecto "TILES EDITOR" o personalizado.

---

Ejecutar con:
```bash
pytest tests/roguelike_editors/tiles/tiles_title
```
