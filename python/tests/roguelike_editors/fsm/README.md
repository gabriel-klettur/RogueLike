# Plan de tests para el editor de FSM

Este documento resume la cobertura actual de tests del editor de máquinas de estados finitos (FSM) y propone una batería de pruebas adicional para garantizar robustez y escalabilidad del módulo `src/roguelike_editors/fsm/`.

## Cobertura actual

- `tests/roguelike_editors/fsm/test_fsm_graph_tools.py`
  - Cubre acciones básicas de la toolbar del panel de grafo (`AddNode`, `Connect`, `Disconnect`, `Clone`, `DeleteNode`, `MarkIni`, `MarkEnd`) contra `FsmGraphPanelModel`.
- `tests/roguelike_editors/fsm/services/test_editor_layout.py`
  - Cálculo de anclajes para paneles y canvas.
- `tests/roguelike_editors/fsm/services/test_graph_build.py`
  - Construcción de grafo desde una definición de set, aplicación de layout persistido y viewport (zoom/pan/legend).

Estos tests validan el “happy path” del grafo y el layout básico. A continuación, se listan pruebas que deberían añadirse para cubrir casos límite, persistencia, flujos de edición avanzados y performance.

## Áreas y suites de prueba recomendadas

- Controlador, modelo y vista del editor:
  - `fsm_editor_controller.py`
  - `fsm_editor_model.py`
  - `fsm_editor_view.py`
- Panel de grafo (modelo + eventos + layout):
  - `fsm_graph_panel/` (incluye toolbar, eventos de canvas, routing de aristas)
- Servicios clave:
  - `services/fsm_persistence.py`
  - `services/fsm_graph_layout.py`
  - `services/graph_build.py`
  - `services/fsm_history.py`
  - `services/fsm_registry.py`
  - `services/fsm_id.py`
  - `services/fsm_runtime_bridge.py`

## Detalle de casos de prueba propuestos

### 1) Editor (controlador/modelo/vista)
- [Ctrl] Inicialización y estado por defecto
  - `active`/paneles visibles por defecto, herramienta seleccionada, zoom/pan iniciales.
- [Ctrl] Toggling de paneles (toolbar/sets/properties/title)
  - Verificar señales/model updates y que el estado se persista si aplica.
- [Ctrl] Despacho de eventos a sub-paneles
  - Click/drag en canvas se enruta al panel de grafo; clicks sobre UI no afectan el canvas.
- [Ctrl] Guardado/carga de sets y layouts
  - Llama a `services.fsm_persistence` con rutas correctas; round-trip idempotente.
- [View] Hit-testing y arrastre de nodos
  - Drag de un nodo actualiza `x,y` con snap a grid (si existe) y respeta límites del canvas/pan.
- [View] Selección múltiple y “marquee selection”
  - Arrastre de rectángulo selecciona subconjunto esperado; mover grupo conserva offsets.

### 2) Panel de grafo (modelo + eventos)
- [Graph] Conectar/Desconectar avanzado
  - Conexión cancelada (ESC o click vacío) limpia `connect_source_node_id`.
  - Autoloops (A→A) permitidos o rechazados según regla; duplicados bloqueados.
- [Graph] Borrado de arista por click en path
  - Hit-test sobre `edge_paths` elimina arista correcta.
- [Graph] Renombrado/Edición de etiqueta de nodo/edge
  - Si existe herramienta/acción, asegura actualización de `label` y reflow opcional.
- [Graph] Marcado de inicial: unicidad
  - Solo un nodo `initial=True`; al marcar otro, el previo se desmarca.
- [Graph] Estados terminales
  - Toggle estable y persistente.
- [Graph] Zoom/Pan
  - Límites, factor mínimo/máximo y persistencia en `graph_build`.

### 3) Layout y routing
- `services/fsm_graph_layout.py`
  - Posicionado inicial determinista (sin datos previos).
  - Routing ortogonal evita superposición con nodos (en la medida de lo posible).
  - Posición de etiquetas de aristas consistente.
- `services/editor_layout.py`
  - Casos borde: toolbar en extremos, pantallas pequeñas, paneles grandes/DPI.

### 4) Persistencia y migración
- `services/fsm_persistence.py`
  - Round-trip completo: `save → load` conserva nodos, aristas, flags (initial/terminal), labels.
  - Migración de versiones anteriores (si aplica): transforma esquemas antiguos a actual.
  - Manejo de errores: archivos corruptos, claves faltantes, IDs duplicados.
  - Esquema de layouts: guarda/lee `viewport` (zoom/pan/legend_collapsed) y posiciones por `set`.
  - Escritura atómica (si implementada): no deja archivos parciales.

### 5) IDs, registro e historial
- `services/fsm_id.py`
  - Unicidad de IDs bajo concurrencia simulada (ráfaga de creación).
  - Charset/formato esperado (p.ej. `A-Za-z0-9_-`).
- `services/fsm_registry.py`
  - Alta/baja/renombrado de `set`s; no permite duplicados; propagación de cambios al editor.
- `services/fsm_history.py`
  - Apilar acciones, deshacer/rehacer múltiples pasos.
  - Invalida redo al introducir nueva acción tras undo.
  - Coalescing de movimientos continuos (p.ej. drag prolongado) en una sola entrada.

### 6) Runtime/Integración
- `services/fsm_runtime_bridge.py`
  - Construcción de runtime desde definición; ejecución de eventos `when`; transiciones correctas.
  - Estados terminales detienen la ejecución según reglas.
  - Manejo de guardas/fallos en callbacks.
- End-to-End (E2E) editor
  - Secuencia: crear dos nodos, conectar, marcar inicial/terminal, guardar, recargar; invariantes se mantienen.

### 7) Robustez, escalabilidad y propiedades
- Grafos grandes (p.ej. 200 nodos, 400 aristas)
  - `graph_build` y layout no deben lanzar excepciones; tiempos aceptables (test etiquetado como `slow`).
- Tests basados en propiedades (Hypothesis)
  - Generar sets válidos aleatorios; verificar invariantes: no aristas a nodos inexistentes, unicidad de IDs, al menos 1 inicial si especificado, etc.
- Fuzz tests de eventos del usuario
  - Secuencias aleatorias de clicks/drag no deben romper el modelo ni dejar estados inválidos.

## Sugerencia de estructura de ficheros de test nuevos

```
tests/roguelike_editors/fsm/
├─ test_fsm_editor_controller.py
├─ test_fsm_editor_view.py
├─ test_fsm_graph_canvas_events.py
├─ test_fsm_persistence_roundtrip.py
├─ test_fsm_persistence_migrations.py
├─ test_fsm_graph_layout.py
├─ test_fsm_history.py
├─ test_fsm_registry.py
├─ test_fsm_id.py
├─ test_fsm_runtime_bridge.py
├─ property_based/
│  ├─ test_graph_build_properties.py
│  └─ generators.py
└─ fixtures/
   ├─ fsm_sets_minimal.py
   ├─ fsm_sets_large.py
   └─ layouts_samples.py
```

## Guía de implementación de tests

- Usar `monkeypatch` para aislar E/S de disco en `fsm_persistence` y `graph_build`.
- Añadir fixtures reutilizables en `fixtures/` (sets pequeños, layouts predefinidos).
- Para property-based, considerar `hypothesis` (añadir a `requirements-dev.txt` si no está).
- Etiquetar pruebas costosas con `@pytest.mark.slow` para ejecutarlas opcionalmente en CI.
- Verificar resultados de persistencia comparando contra un “esquema” mínimo (p.ej. claves requeridas y tipos), incluso sin JSON Schema.

## Cómo ejecutar

- Suite de FSM completa:

```bash
pytest -q tests/roguelike_editors/fsm/
```

- Con tests lentos incluidos:

```bash
pytest -q -m slow tests/roguelike_editors/fsm/
```

- Solo servicios:

```bash
pytest -q tests/roguelike_editors/fsm/services/
```

## Criterios de aceptación

- Cobertura funcional: creación/edición de nodos y aristas, marcados especial(es), selección, drag, zoom/pan, persistencia y carga.
- Robustez: manejo de entradas inválidas, archivos corruptos, duplicados, cancelaciones de acciones.
- Escalabilidad: soportar grafos medianos/grandes sin errores ni degradación excesiva.
- Consistencia: round-trip de persistencia idempotente y layouts aplicados correctamente.
