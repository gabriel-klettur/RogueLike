# Pruebas de Tittle de ItemSelectionPanel

En este directorio se encuentran los tests de `pytest` para el componente de título de `ItemSelectionPanel`:

- **test_tittle_model.py**: Verifica el valor predeterminado de `show_panel` y la capacidad de asignación.
- **test_tittle_controller.py**: Comprueba `TittleController`:
  - `open` inicializa correctamente `default_items`, `ground_items`, `current_tab`, `available_items`, `scroll_offset`, `selected_item`, `quantity`, `selected_index` y muestra el panel (`show_panel=True`).
  - `close` oculta el panel (`show_panel=False`).
- **test_tittle_event_handler.py**: Testea `TittleEventHandler.handle`:
  - Clic fuera de `panel_rect` y `header_rect` invoca `close` y retorna `True`.
  - Clic en `header_rect` inicia arrastre (`dragging=True`, `drag_start_pos` calculado) y retorna `True`.
  - Movimiento del mouse al arrastrar actualiza `drag_offset` y retorna `True`.
  - Suelta del botón finaliza arrastre (`dragging=False`) y retorna `True`.
  - Eventos irrelevantes retornan `False`.
- **test_tittle_view.py**: Valida `TittleView.draw`:
  - Dibuja fondo, borde y cabecera sin errores.
  - Retorna dict con `panel_rect` y `header_rect`, verificando dimensiones y posición de `header_rect`.
