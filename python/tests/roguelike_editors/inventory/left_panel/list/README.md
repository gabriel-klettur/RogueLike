# Pruebas del componente List

En este directorio se encuentran los tests de `pytest` para los módulos del componente de lista (list) del panel izquierdo del editor de inventario:

- **test_list_model.py**: Verifica la inicialización y comportamiento del modelo `ListModel`, incluyendo valores por defecto y mutabilidad independiente de instancias.
- **test_list_controller.py**: Comprueba la lógica de `ListController`, incluyendo `select_entity` y la generación de listas de items para categorías `player`, `monsters` y otras.
- **test_list_event_handler.py**: Testea el manejador `ListEventHandler` para capturar eventos de clic en la lista, incluyendo selección simple, doble-click en posición y casos de clic fuera o categorías distintas.
- **test_list_view.py**: Valida la vista `ListView`, asegurando que `draw` devuelva correctamente `panel_rect` y `list_rect`, establezca items en `scroll_panel` y no falle al resaltar en categoría `monsters`.
