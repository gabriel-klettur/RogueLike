# Test Suite for Tiles Editor Layers Panel Module

Este directorio contiene tests de pytest para los componentes del panel de capas del editor de tiles.

## test_layers_panel_states.py
Pruebas para la clase `LayersPanelState`:

- `test_default_state_values`: Verifica los valores iniciales por defecto (diccionarios vacíos, pos `None`, `dragging` `False`, `drag_offset` `(0,0)`).

## test_layers_panel_controller.py
Pruebas para `LayersPanelController`:

- `test_init_copies_visible_layers`: Asegura que `visible_layers` se copie por valor y no por referencia.
- `test_drag_updates_position_when_dragging_true`: Verifica actualización de `state.pos` durante el arrastre.
- `test_drag_does_nothing_when_not_dragging`: Asegura que `drag` no modifique `pos` si no se está arrastrando.
- `test_stop_drag_sets_dragging_false`: Comprueba que `stop_drag` establezca `dragging` en `False`.

## test_layers_panel_events.py
Pruebas para `LayersPanelEventHandler`:

- `test_handle_event_right_start_drag_inside`: Inicia arrastre al hacer clic derecho dentro del panel.
- `test_handle_event_right_start_drag_outside`: No inicia arrastre si el clic derecho está fuera.
- `test_handle_event_motion_calls_drag_and_returns_true`: Llama a `drag` durante `MOUSEMOTION` con `dragging=True`.
- `test_handle_event_stop_drag_calls_stop_and_returns_true`: Finaliza arrastre en `MOUSEBUTTONUP` derecho.
- `test_handle_event_left_toggle_generic_layer`: Alterna visibilidad de una capa genérica y sincroniza con `toolbar_state`.
- `test_handle_event_left_toggle_buildings`: Alterna `show_buildings` para la capa `buildings`.
- `test_handle_event_unhandled_returns_false`: Retorna `False` para eventos no relacionados.

## test_layers_panel_view.py
Pruebas para `LayersPanelView`:

- `test_ensure_panel_position_with_icon`: Posición inicial cuando existe el icono `view_layers`.
- `test_ensure_panel_position_without_icon`: Posición inicial por defecto sin icono.
- `test_render_populates_option_rects`: Verifica que `render` genere `option_rects` para cada capa del Enum y `buildings`.

---

Para ejecutar todos los tests:

```bash
pytest tests/roguelike_editors/tiles/layers_panel -q
```
