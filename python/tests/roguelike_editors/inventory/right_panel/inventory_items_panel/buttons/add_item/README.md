# Pruebas del componente Add Item

En este directorio se encuentran los tests de `pytest` para el flujo de añadir ítems en el grid (botón **Add Item**):

- **test_add_item_model.py**: Verifica los valores por defecto, la mutabilidad independiente de instancias y la asignación de propiedades en la clase `AddItemModel`.
- **test_add_item_controller.py**: Comprueba la lógica de `AddItemController`, incluyendo:
  - `load_available_items`: carga de lista de ítems disponibles desde la vista.
  - `start_add_item`: inicio del flujo y preparación de datos (default_items y ground_items).
  - `select_item`: selección de un ítem y transición a input de cantidad.
  - `confirm_quantity` en contexto `default`: actualización de datos de slots predeterminados de jugador.
- **test_add_item_event_handler.py**: Testea `AddItemEventHandler.handle` para manejar eventos de usuario, tales como:
  - Click en el rectángulo de **Add Item** para iniciar el flujo.
  - Drag del panel de lista (inicio, movimiento y fin de drag).
  - Scroll y selección de ítems dentro del panel.
  - Flujo de ingreso de cantidad: captura de dígitos, retroceso (Backspace), confirmación (Return) y cancelación (Escape).
- **test_add_item_view.py**: Valida `AddItemView.draw`, asegurando que:
  - Calcula correctamente la posición del botón en función del número de slots (una o varias filas).
  - Devuelve un rectángulo `add_item` válido.
  - Detecta correctamente el estado de hover según la posición del ratón.
