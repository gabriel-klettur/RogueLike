# FSM Editor — Diseño, arquitectura y roadmap

Este documento define el diseño profesional del editor de Máquinas de Estados Finitos (FSM) para el proyecto. El objetivo es crear, conectar, modificar y eliminar estados y transiciones dentro de “conjuntos de FSM” (FSM Sets) y asignarlos a entidades del juego.

## Resumen rápido
El FSM Editor permite autoría visual de comportamientos con nodos y transiciones reutilizables (FSM Sets) validados por esquema y con recarga en caliente del runtime.

## Características destacadas
- __Edición visual__: nodos/estados y aristas/transiciones dirigidas con flechas, ancladas a bordes, soportando aristas paralelas curvadas, auto-bucles y etiquetas.
- __Paneles dedicados__: Sets (lista/duplicar/renombrar), Graph (canvas con herramientas), Properties (estado/transición/acciones/condiciones/blackboard) con hints.
- __Validación y linter__: validación por `schema.json`; el Properties Panel muestra contadores de warnings/errores y tooltips por fila.
- __Persistencia profesional__: normaliza IDs, valida, y exporta `data/fsm/fsm_ids.json` (SET_IDS/STATES_BY_SET/TRANSITIONS_BY_SET) para tooling/runtime.
- __Hot-reload__: guardado (Ctrl+S) publica versión y el runtime recarga sin reiniciar.
- __UX afinada__: pan solo con MMB; zoom con rueda del mouse y teclas `+`/`-`; UI blockers para evitar “hover bleed-through”.
- __Integración UI__: al abrir el editor se ocultan minimapa y help overlay para no interferir con la vista del grafo.

## Objetivos
- __FSM Sets__: Agrupar la definición completa de una FSM (estados + transiciones + propiedades) con un `id` único reutilizable.
- __Asignación a entidades__: Permitir asignar un `fsm_set_id` a entidades (monstruos/jugadores) desde editores o archivos de config.
- __Edición visual__: Crear, conectar (edges), desconectar, mover, duplicar estados; editar propiedades de estados y transiciones.
- __Persistencia__: Guardar/leer desde JSON validado por esquema. Recarga en caliente al guardar.
- __Deshacer/Rehacer__: Historial de comandos para todas las acciones del editor.
- __Reutilización__: Basarse en componentes existentes (TitleBar, ToolbarView, PickerPanel, UI blockers, servicios comunes).

## Conceptos clave
- __FSM Set__: Una colección nombrada de estados y transiciones. `id` único global.
- __State__: Nodo con `id`, `label`, `pos` (x,y), `entry_actions`, `exit_actions`, `update_actions`, `properties`.
- __Transition__: Arista con `from`, `to`, `conditions` (guards), `actions`, `priority` y `kind` opcional (e.g., on_event, timed).
- __Blackboard__: Diccionario de datos asociado al runtime de la FSM por entidad (valores evaluables por guards/actions).

## Almacenamiento y esquema
- __Directorio__: `data/fsm/`
- __Archivos__:
  - `data/fsm/sets.json`: Catálogo de FSM Sets.
  - `data/fsm/schema.json`: Esquema JSON para validar `sets.json`.
  - Opcional: `data/fsm/assignments.json` si no se quiere tocar configs de entidades (mapea `entity_id -> fsm_set_id`).

Ejemplo mínimo de `sets.json`:
```json
{
  "version": 1,
  "sets": [
    {
      "id": "enemy_basic",
      "label": "Enemy Basic",
      "blackboard_template": {"aggro_range": 6, "hp_threshold": 0.3},
      "states": [
        {"id": "idle",   "label": "Idle",   "pos": [120, 80],  "properties": {}, "entry_actions": [], "exit_actions": [], "update_actions": []},
        {"id": "chase",  "label": "Chase",  "pos": [360, 80],  "properties": {"speed": 1.2}, "entry_actions": [], "exit_actions": [], "update_actions": []},
        {"id": "attack", "label": "Attack", "pos": [360, 240], "properties": {"cooldown": 0.8}, "entry_actions": [], "exit_actions": [], "update_actions": []}
      ],
      "transitions": [
        {"id": "t1", "from": "idle",   "to": "chase",  "conditions": ["player_in_range(aggro_range)"]},
        {"id": "t2", "from": "chase",  "to": "attack", "conditions": ["distance(player) < 1.0"], "actions": ["face_target(player)"]},
        {"id": "t3", "from": "attack", "to": "chase",  "conditions": ["cooldown_ready()"]}
      ],
      "initial_state": "idle"
    }
  ]
}
```

Notas:
- `blackboard_template` define claves iniciales disponibles para guards/actions.
- `properties` en estado permite parámetros (p. ej., `speed`, `cooldown`).
- Transiciones aceptan `conditions` como strings evaluables por el runtime (o referencias a funciones registradas).


## Integración con runtime (ECS)
- __Componente__: `FSMComponent` por entidad con `fsm_set_id`, `current_state`, `blackboard`.
- __Sistemas__: `FSMUpdateSystem` (evalúa guards, ejecuta actions enter/exit/update, hace transición).
- __Asignación__:
  - Opción A: añadir `fsm_set` en configs de entidades (monstruos/jugadores) y cargar en spawn.
  - Opción B: mantener `data/fsm/assignments.json` y resolver al spawn.
- __Recarga__: al guardar `sets.json`, el editor notifica un `FSM_RELOAD` para reconstruir máquinas activas (similar a otras recargas existentes). Atajo sugerido: Ctrl+S dentro del editor.
- __Toggle del editor__: F12 (ya usado internamente como “FSM Editor”).
- __Visibilidad UI__: cuando el FSM Editor está visible, se ocultan minimapa y help overlay para evitar solapamiento (ver `src/roguelike_game/managers/core/render_manager.py`).

Skeleton conceptual de runtime (ilustrativo):
```python
class FSMComponent:
    def __init__(self, fsm_set_id: str, initial_state: str, blackboard: dict):
        self.fsm_set_id = fsm_set_id
        self.current_state = initial_state
        self.blackboard = dict(blackboard)

class FSMUpdateSystem:
    def update(self, world, dt):
        for e in world.with_component(FSMComponent):
            self._update_entity_fsm(e, dt)
```

## Estado actual y alineación con el FSM del proyecto
- __Infra de editor existente__: el proyecto ya cuenta con utilidades del editor (p. ej., `FMSModel`, `FMSController` y `FMSEventSpy`) que encapsulan el toggle del editor con F12 y el ruteo de eventos. El FSM Editor se apoyará en esta base para integrarse con el loop, el input y el render.
- __Objetivo__: no reemplazar el FSM runtime existente, sino profesionalizar la autoría y la interoperabilidad: edición visual, persistencia validada, y puente de recarga hacia el runtime actual.

Acciones concretas de alineación:
- Mantener F12 como toggle y delegar input a un `FMSEventRouter` (o `FMSEventSpy` existente) que distribuya a paneles.
- Añadir un `fsm_runtime_bridge.py` que traduzca los FSM Sets del editor al formato que consume el runtime actual (resolución de guards/actions registradas, ids y estructuras).
- Exponer un evento único de recarga (p. ej., `FSM_SETS_RELOAD`) que el runtime escuche para rehidratar FSMs activos de forma segura.


## Arquitectura del Editor (MVC)
- __Carpeta__: `src/roguelike_editors/fsm/`
- __Controlador principal__: `fsm_editor_controller.py` (orquesta paneles, estado global, persistencia, historial, recarga)
- __Vista principal__: `fsm_editor_view.py` (layout, dibuja paneles y canvas)
- __Eventos__: `fsm_editor_events.py` (delegación a paneles; teclado/ratón)
- __Paneles__:
  - `fsm_title/` ya existente: título con `TitleBar`.
  - `fsm_toolbar/`: usa `ToolbarView` con botones: ['undo', 'redo', 'sets_list', 'sets_entities_assignment', 'sets_animation_assignment', 'set_properties'].
  - `fsm_sets_panel/`: lista de FSM Sets con `PickerPanel` (crear/duplicar/eliminar/renombrar set).
  - `fsm_graph_panel/`: canvas nodal (estados como nodos, transiciones como aristas). Soporta pan/zoom, selección, arrastre, conexión. Incluye una barra de herramientas horizontal superior con botones: ['select', 'connect', 'delete', 'zoom_in', 'zoom_out', 'mark_ini', 'mark_end'].
    - Render dirigido: aristas con flechas (arrowheads) y anclaje a bordes de nodos (no centro a centro).
    - Aristas paralelas curvadas, auto-bucles y etiquetas por arista; estilos por transición vía claves `style.*` (color, width, head_len, head_width).
    - Leyenda de colores bajo la barra para estados especiales (p. ej., Damage/Alert/External) con botón clicable para minimizar/expandir.
  - `fsm_properties_panel/`: propiedades del estado o transición seleccionados; pestañas: ['state', 'transition', 'actions', 'conditions', 'blackboard'].
- __Servicios__ (nuevo `services/`):
  - `fsm_persistence.py`: load/save/validate contra `schema.json` y export de `data/fsm/fsm_ids.json` (SET_IDS, STATES_BY_SET, TRANSITIONS_BY_SET).
  - `fsm_graph_layout.py`: snap a grid, auto-layout inicial, routing sencillo de aristas.
  - `fsm_history.py`: comandos (Command pattern) para undo/redo.
  - `fsm_id.py`: generación/validación de ids únicos.
  - `fsm_runtime_bridge.py`: señalización de reload y mapeo set->runtime. Expone helpers para el índice de ids:
    - `get_ids_index()` → objeto con `SET_IDS`, `STATES_BY_SET`, `TRANSITIONS_BY_SET`.
    - `get_set_ids()` → lista de ids de sets.
    - `get_state_ids(set_id)` → lista de ids de estados por set.
    - `get_transition_ids(set_id)` → lista de ids de transiciones por set.
- __UI Blockers__: todos los paneles registran `ui_blocker` rect para suprimir hovers/drag del canvas cuando el ratón está sobre UI.

### fsm_toolbar (implementado)
- __Ubicación__: `src/roguelike_editors/fsm/fsm_toolbar/` con MVC: `fsm_toolbar_model.py`, `fsm_toolbar_view.py`, `fsm_toolbar_controller.py`, `fsm_toolbar_events.py`.
- __Botones por defecto__: definidos en `DEFAULT_BUTTONS` del `FsmToolbarModel`:
  `['undo','redo','sets_list','sets_entities_assignment','sets_animation_assignment','set_properties']`.
- __Iconos__: temporalmente todos los botones usan el mismo icono genérico `assets/ui/generic_icon.png` a través de `IconCache`.
- __Vista__: `FsmToolbarView` envuelve `ToolbarView` (vertical). Propiedades iniciales: `anchor=(20,60)`, `size=32`, `padding=8`.
- __Interacción__:
  - LMB sobre un botón: activa la herramienta (`model.active_tool`) cuando aplica (p. ej., `sets_list`).
  - RMB sobre el panel: permite arrastrar el toolbar (delegado a `DraggablePanel`).
  - ESC: limpia la herramienta activa (`active_tool=None`).
  - Atajo: tecla `S` alterna la visibilidad de `sets_list`.
- __API del controlador__: `FsmToolbarController.is_active(tool)` y `set_active(tool|None)` para que la vista pinte selección y el resto del editor pueda consultar el estado.
- __Integración típica__:
  - Render: `rect = toolbar_controller.render(screen)` devuelve el `pygame.Rect` del panel para layout y registro de UI blockers (el `ToolbarView` ya invoca `register_blocker`).
  - Eventos: llamar primero `toolbar_controller.handle_event(event)` para que gestione drag/clicks del toolbar antes que el canvas.
- __Siguientes pasos__: reemplazar iconos por específicos por herramienta, añadir atajos de teclado (p. ej., `V` select, `C` connect, `Del` delete, `Ctrl+Z/Y` undo/redo) y tooltips.


## Pipeline Autoría → Build → Runtime
1. __Autoría__: el editor modifica un AST declarativo en memoria con historial de comandos.
2. __Validación__: al guardar o previo a compilar, `fsm_persistence.validate(schema)`.
3. __Build/Compilación__ (determinística):
   - Normalizar ids (kebab/snake), labels opcionales.
   - Preindexar: `state_id -> index`, `adjacency[from_index] -> [to_index]`.
   - Ordenar transiciones por `priority` (desc) y posición de adición (estable).
   - Resolver guards/actions a punteros de función registrados (sin eval).
   - Congelar estructuras (tuplas, frozenset) para hashing/caching.
4. __Runtime Bridge__: publicar versión (`FSM_SETS_VERSION++`) y emitir evento de recarga con snapshot inmutable del build.
5. __Runtime__: sistemas consumen la versión más reciente mediante handle idempotente (si versión no cambia, no reconstruye).


## Interacciones y UX
- __Pan__: solo botón medio (MMB). Seguridad: liberación forzada si MMB deja de estar presionado.
- __Zoom__: rueda del mouse cuando el puntero está sobre el canvas (centra en el puntero); teclas '+'/'-' (fila numérica o keypad; '+' con Shift sobre '=') centran en el canvas.
- __Selección__: LMB en nodo/arista. Arrastre para mover nodo; Shift+LMB para multi-selección (marquee).
- __Herramienta Conectar__: Activa el modo “connect”; LMB en origen, LMB en destino para crear transición.
- __Eliminar__: tecla Supr o botón “delete” sobre selección elimina estados/aristas (confirmación si afecta transiciones).
- __Edición inline de etiquetas__: doble click sobre estado o etiqueta de transición para editar el texto.
- __Editar propiedades__: panel derecho; Enter aplica cambios; Ctrl+Z/Ctrl+Y para undo/redo.
- __Guardar__: Ctrl+S valida y persiste `sets.json` y notifica reload.

Atajos propuestos adicionales:
- __Ctrl+R__: reconstruir (build) sin persistir; útil para detectar errores temprano.
- __F3__: recargar runtime desde disco (pull) cuando el runtime cambie fuera del editor.


## Flujo de trabajo típico
1. __Crear Set__: botón “+ Set” en `fsm_sets_panel` → asignar `id` y label.
2. __Añadir estados__: click “+ State” o doble click en canvas para crear nodo en posición.
3. __Conectar__: activar “connect” → click origen → click destino → definir condiciones/acciones.
4. __Configurar propiedades__: seleccionar estado/transición → editar en `fsm_properties_panel` (propiedades, actions, guards).
5. __Inicial__: marcar `initial_state` en el estado deseado.
6. __Asignar__: en editor de entidades, escoger `fsm_set` para la entidad, o usar `assignments.json`.
7. __Probar__: guardar (Ctrl+S) → runtime recarga y se observa el comportamiento.


## Validación y errores
- Al guardar: validar contra `schema.json`; mostrar lista de errores con referencias a set/state/transition.
- Restricciones: ids únicos por set, `initial_state` existente, transiciones referencian estados válidos.
- Linter opcional: advertencias por estados sin salidas, transiciones sin condiciones, ciclos triviales.
 - UI: el Properties Panel muestra badges de conteo (warnings/errores) y tooltips/hints por fila para facilitar la corrección.

Validaciones adicionales para profesionalizar:
- __Tipos estrictos__: `properties` tipadas por estado (schema enriquecido por tipo de estado).
- __No-orfanatos__: sin transiciones apuntando a estados inexistentes; detectar islas.
- __Un solo inicial__: exactamente un `initial_state` por set.
- __Prioridades consistentes__: sin empates ambiguos (o resolver por orden de inserción documentado).


## Pruebas
- __Unitarias__: `services/fsm_persistence_test.py`, `fsm_graph_layout_test.py`, `fsm_history_test.py`, validación de esquema.
- __Integración__: abrir editor, crear set, añadir estado, conectar, guardar, recargar.
- __Golden tests__: serialización determinista de `sets.json`.

Cobertura extra sugerida:
- __Build determinista__: mismo input → mismo output binario/hash.
- __Guards/Actions registry__: registro, resolución, y errores bien formados.
- __Hot-reload seguro__: rehidratación sin perder estado cuando no cambia el grafo; migración de estado cuando cambia (ver abajo).


## Roadmap incremental
- __Fase 1__: Skeleton del editor (título, toolbar, sets panel, canvas vacío), carga/guardado de `sets.json`, toggle con F12.
- __Fase 2__: Nodos básicos (crear/mover/seleccionar), conectar transiciones, eliminar, undo/redo, validación y guardado.
- __Fase 3__: Properties Panel completo (state/transition/actions/conditions), blackboard_template por set.
- __Fase 4__: Auto-layout, zoom, marquee, duplicar nodos, multi-selección.
- __Fase 5__: Integración completa con runtime (FSMComponent, FSMUpdateSystem, reload), asignación desde editor de entidades.
- __Fase 6__: Linter/inspector, minimapa, búsqueda, duplicación de sets, import/export parcial.

Hitos técnicos clave:
- __Registry tipado de Guards/Actions__.
- __Compilación a representación inmutable preindexada__.
- __Versionado y migración de estado en caliente__.


## Reutilización de componentes existentes
- __Title__: usar `TitleBar` como en otros editores.
- __Toolbar__: `ToolbarView` con orden consistente; atajos estándar (Ctrl+Z/Ctrl+Y/Ctrl+S).
- __PickerPanel__: para listar FSM Sets con scroll, hover, selección, drag opcional.
- __UI blockers__: seguir patrón de Items/Tiles/Entities para evitar “hover bleed-through”.


## Decisiones de diseño
- __Formato JSON validado__: facilita tooling, diffs y migraciones.
- __Command pattern para edición__: coherente con undo/redo y con otros editores.
- __Separación editor/runtime__: el editor no ejecuta la FSM; se limita a editar y notificar reload.
- __Ids estables y posiciones absolutas__: layout reproducible y merges limpios.

### Guards/Actions: API profesional sin eval
- __Registro explícito__: funciones de guard/action se registran con decoradores y nombres estables.
```python
# services/fsm_registry.py
GUARDS = {}
ACTIONS = {}

def guard(name):
    def wrap(fn): GUARDS[name] = fn; return fn
    return wrap

def action(name):
    def wrap(fn): ACTIONS[name] = fn; return fn
    return wrap

@guard('player_in_range')
def player_in_range(bb, ctx, *, range: float):
    # ctx: referencias (world, self, targets)
    return ctx.distance_to_player() <= range

@action('face_target')
def face_target(bb, ctx, *, target: str = 'player'):
    ctx.face(target)
```

- __JSON estructurado__: en transiciones, condiciones/acciones como objetos con `name` y `args` (sin strings evaluadas).
```json
{
  "id": "t1",
  "from": "idle",
  "to": "chase",
  "conditions": [ { "name": "player_in_range", "args": {"range": 6} } ],
  "actions": [ { "name": "face_target", "args": {"target": "player"} } ]
}
```
- __Binding en build__: `fsm_runtime_bridge` resuelve `name -> callable` y congela `args` validados.

### Migración y versionado de datos
- __`data/fsm/schema.json`__ versionado (campo `version`) con migradores en `scripts/migrate_fsm.py`.
- __Backward compatible__: aceptar tanto formato de strings (legacy) como estructurado (nuevo) vía `oneOf` en el schema.
- __Semántica de cambios__:
  - Minor: agregar propiedades/acciones; compatible.
  - Major: cambiar estructura; requiere migración automática y bump de `version`.

### Hot-reload, versionado y preservación de estado
- __`FSM_SETS_VERSION`__ monotónico: cada build incrementa y publica.
- __Rehidratación__: si el `current_state` sigue existiendo y no hay cambio incompatible, el runtime conserva el estado y el blackboard.
- __Migración de estado__: si el estado desaparece, aplicar política configurable: fallback a `initial_state`, map de renombres o error visible.


## Extensiones futuras
- __Submáquinas (anidación)__ y estados compuestos.
- __Condiciones declarativas__ con UI para parámetros y sugerencias desde blackboard.
- __Biblioteca de acciones__ con documentación inline y ejemplos.
- __Simulador en el editor__ (play/pause/step) desconectado del mundo real.

## Asignación de FSM a entidades (profesional y escalable)
- __Fuentes de asignación__ (prioridad descendente):
  1) Runtime override temporal (debug/testing).
  2) Config de entidad concreta (players/monsters JSON).
  3) `data/fsm/assignments.json` por clase/categoría.
  4) Default global por tipo de entidad.
- __Resolución__: servicio `fsm_assignment_service.py` que, dado un `entity_id`/clase, devuelve `(fsm_set_id, initial_state, blackboard)`.
- __Editor de entidades__: exponer selector de `fsm_set` y abrir FSM Editor centrado en ese set.

## Rendimiento y escalabilidad
- __O(E * T̄)__ por frame, donde `E` = entidades con FSM y `T̄` = transiciones salientes promedio del estado actual.
- __Optimización__:
  - Preindexar transiciones por estado y prioridad.
  - Guardar closures de guards/actions con args ya validados (evitar dict lookups repetidos).
  - Reutilizar blackboards y estructuras inmutables para cache de builds.
- __Threading__: ejecución en el hilo principal; evitar locks. El build ocurre fuera del tick crítico y publica snapshot inmutable.

## Observabilidad y depuración
- __Event Spy__: hooks para escuchar enter/exit/transition en tiempo real (apoyarse en `FMSEventSpy`).
- __Overlay opcional__: mostrar estado actual encima de la entidad y transiciones disparadas (cuando el FSM Editor esté visible).
- __Trazas__ (nivel debug): guard/action con tiempos; sampling para no saturar logs.