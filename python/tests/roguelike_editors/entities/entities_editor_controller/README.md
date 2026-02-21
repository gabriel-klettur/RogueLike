# Tests: Entities Editor Controller

Cobertura de pruebas para `entities_editor_controller.py`:

- enter_spawn_mode/exit_spawn_mode: activa flags, parpadeo del Picker, visibilidad del Picker, limpieza del Properties Panel y restauración de cursor.
- enter_delete_mode/exit_delete_mode: alterna flags y cursor; oculta Picker y limpia estado de Properties.
- enter_add_entities_on_system_mode/exit_add_entities_on_system_mode: oculta el Picker y expande el Properties Panel al espacio del Picker; restaura el layout al salir.
- handle_event:
  - Click en mapa en modo Spawn encola `SpawnEntityCommand` con coordenadas de tile (via `screen_to_tile`).
  - Click en mapa en modo Delete encola `DeleteEntityCommand` usando `find_clickable_entity_at`.
  - Click en el panel de Picker durante Delete encola `DeleteEntityDefinitionCommand` para el `selected_id`.

Se emplean stubs simples para `game/camera` y monkeypatch de helpers para aislar la lógica del controlador y evitar dependencias de ECS.
