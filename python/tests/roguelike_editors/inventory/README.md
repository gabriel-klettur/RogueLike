# Pruebas del Editor de Inventario

Este directorio contiene los tests `pytest` para los componentes centrales del editor de inventario:

- **test_data_controller.py**: Verifica la carga de datos JSON y la extracción de datos anidados (map), incluyendo rutas y omisión de validación de esquemas.
- **test_editor_model.py**: Valida `InventoryEditorModel`:
  - Estados iniciales de atributos (visible, default_data, active_data, etc.)
  - Getters y setters de `editing_side`, `categories`, `current_category`, `selected_eid`, `camera_focus_target`.
- **test_editor_controller.py**: Comprueba `InventoryEditorController`:
  - Inicialización de subcontroladores (panel izquierdo, grid, item selection)
  - Llamada a `DataController.load_data` durante la construcción
  - Métodos `handle_event` y `debug_dump` generan salidas esperadas.
- **test_editor_events.py**: Testea `InventoryEditorEventHandler.handle`:
  - Delegación de eventos a `inventory_panel_event_handler`, `item_selection_event_handler` y `grid_event_handler`.
  - Retorno de `True`/`False` según manejadores.
- **test_editor_view.py**: Valida `InventoryEditorView` (en los tests se stubean `inventory_panel_controller` y `inventory_panel_view.draw`):
  - `draw` retorna `None` si `model.visible=False`
  - `_draw_overlay` crea overlay y dibuja títulos y panel izquierdo (test stubea `inventory_panel_controller` y `inventory_panel_view.draw`)
  - `get_slot_at_pos` identifica índices correctos según posición
  - `_get_item_image` retorna `None` para ítems inexistentes o errores de carga.

Ejecuta `pytest` para validar que todos los tests pasen sin errores.
