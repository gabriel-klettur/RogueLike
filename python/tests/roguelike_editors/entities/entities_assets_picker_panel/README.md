# Tests: Entities Assets Picker Panel

Valida el comportamiento del picker de assets embebido bajo el editor de entidades.

Archivos de test:
- test_events.py
  - ESC cierra el panel (`controller.hide()`).
  - Click fuera del panel cierra el picker.
  - Click dentro pero fuera de entradas: consume sin cerrar.
  - Click simple sobre directorio/archivo: solo selecciona (`fs_model.selected`).
  - Doble click sobre directorio: navega (`fs_model.navigate(idx)`).
  - Doble click sobre archivo: invoca `on_asset_chosen(key, path)` sin cerrar.

Cobertura principal:
- Gestión de área del panel (`panel_rect`) y `entry_rects`.
- Flujo de selección/navegación con `DoubleClickDetector`.
- Callback de selección conectado con `model.key`.
