# Pruebas del List de ItemSelectionPanel

En este directorio se encuentran los tests de `pytest` para el componente de lista de `ItemSelectionPanel`:

- **test_list_model.py**: Verifica valores por defecto (`visible_count`, `scroll_offset`, `selected_item`, `selected_index`) y asignación de propiedades, incluyendo independencia de instancias.
- **test_list_controller.py**: Comprueba `ListController`:
  - `select_item` establece `selected_item` y `selected_index` sólo si `current_tab=='ground'`.
  - `reset_selection` limpia ambas propiedades.
- **test_list_event_handler.py**: Testea `ListEventHandler.handle`:
  - Eventos de scroll (`MOUSEBUTTONDOWN`, `MOUSEWHEEL`) delegados a `scroll_panel.handle_event`.
  - Clic en elemento dentro del área selecciona el ítem correcto a través de `list_controller`.
  - Clic fuera retorna `False` cuando no hay selección.
- **test_list_view.py**: Valida `ListView.draw`:
  - Dibuja panel de scroll sin errores.
  - Resalta hover y selección genérica e `ground` sin lanzar excepciones.
