# Tests: Entities Title

Verifica el controlador y la vista del título del editor de entidades.

Archivos de test:
- test_entities_title.py
  - Renderiza sin errores y refleja `model.title` en el widget.
  - `handle_event` retorna `False` (sin eventos activos).

Cobertura principal:
- Construcción de `EntitiesTitleController` y `EntitiesTitleView`.
