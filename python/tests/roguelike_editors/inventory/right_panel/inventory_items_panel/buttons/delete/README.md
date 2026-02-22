# Pruebas del componente Delete Item

En este directorio se encuentran los tests de `pytest` para el flujo de eliminar ítems en el grid (botón **Delete Item**):

- **test_delete_model.py**: Verifica los valores por defecto de `DeleteModel`, la independencia de instancias y la asignación de propiedades (`show_delete_mode`, `show_delete_quantity_input`, `delete_quantity`).
- **test_delete_controller.py**: Comprueba la lógica de `DeleteController`, incluyendo:
  - Eliminación parcial y total en el modo `default` para la categoría `player`, ajustando cantidades o removiendo slots.
  - Eliminación en modo `active`, actualizando datos activos y componente ECS (`InventoryComponent`).
- **test_delete_event_handler.py**: Testea `DeleteEventHandler.handle` para:
  - Alternar modo eliminación al hacer click en el rectángulo.
  - Cancelar modo al hacer click fuera de slots o al desactivar.
  - Integración con el input de cantidad: activación de `delete_qty_input`, manejo de eventos de texto y clics.
- **test_delete_view.py**: Valida `DeleteView.draw_button` y `draw_input`, asegurando que:
  - Calculan correctamente posición y tamaño del botón según el número de slots.
  - Detectan estado de hover y cambian el color del borde.
  - `delete_qty_input_rect` se establece tras `draw_input` con coordenadas correctas.
