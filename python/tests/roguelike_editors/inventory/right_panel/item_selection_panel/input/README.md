# Pruebas del Input de ItemSelectionPanel

En este directorio se encuentran los tests de `pytest` para el componente de input de `ItemSelectionPanel`:

- **test_input_model.py**: Verifica creación de `InputModel` con valores por defecto y personalizados.
- **test_input_controller.py**: Comprueba `InputController.set_quantity` para cadenas válidas (ints, negativos) y no válidas (letras, vacío, None), con fallback a 1.
- **test_input_event_handler.py**: Testea `InputEventHandler.handle`:
  - Click en `input_rect` activa `text_input.activate` con texto inicial y selección.
  - Evento de texto (`handle_event=True`) llama a `controller.set_quantity`.
  - Eventos no relevantes retornan `False`.
- **test_input_view.py**: Valida `InputView.draw`:
  - Sin actividad sincroniza `text_input.text` con valor de `quantity` y retorna `input_rect`.
  - Con `text_input.active=True` mantiene el texto anterior.
