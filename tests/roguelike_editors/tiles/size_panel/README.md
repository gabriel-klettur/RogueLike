# Test Suite for Tiles Editor Size Panel Module

Este directorio contiene tests de pytest para los componentes del panel de tamaño del editor de tiles.

## test_size_panel_state.py
Pruebas para la clase `SizePanelState`:

- `test_default_state`: Valida atributos iniciales (`sizes`, `selected_index`, `selected_size`, `visible`, `option_rects`, `pos`, `dragging`, `drag_offset`).
- `test_select_valid_index`: Selección correcta de un índice válido actualiza `selected_index`.
- `test_select_invalid_index`: Índices negativos o fuera de rango no modifican `selected_index`.

## test_size_panel_controller.py
Pruebas para `SizePanelController`:

- `test_toggle_show_hide`: `toggle()`, `show()`, `hide()` cambian `visible`.
- `test_drag_updates_pos_when_dragging`: `drag()` actualiza `pos` con `drag_offset`.
- `test_drag_does_nothing_when_not_dragging`: `drag()` sin `dragging` no cambia `pos`.
- `test_stop_drag`: `stop_drag()` establece `dragging=False`.
- `test_on_size_selected_valid_and_invalid`: `on_size_selected()` invoca `select` y respeta límites.

## test_size_panel_events.py
Pruebas para `SizePanelEventHandler`:

- `test_start_drag_inside_panel_sets_dragging_and_offset`: `_start_drag()` activa `dragging` y `drag_offset` al hacer clic derecho dentro.
- `test_perform_drag_calls_controller_drag_and_returns_true`: `_perform_drag()` llama `drag()` del controller.
- `test_stop_drag_calls_controller_and_returns_true`: `_stop_drag()` llama `stop_drag()` del controller.
- `test_select_size_calls_on_size_selected_and_returns_true`: `_select_size()` llama `on_size_selected()` para clic izquierdo dentro de `option_rects`.
- `test_unhandled_event_returns_false`: Eventos no relacionados retornan `False`.

## test_size_panel_view.py
Pruebas para `SizePanelView`:

- `test_ensure_panel_position_initializes_pos_and_panel_pos`: `_ensure_panel_position()` asigna `pos` y `panel.pos`.
- `test_render_does_nothing_when_not_visible`: `render()` sin `visible` no altera `option_rects`.
- `test_render_populates_option_rects_when_visible`: `render()` con `visible` llena `option_rects` para cada tamaño.

---

Para ejecutar los tests:

```bash
pytest tests/roguelike_editors/tiles/size_panel -q
```
