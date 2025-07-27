# Pruebas del InventoryItemsPanel

En este directorio se encuentran los tests de `pytest` para el panel de items del inventario (componente completo):

- **test_inventory_items_panel_model.py**: Verifica instanciación de submodelos (`add_item`, `delete`, `save`, `grid`, `tabs`), wrappers de propiedades y funcionamiento independiente entre instancias.
- **test_inventory_items_panel_controller.py**: Comprueba creación de subcontroladores y delegación de métodos (`load_available_items`, `start_add_item`, `select_item`, `confirm_quantity`, `delete_item`, `save_default`, `save_active`) incluyendo alias `_save_default` y `_save_active`.
- **test_inventory_items_panel_event_handler.py**: Testea `InventoryItemsPanelEventHandler.handle`, asegurando que:
  - Retorna `False` cuando no hay handlers.
  - Retorna `True` y detiene iteración al primer handler que devuelve `True`.
  - Captura excepciones en handlers y continúa.
- **test_inventory_items_panel_view.py**: Valida `InventoryItemsPanelView`:
  - `get_slot_index`: delega a `grid_view.get_slot_index`.
  - `draw()`: devuelve un diccionario con claves `show_default`, `show_active`, `add_item`, `delete_item`, `save` y actualiza los atributos de compatibilidad (`show_default_rect`, `show_active_rect`, `add_item_rect`, `delete_item_rect`, `save_rect`) sin errores.
