# Pruebas del componente Panel

En este directorio se encuentran los tests de `pytest` para los módulos del componente de panel izquierdo:

- **test_panel_model.py**: Verifica la inicialización y delegación de propiedades en `InventoryPanelModel`, incluyendo `tabs_model`, `list_model`, `categories`, `current_category`, `selected_eid` y `camera_focus_target`.
- **test_panel_controller.py**: Comprueba la delegación de métodos `change_category`, `select_entity` y `get_items_list` en `PanelController` hacia `TabsController` y `ListController`.
- **test_panel_event_handler.py**: Testea `PanelEventHandler.handle` para:
  - Recentralizar la cámara y limpiar `camera_focus_target` al recibir eventos de mouse con foco activo.
  - Cortocircuitar en `tabs_handler.handle` y `list_handler.handle`.
  - Bloquear hovers dentro de `tab_rects`.
- **test_panel_view.py**: Valida `PanelView.draw`, asegurando que delega en `TabsView` y `ListView`, combine resultados y actualice `tab_rects` y `panel_rect`.
