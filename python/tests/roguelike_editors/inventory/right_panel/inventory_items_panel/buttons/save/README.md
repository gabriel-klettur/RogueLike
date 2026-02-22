# Pruebas del componente Save

En este directorio se encuentran los tests de `pytest` para el flujo de guardado del inventario (botón **Save**):

- **test_save_model.py**: Verifica los valores por defecto de `SaveModel`, la independencia de instancias y la asignación de propiedades (`save_in_progress`, `save_message`).
- **test_save_controller.py**: Comprueba la lógica de `SaveController`, incluyendo:
  - `save_default`: creación de archivo JSON con datos por defecto y registro en `logger.info`.
  - `save_active`: creación de archivo JSON con datos activos y registro en `logger.info`.
  - Manejo de errores al crear directorios o escribir archivos, registrando en `logger.error`.
- **test_save_event_handler.py**: Testea `SaveEventHandler.handle` para:
  - Detectar clic en `save_rect` y llamar a `save_default` cuando `editing_side=='default'`.
  - Detectar clic en `save_rect` y llamar a `save_active` cuando `editing_side=='active'`.
  - Retornar `False` al hacer clic fuera de `save_rect`, sin invocar ningún método de guardado.
- **test_save_view.py**: Valida `SaveView.draw`, asegurando que:
  - Calcula posición y tamaño del botón según el número de slots y el estado `delete_mode_active`.
  - Devuelve un rectángulo bajo la clave `save` con las coordenadas correctas.
  - Detecta estado de hover sobre el botón cambiando el color del borde.
