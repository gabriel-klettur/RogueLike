# Pruebas del componente Tabs

En este directorio se encuentran los tests de `pytest` para los módulos del componente de pestañas (tabs) del panel izquierdo del editor de inventario:

- **test_tabs_model.py**: Verifica la inicialización y comportamiento del modelo `TabsModel`, incluyendo valores por defecto y mutabilidad independiente de instancias.
- **test_tabs_controller.py**: Comprueba la lógica de `TabsController` al cambiar categorías, actualización de estados en el modelo del panel y recarga de datos para la categoría `monsters`.
- **test_tabs_event_handler.py**: Testea el manejador `TabsEventHandler` para capturar eventos de clic en las pestañas y llamar a `change_category` adecuadamente.
- **test_tabs_view.py**: Valida la vista `TabsView`, asegurando que `draw` devuelva correctamente los rectángulos con sus categorías y actualice `tab_rects`.
