# Pruebas del componente Tabs

En este directorio se encuentran los tests de `pytest` para la funcionalidad de pestañas del inventario (default/active):

- **test_tabs_model.py**: Verifica valores por defecto de `TabsModel.active_tab` y `TabsModel.available_tabs`, independencia de instancias en `available_tabs` y asignación de propiedades.
- **test_tabs_controller.py**: Comprueba que `TabsController.show_default()` y `TabsController.show_active()` actualizan `model.editing_side` a 'default' o 'active' respectivamente.
- **test_tabs_event_handler.py**: Testea `TabsEventHandler.handle`:
  - Click en el rectángulo `show_default_rect` cambia `editing_side` a 'default' y retorna `True`.
  - Click en `show_active_rect` cambia `editing_side` a 'active' y retorna `True`.
  - Click fuera de ambos rects retorna `False` y no modifica `editing_side`.
  - Eventos no mouse retornan `False`.
- **test_tabs_view.py**: Valida `TabsView`:
  - `draw_tabs()`: genera un diccionario con claves `show_default` y `show_active` y rectángulos en posiciones correctas según origen, tamaño de botón y margen.
  - `get_slots_data()`: devuelve la lista de `slots` correcta según `model.editing_side` y `model.current_category` para casos:
    - `default/player` (slots por defecto);
    - `default/monsters` (uso de `template_id`, cantidad mínima y padding);
    - `active` (slots activos);
    - categoría vacía o inválida retorna lista vacía.
