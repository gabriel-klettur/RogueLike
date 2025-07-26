# Pruebas del componente Grid

En este directorio se encuentran los tests de `pytest` para la cuadrícula de slots del panel derecho:

- **test_grid_model.py**: Verifica los valores por defecto de `GridModel`, la independencia de instancias y la asignación de propiedades (`selected_slot`, `hover_slot`, `grid_rows`, `grid_cols`, `show_delete_mode`).
- **test_grid_controller.py**: Comprueba la delegación de métodos en `GridController`:
  - `_save_default`: invoca `save_controller.save_default` y retorna su valor.
  - `_save_active`: invoca `save_controller.save_active` y retorna su valor.
- **test_grid_event_handler.py**: Testea `GridEventHandler.handle`, asegurando que siempre devuelve `False` sin procesar ningún evento.
- **test_grid_view.py**: Valida `GridView`:
  - `get_slot_index`: detecta correctamente el índice de slot bajo una posición dada y retorna `None` si está fuera de rango.
  - `draw_slots`: no lanza errores al dibujar slots vacíos o con imágenes válidas, y registra errores en `logger.error` si `get_item_image` falla. Detecta el resaltado en modo eliminación (`delete_mode_active`).
