# Pruebas de Tabs de ItemSelectionPanel

En este directorio se encuentran los tests de `pytest` para el componente de tabs de `ItemSelectionPanel`:

- **test_tabs_model.py**: Verifica:
  - Valores por defecto (`default_items`, `ground_items`, `current_tab`, `available_items`).
  - Instanciación con lista de items y copia independiente de `default_items`.
- **test_tabs_controller.py**: Comprueba `TabsController.change_tab`:
  - Cambio a `ground` y `default` resetea `scroll_offset`, `quantity`, `selected_item`, `selected_index`, y ajusta `available_items`.
  - Llamadas inválidas no modifican el modelo.
- **test_tabs_event_handler.py**: Testea `TabsEventHandler.handle`:
  - Eventos no click o clic fuera de `tab_rects` retornan `False`.
  - Clic en `default` o `ground` llama a `change_tab` con etiqueta correcta y resetea `scroll_panel.scroll_offset`.
- **test_tabs_view.py**: Valida `TabsView.draw`:
  - Retorna un diccionario con clave `tab_rects` con ambos rects.
  - Funciona tanto para `default` como `ground` sin errores.
