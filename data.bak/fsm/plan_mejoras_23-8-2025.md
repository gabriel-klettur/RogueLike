# Plan de mejoras FSM (23-08-2025)

Documento guía para robustecer y escalar el sistema de FSM (runtime ECS + Editor). Este plan se usa como referencia viva y checklist de implementación.

- Autor: Equipo RogueLike
- Ámbito: `data/fsm/*.json`, `src/roguelike_editors/fsm/*`, `src/roguelike_game/*`
- Estado: En preparación (v0)

---

## 0) Contexto actual (resumen)

- Datos: `data/fsm/sets.json` (con `states` + `transitions`), `assignments.json`, `schema.json`.
- Persistencia: `_ensure_ids_and_defaults()` normaliza IDs, `validate()` (schema), `_lint_sets()` (semántico), exporta `data/fsm/fsm_ids.json` con: `SET_IDS`, `STATES_BY_SET`, `TRANSITIONS_BY_SET`.
- Runtime: `fsm_runtime_bridge.build_fsm_from_set()` crea FSM desde `initial`, adjunta `context` (`id_to_class`, `class_to_id`, `transitions`, `set_id`, `anim_map`, `damage_next_class` y `allowed_state_classes` para Monster_*). Eventos de combate ya en cola FSM.
- Editor: MVC con Toolbar, Sets, Graph, Properties; linter UI (badges) y filas para transitions con hints e ids estables.

---

## 1) Objetivos

- Robustez: validación estricta, migraciones versionadas, escritura atómica, guardias seguras.
- Escalabilidad: evaluación O(1) por frame, caches precompiladas, hot-reload estable.
- DX/UX: edición tipada, simulación visual, refactors seguros, trazas y métricas.

---

## 2) Roadmap (alto nivel)

- [ ] Fase A: Schema V1 + Migraciones + Linter ampliado (alta prioridad)
- [ ] Fase B: Evaluador de transiciones JSON en runtime (prioridad alta)
- [ ] Fase C: Editor – UI tipada + Modo Simulación (prioridad media-alta)
- [ ] Fase D: Persistencia robusta + CLI (prioridad media)
- [ ] Fase E: Instrumentación/Debug + Export extendido (prioridad media)
- [ ] Fase F: Refactors seguros + Panel de Assignments (prioridad media)
- [ ] Fase G: Rendimiento, tests y endurecimiento final (prioridad continua)

---

## 3) Fase A — Schema V1 + Migraciones + Linter

Archivos: `data/fsm/schema.json`, `src/roguelike_editors/fsm/services/fsm_persistence.py`

Cambios propuestos (schema):
- [x] `version` obligatorio en `sets.json` (int, >=1).
- [x] `transition` extiende campos: `event` (string), `priority` (int>=0, default 0), `cooldown_frames` (int>=0, default 0), `guard` (obj AST), `actions` (array de objetos `{id, params}`), `style.*`, `tags[]`.
- [x] `state`: soportar `on_enter[]`, `on_exit[]`, `blackboard` defaults (obj), `special`, `external_entry` (bool), `class` requerido (ya).
- [x] Global transitions: permitir `from: "*"` o `global: true`.
- [x] $defs: `GuardAST`, `ActionCall`, `BlackboardSpec`.

Migraciones y defaults:
- [x] `_ensure_ids_and_defaults()`: si falta `version`, set a 1; rellenar defaults por transición (`priority=0`, `cooldown_frames=0`, `actions=[]`).
- [x] Migración de `when` → `event` manteniendo compatibilidad (duplicar si ambos existen).
- [x] Normalizar `global transition`: si `from=="*"`, marcar `global=true`.

Linter ampliado (`_lint_sets()`):
- [x] Duplicados por firma `(from,to,event)` → warning.
- [x] `priority` debe ser entero no negativo; `cooldown_frames` idem.
- [x] Guards inválidos (estructura AST) → error no bloqueante (reportado como warning básico).
- [x] Estados sin salida y sin `global` que los saque → warning.
- [x] Transiciones inalcanzables desde `initial` (ignorando especiales/external e incluyendo globales) → warning.

Criterios de aceptación:
- [x] `validate()` pasa con schema nuevo sobre sets actuales (con migración automática).
- [x] Guardados reproducibles (orden y formato estable).
- [x] Linter muestra mensajes útiles en el Editor (badges) sin romper el flujo.

---

## 4) Fase B — Evaluador de transiciones JSON (runtime)

Archivos: `src/roguelike_editors/fsm/services/fsm_runtime_bridge.py`, `roguelike_game/ecs/systems/fsm/*`

Compilación al cargar set:
- [ ] Construir `transitions_by_from: Dict[state_id, List[Transition]]` ordenado por `priority` desc y orden estable.
- [ ] `global_transitions: List[Transition]` prefiltradas.
- [ ] Precompilar `guard` (AST) a un evaluador seguro (sin `eval`), con operadores `and/or/not`, comparaciones (`==,!=,<,<=,>,>=`), acceso limitado a `bb` (blackboard), `ctx` (fsm.context), `evt` (evento actual).

Ejecución determinista por frame/evento:
- [ ] Consumir eventos de la cola por entidad; para cada evento, evaluar transiciones del estado actual; si ninguna, evaluar `global_transitions`.
- [ ] Aplicar cooldown por transición (timestamp frame-based, `cooldown_frames`).
- [ ] Ejecutar `actions` (Action Registry) y `on_exit`/`on_enter` de estados (data-driven cuando posible).

Compatibilidades y políticas:
- [ ] Generalizar `allowed_state_classes` por set (no sólo Monster_*), configurable.
- [ ] `damage_next_class` derivado del JSON y override-able por entidad.
- [ ] Hot-reload: doble buffer de cache; mantener `current_state_id` si sigue existiendo; si no, caer a `initial`.

Criterios de aceptación:
- [ ] Coste O(1) por evaluación (no iterar todas las transiciones globalmente).
- [ ] Guards no ejecutan código arbitrario; límites de tiempo/tamaño.
- [ ] Comportamientos actuales (Player/Monster/Spawner) siguen funcionando y ganan determinismo.

---

## 5) Fase C — Editor: UI tipada + Simulación

Archivos: `src/roguelike_editors/fsm/*`

Propiedades tipadas:
- [ ] Dropdown de `event` (catálogo configurable por proyecto).
- [ ] Inputs numéricos (priority, cooldown) con validación inmediata.
- [ ] Builder visual de `guard` (árbol de condiciones) con preview textual.
- [ ] Picker de `actions` desde Action Registry (con params).
- [ ] Hints y tooltips contextuales (por regla de linter/schema).

Modo simulación:
- [ ] Controles Play/Pause/Step (barra del Graph Panel).
- [ ] Inyección de eventos de prueba; visualización de `bb`/`ctx`/timers.
- [ ] Resaltado del camino activo (nodo actual y arista tomada); timeline simple.

Refactors y UX:
- [ ] Renombrar `state.id` actualiza `transitions.from/to` y `assignments.json` (transacción undoable).
- [ ] Clonar subgrafo (multi-select) generando IDs estables.
- [ ] Auto-layout, align/distribute, multi-select, copiar/pegar.

Criterios de aceptación:
- [ ] Edición libre sin roturas (undo/redo integrales).
- [ ] Simulación reproduce lo que hace el runtime para un set aislado.

---

## 6) Fase D — Persistencia robusta + CLI

Archivos: `fsm_persistence.py`, `scripts/cli_fsm.py` (nuevo)

- [ ] Escrituras atómicas: guardar a `*.tmp` + `replace()`; backups rotativos `*.bak` (N últimos).
- [ ] Detección de conflictos: si hay `<<<<<<<` en memoria/archivo, bloquear save y mostrar ayuda.
- [ ] CLI: `fsm lint`, `fsm migrate --to <ver>`, `fsm export --ids|--graphviz`, `fsm check-refs`.
- [ ] Integración CI/local (opcional): pre-commit con `fsm lint`.

Criterios de aceptación:
- [ ] Ningún guardado deja archivos corruptos en cortes de luz.
- [ ] CLI usable sin entorno gráfico; documentación breve incluida.

---

## 7) Fase E — Instrumentación, Debug y Export extendido

Archivos: `fsm_runtime_bridge.py`, `render/debug/*`, `data/fsm/*`

- [ ] Ring-buffer de trazas por entidad (`frame, evt, from, to, tr_id, guard_result, ms`).
- [ ] Overlay de depuración con filtro por entidad/set.
- [ ] Contadores de rendimiento (transiciones evaluadas, tomadas, descartadas por guard/cooldown).
- [ ] Export opcional: `TRANSITIONS_DICT_BY_SET[set_id][tr_id] -> {from,to,event,priority,cooldown,...}` en `fsm_ids.json` (feature flag).

Criterios de aceptación:
- [ ] Diagnóstico de problemas sin necesidad de logs verbosos.
- [ ] Export extendido no rompe consumidores existentes.

---

## 8) Fase F — Refactors seguros + Panel de Assignments

Archivos: `src/roguelike_editors/fsm/sets_panel/*`, `.../assignments_panel/*` (nuevo)

- [ ] Panel para `assignments.json`: mapear archetypes/eids → set, validar referencias.
- [ ] Operaciones masivas: reasignar un conjunto de entidades a otro set.
- [ ] Refactors transversales (rename set/state) con previews y efectos en cascada (transacciones).

Criterios de aceptación:
- [ ] JSONs siempre consistentes tras refactors.

---

## 9) Fase G — Rendimiento y Tests

Rendimiento:
- [ ] Precalentamiento de caches al cargar partida/escena.
- [ ] Medición de latencia por frame del evaluador (objetivo < 50µs/entidad con 5 transiciones/estado en HW medio).

Testing:
- [ ] Golden tests de schema por versión y round-trip (load→save→diff igual salvo orden permitido).
- [ ] Tests del evaluador: prioridades, cooldowns, globales, timers; fuzzing de guards.
- [ ] Tests de Editor: simulación, refactors rename/clone, linter UI.

---

## 10) Catálogo de Eventos / Actions / Guards (base)

- [ ] Definir catálogo de eventos comunes: `OnHit, OnDeath, after_attack, on_clear, on_cooldown, on_input, on_timer`.
- [ ] Action Registry: `play_anim`, `emit_event`, `set_bb`, `inc_bb`, `spawn_fx`, `publish_bus`, ... (documentar firma).
- [ ] Guard AST nodos: `and, or, not, cmp(op, left, right), get(path in bb/ctx/evt), const`.

---

## 11) Riesgos y mitigación

- __Breaking changes de schema__: migraciones idempotentes + feature flags + validación dual.
- __Expresividad de guards__: empezar simple (AST básica) y ampliar por necesidades reales.
- __Tempestades de eventos__: cooldowns y backpressure por cola.
- __Deadlocks/ciclos__: linter y simulación deben señalarlos.

---

## 12) Checklist de entrega por fase

Fase A
- [x] Schema V1 implementado + migraciones
- [ ] Linter ampliado + UI badges con tooltips
- [x] Validación sobre sets actuales

Fase B
- [ ] Precompilador `transitions_by_from` + `global_transitions`
- [ ] Evaluador con prioridad/guards/cooldowns
- [ ] Integración con cola de eventos ECS

Fase C
- [ ] Propiedades tipadas (event/priority/cooldown/guard/actions)
- [ ] Modo simulación en Graph Panel
- [ ] Refactors seguros (rename/clonar)

Fase D
- [ ] Atomic write + backups + detect conflicts
- [ ] CLI `lint/migrate/export/check-refs`

Fase E
- [ ] Trazas y overlay + métricas
- [ ] Export extendido opcional

Fase F
- [ ] Panel de Assignments operativo
- [ ] Reasignación masiva y validaciones

Fase G
- [ ] Benchmarks y límites aceptados
- [ ] Suites de tests verdes

---

## 13) Referencias de código a tocar

- `src/roguelike_editors/fsm/services/fsm_persistence.py`
- `src/roguelike_editors/fsm/services/fsm_runtime_bridge.py`
- `data/fsm/schema.json`, `data/fsm/sets.json`, `data/fsm/assignments.json`
- `src/roguelike_editors/fsm/*` (properties panel, graph panel, sets panel, toolbar)
- `roguelike_game/ecs/systems/fsm/*` (si se separa un evaluador dedicado)
- `data/fsm/fsm_ids.json` (export extendido opcional)

---

## 14) Notas de implementación

- Mantener compatibilidad progresiva: activar nuevas características tras migrar y testear.
- Registrar decisiones en commits/README_FSM para trazabilidad.
- Usar feature flags para: global transitions, export extendido, action registry runtime.

