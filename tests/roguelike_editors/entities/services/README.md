# Tests: Services (entities)

Conjunto de tests unitarios para utilidades de `roguelike_editors.entities.services`.

Archivos de test:
- test_history.py: pila de undo/redo (`HistoryManager`).
- test_camera_helpers.py: conversiones de pantalla a mundo/tiles (`screen_to_world`, `screen_to_tile`).
- test_entity_lookup.py: búsquedas y hit-testing de entidades clickeables (`iter_clickable_entities`, `find_clickable_entity_at`, `find_clickable_entity_rect_at`).
- test_ui_helpers.py: ocultar picker y limpiar propiedades en cambios de modo (`hide_assets_picker_and_clear_properties`).
- test_spawn_services.py: spawning en mapa para jugadores/monstruos usando stubs de ECS (`spawn_entity` decide fábrica y kwargs).
