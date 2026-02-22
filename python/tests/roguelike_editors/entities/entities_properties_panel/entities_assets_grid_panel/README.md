# Tests: Entities Assets Grid Panel

Valida el manejo de hover, selección y double-click para abrir el Assets Picker; y el toggle del conjunto activo.

Archivos de test:
- test_events.py
  - Hover: setea `hovered_asset_cell`.
  - Click: setea `selected_asset_cell`.
  - Double-click: llama `assets_picker_controller.show(...)` con label provider.
  - Click en `active_set_rect`: empuja `ToggleActiveSetCommand` al History.

Cobertura principal:
- Integración con `picker_model.panel_rect` para posicionamiento del picker.
- Comando undoable `ToggleActiveSetCommand` mediante monkeypatch.
