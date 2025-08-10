# Tests: Entities Tool Bar Panel

Suite de tests para el Toolbar del editor de entidades.

Archivos de test:
- `test_events.py`: valida la integración de eventos del toolbar.
  - Click en `undo` y `redo` invoca `history.undo()` y `history.redo()` (stubs).
  - Click en `entities_on_map` hace toggle de `model.active_tool` y visibilidad del editor y del picker.

Cobertura:
- Uso de `toolbar_view.widget.icon_rects` para hit-testing de íconos.
- Efectos secundarios sobre `picker_controller.model.visible` y `controller.model.visible`.
