# Pruebas de ItemSelectionPanel

Este directorio contiene los tests de `pytest` para los componentes generales del panel de selección de ítems:

- **test_item_selection_panel_model.py**: Verifica:
  - Estado inicial de `ItemSelectionPanelModel` (tabs, lista, input, título, arrastre).
  - Funcionalidad de los setters que delegan a submodelos.
- **test_item_selection_panel_controller.py**: Comprueba `ItemSelectionPanelController` delega correctamente en:
  - `open`/`close` → `TittleController`
  - `select_item` → `ListController`
  - `change_tab` → `TabsController`
  - `set_quantity` → `InputController`
  - `confirm` → `ButtonController` y retorna valores.
- **test_item_selection_panel_event_handler.py**: Testea `ItemSelectionPanelEventHandler.handle`:
  - No procesa eventos si `show_panel=False`.
  - Devuelve `True` en el primer handler que procese el evento.
  - Devuelve `False` si ningún handler lo procesa.
- **test_item_selection_panel_view.py**: Valida `ItemSelectionPanelView.draw`:
  - Retorna `{}` cuando `show_panel=False`.
  - Fusiona los dicts de todas las subviews (`tittle`, `tabs`, `list`, `input`, `button`).
  - Calcula y posiciona `panel_rect` usando `drag_offset`.

Ejecuta `pytest` para validar que todos los tests pasan sin errores.
