# Tile Toolbar Panel Tests

Este directorio contiene tests `pytest` para el módulo `tiles_toolbar_panel`, cubriendo:

## test_tile_toolbar_state.py
- Verifica valores por defecto de `TileToolbarState`:
  - `view_active` = True
  - `layers_view_open` = False
  - `visible_layers` incluye todas las capas con `True`
  - `show_buildings` = True
  - `show_collisions` = False
  - `show_collisions_overlay` = False
  - `collision_picker_open` = False
  - `collision_choice` = None
  - `pos` = None
  - `dragging` = False
  - `drag_offset` = (0, 0)
  - `btn_delete_rect` y `btn_default_rect` = None

## test_tile_toolbar_controller.py
- Verifica `TileToolbarController`:
  - `__init__`: inicializa `editor_state`, `icons`, `view`, `icon_rects`.
  - `select_tile(tool)`: actualiza `editor_state.current_choice` y `editor_state.current_tool`.
  - `drag(pos)`: ajusta `toolbar_state.pos` usando `drag_offset`.
  - `stop_drag()`: desactiva `toolbar_state.dragging`.

## test_tile_toolbar_events.py
- Verifica `TileToolbarEventHandler`:
  - `handle_click`:
    - Ignora clicks fuera de `icon_rects` o no botón izquierdo.
    - `delete`: invoca `delete_tile`.
    - `default`: invoca `set_default`.
    - `view`: alterna `toolbar_state.view_active`.
    - `view_layers`: alterna `toolbar_state.layers_view_open`.
    - `view_collisions`: alterna `show_collisions`, abre `collision_picker_open` y setea `current_tool` a "brush".
    - `select`: cierra `picker_state.open` y setea `current_tool` a "select".
  - `handle_event`:
    - Drag de panel (MOUSEBUTTONDOWN botón 3, MOUSEMOTION, MOUSEBUTTONUP) llama a `ctrl.drag` y `ctrl.stop_drag`.

## test_tile_toolbar_view.py
- Verifica `TileToolbarView`:
  - `__init__`: crea `panel` y `buttons` correctamente.
  - `_compute_icon_rect()`: calcula el rect de cada icono.
  - `_get_panel_position()`: retorna `toolbar_state.pos` o fallback `(x, y)`.
  - `render()`: dibuja panel e iconos, puebla `icon_rects` y blitea el panel.

---

Ejecutar tests:
```bash
pytest tests/roguelike_editors/tiles/tiles_toolbar_panel
```
