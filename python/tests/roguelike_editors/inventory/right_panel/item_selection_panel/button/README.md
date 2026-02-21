# Pruebas del botón de ItemSelectionPanel

En este directorio se encuentran los tests de `pytest` para el componente de botón de `ItemSelectionPanel`:

- **test_button_model.py**: Verifica valores por defecto (`drag_offset`, `dragging`, `drag_start_pos`) y asignación de propiedades.
- **test_button_controller.py**: Comprueba la lógica de `ButtonController.confirm` en diferentes escenarios:
  - Confirmación fuera de `ground` retorna item y qty directos.
  - Para `ground` con `qty=1` remueve el ítem y limpia la selección.
  - Para `ground` con `qty>1` actualiza cantidad restante en `ground_items`.
  - Manejo de cadenas de cantidad inválidas con fallback a `model.quantity`.
- **test_button_event_handler.py**: Testea `ButtonEventHandler.handle`:
  - Clic dentro del botón dispara `set_quantity`, `confirm`, delega a `grid_controller` y desactiva `text_input`.
  - Clic fuera o evento no click retornan `False`.
- **test_button_view.py**: Valida `ButtonView.draw`:
  - Dibuja el botón sin errores y retorna diccionario con clave `add_button_rect`.
