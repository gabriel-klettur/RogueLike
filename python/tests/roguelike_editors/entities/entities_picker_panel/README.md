# Tests: Entities Picker Panel

Valida la interacción del panel de selección (Players/Hostile).

Archivos de test:
- test_events.py
  - Click en pestañas: cambia `active_tab`, resetea selección/hover/scroll.
  - Click en grid: selecciona `selected_id` según celda.
  - Hover en grid: actualiza `hovered_id`.
  - Arrastre con botón derecho: delega a `draggable_panel` (down/motion/up).
  - Teclas: `K_DOWN` y `K_UP` ajustan `scroll_index`; `F5` alterna visibilidad.

Cobertura principal:
- Cálculo de posición en grid (`_calculate_grid_position`) y límites de celda.
- Manejo de arrastre y teclas en `EntitiesPickerEventHandler`.
