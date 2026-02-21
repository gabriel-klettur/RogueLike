# Tiles View Panel Tests

Este directorio contiene tests `pytest` para el módulo `tiles_view_panel`, cubriendo:

## test_tiles_view_state.py
- Verifica valores por defecto de `TilesViewPanelState`:
  - `active` = False
  - `pos` = None
  - `dragging` = False
  - `drag_offset` = (0, 0)
  - `size` = None

## test_tiles_view_controller.py
- Verifica `TilesViewPanelController`:
  - `__init__`: inicializa `editor_controller`, `editor_state`, `state`, `view`.
  - `render(screen, camera, game_map)`: delega a `view.render()`.
  - `drag(pos)`: actualiza `state.pos` restando `drag_offset`.
  - `stop_drag()`: establece `state.dragging` a False.
  - `_tile_under_mouse`: invoca al método de `editor_controller`.

## test_tiles_view_events.py
- Verifica `TilesViewPanelEventHandler`:
  - `handle_event`: delega a `panel.handle_event`, actualiza `state.pos`.
  - Detectores:
    - `_is_right_click_start`, `_is_drag_motion`, `_is_right_click_end`.
  - Drag:
    - `_start_drag`: inicia `dragging` y `drag_offset`.
    - `_perform_drag`: invoca `controller.drag()`.
    - `_stop_drag`: invoca `controller.stop_drag()`.
  - `_get_initial_position`: retorna `state.pos` o fallback `(0,0)`.

## test_tiles_view_view.py
- Verifica `TilesViewPanelView`:
  - `__init__`: crea `panel` e inicializa `pos` si `state.pos`.
  - `_screen_to_world`: convierte coordenadas.
  - `_compute_panel_position`: calcula posición override, top-right o al lado de toolbar.

---

Ejecutar tests de este directorio:
```bash
pytest tests/roguelike_editors/tiles/tiles_view_panel
```
