# Tests: Entities Add/Remove Panel

Este paquete valida el comportamiento del panel de agregar/quitar entidades.

Archivos de test:
- test_events.py
  - Click en `add_entitie`: entra/sale de modo spawn y actualiza `model.active_tool`.
  - Click en `remove_entitie`: entra/sale de modo delete.
  - Click en `add_entities_on_system`: abre propiedades, muestra selector y alterna el modo correspondiente.

Cobertura principal:
- Eventos de ratón sobre íconos del toolbar interno.
- Integración mínima con `EntitiesEditorController` (métodos stub de enter/exit/open).
- Ajuste de flags del modelo (p.ej. `show_add_system_selector`).
