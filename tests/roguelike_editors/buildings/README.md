# Plan de Tests del Buildings Editor

Este documento define la batería de pruebas propuesta para garantizar robustez, escalabilidad y funcionalidad del Buildings Editor.

Se apoya en la arquitectura del módulo en `src/roguelike_editors/buildings/` y en su documentación (`README_BUILDINGS.md`).

## Alcance
- Cobertura de MVC principal: `building_editor_model.py`, `building_editor_controller.py`, `building_editor_view.py`, `building_editor_events.py`.
- Paneles especializados: `buildings_tool_bar_panel/`, `buildings_add_remove_panel/`, `buildings_colliders_panel/`, `buildings_title_panel/`, `buildings_picker/`.
- Herramientas: `tools/` (Resize, Default, Split, Z top/bottom, ColliderScope, Placer, Delete).
- Utilidades y persistencia: `utils/load_buildings_from_json.py`, `utils/save_buildings_to_json.py`, `utils/zone_helpers.py`.

## Guías de configuración de tests
- Usar `pytest` con pygame en modo headless (por ejemplo, `os.environ['SDL_VIDEODRIVER']='dummy'`).
- Crear fixtures para:
  - __Camera fake__: con `zoom`, `offset_x`, `offset_y`, `apply((x,y))` y `scale((w,h))`.
  - __Superficies pygame__: inicializar mínimamente pygame para crear `pygame.Surface`.
  - __Buildings fake__: instancias con atributos mínimos (`x`, `y`, `image`, `rect`, `image_path`, `zone`, `rel_x`, `rel_y`, `split_ratio`, `z_bottom`, `z_top`, `solid`, `original_scale`, `collision_map`, `collider_scope`).
  - __Monkeypatch de persistencia__: reemplazar `save_buildings_to_json.save_buildings_to_json` para espiar llamadas sin escribir disco; inyectar `BUILDINGS_DATA_PATH` temporal cuando se verifique E2E.
- Emplear `pygame.event.Event` para sintetizar eventos y pasar listas explícitas a `BuildingEditorEventHandler.handle(camera, entities, events)`.

## Matriz de pruebas

### 1) Events router (`building_editor_events.py`)
- __[EVT-001] Panning con MMB__: DOWN(2) inicia pan, `MOUSEMOTION` ajusta `camera.offset_x/y` proporcional a `1/zoom`, UP(2) detiene.
- __[EVT-002] QUIT persiste__: si `editor.active=True`, al `pygame.QUIT` se llama a `save_buildings_to_json(..., z_state, zone_offsets)` exactamente una vez; `state.running=False`.
- __[EVT-003] ESC cierra y guarda__: al `K_ESCAPE` desactiva flags (`active=False`, limpia `selected_building/dragging/resizing/split_dragging`) y persiste a disco.
- __[EVT-004] Toggle picker con P__: `K_p` llama `controller.toggle_picker()` y alterna `editor.picker_active`.
- __[EVT-005] Reset con D__: con `hovered_building` definido y `colliders_mode=False`, `K_d` llama `default_tool.apply_reset` sobre el hovered.
- __[EVT-006] Resize con R__: `KEYDOWN K_r` invoca `controller._start_resize(...)`; `KEYUP K_r` pone `editor.resizing=False`.
- __[EVT-007] Undo Ctrl+Z__: con una eliminación en `undo_stack`, `Ctrl+Z` restaura el building en su índice y actualiza `hovered/selected`.
- __[EVT-008] Guardado Ctrl+S__: `Ctrl+S` invoca persistencia sin cerrar el editor.
- __[EVT-009] Colocar con N__: `K_n` invoca `placer_tool.place_building_at_mouse` y crece `entities.buildings`.
- __[EVT-010] Borrar con Supr__: `K_DELETE` llama `delete_tool.delete_building_at_mouse` respetando `colliders_mode`.
- __[EVT-011] Delegación de mouse__: MOUSEBUTTONDOWN/UP/MOTION delega a `controller.on_mouse_*` salvo panning o paneles que consuman.
- __[EVT-012] Persistencia tras mouse up__: en `MOUSEBUTTONUP` se guarda a JSON una única vez (después de drag/resize/split).
- __[EVT-013] Bloqueo de UI__: si `ui_blocker.is_blocked(mx,my)` entonces limpia `hovered_buildings`, `hovered_building`, `active_building` y no dibuja overlays.
- __[EVT-014] Enfoque activo en select__: en `MOUSEMOTION`, si `current_tool=='select'` y no hay `active_building`, promueve `hovered_building` a activa; la desactiva si el mouse sale de su `rect` world-space.
- __[EVT-015] Wheel cicla hovered__: con múltiples candidatos, `MOUSEWHEEL` rota `hovered_building_index` y actualiza `hovered_building`.
- __[EVT-016] Delegación a paneles__: si toolbar/add_remove/colliders están activos y consumen, el evento no continúa (verifica por retorno `True` de sus `handle_event`).

### 2) Controller (`building_editor_controller.py`)
- __[CTL-001] Respeta UI blocker__: `on_mouse_down` no modifica estado si `is_blocked` es `True`.
- __[CTL-002] Colliders mode__: cuando `editor.colliders_mode=True`, solo responde a LMB sobre el handle de `ColliderScopeTool`; resto ignorado.
- __[CTL-003] Split handle__: click en barra de split inicia `split_dragging` vía `split_tool.start_drag`.
- __[CTL-004] Toggle collider scope__: LMB sobre handle alterna `CG<->CU` en el building activo.
- __[CTL-005] Delete handle__: LMB sobre botón rojo elimina y apila `(building, idx)` en `undo_stack`.
- __[CTL-006] Reset handle__: LMB sobre reset invoca `default_tool.apply_reset`.
- __[CTL-007] Resize handle__: LMB en handle inicia `resizing`, fija `resize_origin` y `initial_size`.
- __[CTL-008] Drag con RMB__: si `hovered_building` colisiona con world pos, empieza drag; si no, busca top-most por Z (orden de lista invertido).
- __[CTL-009] Z buttons__: LMB sobre botones mueve el edificio entre capas top/bottom (verificar cambios de orden/flags según `ZTool`).
- __[CTL-010] Mouse up housekeeping__: limpia `dragging/resizing/split_dragging`, llama una sola vez `assign_zone_and_relatives(selected_building)` si existía, y limpia `selected_building`.
- __[CTL-011] Motion hover list__: `_buildings_under_mouse` devuelve lista ordenada y respeta zoom/offset; índice fuera de rango se re-normaliza.
- __[CTL-012] Update drag__: durante drag, `update()` ajusta `b.x/y` y `rect.topleft` conforme a mouse world-space; durante resize delega a `resize_tool.update_resizing`.

### 3) View (`building_editor_view.py`)
- __[VIW-001] Title bar__: siempre renderiza `BuildingsTitleView`; expone `_last_title_rect`.
- __[VIW-002] Anclaje del Picker__: si `editor.picker_manual_pos` es `None`, alinea a `editor.add_remove_panel_rect` si existe; si no, debajo del título. Si manual, usa esa posición exacta.
- __[VIW-003] Picker visible__: renderiza el picker solo cuando `editor.picker_active=True`.
- __[VIW-004] UI blocked__: si `is_blocked(mx,my)` retorna antes de dibujar overlays de edificios.
- __[VIW-005] Overlays solo en activo__: dibuja rectángulo cian/blanco y handles únicamente sobre `editor.active_building`.
- __[VIW-006] Colliders mode oculta handles__: con `colliders_mode=True` no dibuja reset/split/z, pero sí el toggle CG/CU.
- __[VIW-007] Geometría con cámara__: el rectángulo de overlay usa `camera.apply/scale` coherentemente con tamaño de imagen.

### 4) Utils y persistencia (`utils/*.py`)
- __[UTL-001] Carga básica__: `load_buildings_from_json` devuelve lista de `Building` con campos: `rel_x/rel_y`, `image_path`, `solid`, `scale`, `split_ratio`, `z_bottom/z_top`, `zone` (canónica), `collider_scope`.
- __[UTL-02] Normalización de colisiones__: cuando existen, el `collision_map` se ajusta a (ceil(h/TILE_SIZE), ceil(w/TILE_SIZE)): padding con `'.'` o truncado.
- __[UTL-003] Override CU__: si `collider_scope=='CU'` y `collision_override` en JSON, prevalece sobre mapa global y también se normaliza.
- __[UTL-004] Inyección de Z__: si se pasa `z_state`, se aplican campos Z a las instancias (`extract_z_from_json`/`inject_z_into_json`).
- __[UTL-005] Canonicalización de zonas__: entradas como "Lobby", "lobby" o variantes mapean a llaves de `global_map_settings.zone_offsets`; respeta sentinela `'no zone'`.
- __[UTL-006] Guardado__: `save_buildings_to_json` escribe todos los campos esperados; `scale` persiste tamaño actual de la imagen; incluye `collision_override` cuando `CU`.
- __[UTL-007] Asignación de zona y relativos__: `assign_zone_and_relatives` computa `zone`, `rel_x`, `rel_y` usando el centro-inferior del sprite y offsets de `global_map_settings.zone_offsets`.
- __[UTL-008] Detección de zona por px__: `detect_zone_from_px` retorna `("no zone", (0,0))` fuera de zonas válidas.
- __[UTL-009] Redimensionado de collision_map__: tras un `resize` del edificio, el grid de colisiones se re-muestrea (nearest-neighbor) para coincidir con el nuevo tamaño en tiles; dimensiones esperadas `rows=ceil(h/TILE_SIZE)`, `cols=ceil(w/TILE_SIZE)`.

### 5) Paneles
- __[PNL-001] Toolbar__: click en icono `buildings_manager` abre/cierra Picker + Add/Remove; `buildings_colliders` activa el panel de colisiones (marca `editor.colliders_mode=True`) y lo desactiva restaurando overlays. Redo es placeholder (verificar no rompe flujo).
- __[PNL-002] Add/Remove__: expone `editor.add_remove_panel_rect` tras render; verifica que `BuildingEditorView` ancle el Picker a su derecha cuando visible.
- __[PNL-003] Colliders panel__: cuando `is_active()`, su `handle_event` consume eventos y el editor suprime overlays; al desactivar, `colliders_mode=False`.
- __[PNL-004] Picker navegación__ (PENDIENTE / TEST REMOVIDO TEMPORALMENTE):
  - Motivo: el drag del panel con RMB depende de un hit-testing sensible al layout (márgenes, padding, scrollbar) que está sujeto a ajustes. El test correspondiente fue eliminado temporalmente para estabilizar la suite.
  - Alcance objetivo cuando se retome: LMB selecciona item; RMB sobre imagen inicia drag; RMB en fondo arrastra el panel; rueda mueve scroll; drag del thumb de scrollbar actualiza scroll; drop con RMB coloca building vía `placer_tool`.
  - Próximo paso: fijar contrato de métricas publicadas por `PickerView` y reescribir el test con coordenadas derivadas (márgenes, rect de grid/scrollbar) y renders previos para asegurar rects frescos.

### 6) Integración y E2E ligeros
- __[E2E-001] Mover y guardar__: simular drag con RMB y `MOUSEBUTTONUP` → verificar llamada de guardado y que `rel_x/rel_y` cambian conforme a zona detectada.
- __[E2E-002] Resize y split__: iniciar `R`, mover, soltar `R`; arrastrar split; en UP guardar; verificar `scale` y `split_ratio` en JSON.
- __[E2E-003] Z-order__: clicks en botones z-top/z-bottom persisten `z_top/z_bottom` adecuadamente.
- __[E2E-004] CG/CU__: alternar collider scope y guardar; para `CU` debe aparecer `collision_override` con dimensiones consistentes.
- __[E2E-005] Colisiones tras resize__: después de un resize, verificar que `collision_map` del building activo coincide en dimensiones con su nueva imagen en tiles y que, si `collider_scope=='CU'`, el guardado incluye `collision_override` re-muestreado.

## Pruebas de escalabilidad y rendimiento
- __[PERF-001] Muchas instancias__: con 1.000 buildings en `entities.buildings`, mover el mouse no debe degradar por encima de un umbral (p.ej., tiempo promedio por `_buildings_under_mouse` < X ms en lote sintético).
- __[PERF-002] Picker grande__: con >500 assets simulados, la rueda y el scrollbar deben seguir respondiendo fluidamente; no debe explotar memoria al render (comprobar que solo se dibujan elementos visibles). 
- __[PERF-003] Persistencia masiva__: guardar 1.000 entries no debe fallar ni exceder tiempo límite razonable; JSON válido.

## Casos borde y robustez
- __[RBT-001] UI blocker__: no hay “bleed-through” de hover/active al pasar por encima de paneles.
- __[RBT-002] Colliders activos__: con panel de colisiones activo, ninguna herramienta (reset/split/resize/z/delete) responde, excepto el toggle CG/CU.
- __[RBT-003] Hover múltiple__: wheel rota correctamente el índice circularmente, sin desbordes.
- __[RBT-004] Undo vacío__: `Ctrl+Z` sin elementos en `undo_stack` no falla.
- __[RBT-005] Zonas desconocidas__: guardar/cargar zonas no presentes en `zone_offsets` emite warning pero no rompe; respeta `'no zone'`.
- __[RBT-006] JSON corrupto__: `load_buildings_from_json` maneja `JSONDecodeError` devolviendo `[]` y logueando.
- __[RBT-007] Collisions sin columnas/filas__: normalización cuando `collision` está vacío o irregular.

### 7) Manager / Integración con Renderer
- __[MGR-001] Renderizado vía Manager__: verificar que `Renderer` invoca `BuildingEditorManager.render()` y que ello garantiza que el toolbar del Buildings Editor se dibuja al abrir el editor (no llamar directamente a `BuildingEditorView.render()`).
- __[MGR-002] Centrando toolbar__: comprobar que el centrado horizontal del toolbar usa `panel.surface.get_width()` para alinearse bajo el título (no ancho fijo).
- __[MGR-003] Toggled de paneles desde toolbar__: al hacer click en `buildings_manager`, el `Manager` abre/cierra en tándem el Add/Remove Panel y el Picker, y desactiva el panel de colisiones si estaba activo.
- __[MGR-004] Colliders Panel orchestration__: al activar `buildings_colliders` desde el toolbar, el `Manager` activa el panel de colisiones y el editor suprime overlays; al desactivar, los restaura.

## Estructura sugerida de archivos de test
- `tests/roguelike_editors/buildings/test_events.py` — EVT-001..016
- `tests/roguelike_editors/buildings/test_controller.py` — CTL-001..012
- `tests/roguelike_editors/buildings/test_view.py` — VIW-001..007
- `tests/roguelike_editors/buildings/test_utils_persistence.py` — UTL-001..008
- `tests/roguelike_editors/buildings/test_panels.py` — PNL-001..004
- `tests/roguelike_editors/buildings/test_e2e_smoke.py` — E2E-001..004
- `tests/roguelike_editors/buildings/test_perf.py` — PERF-001..003 (marcar como `@pytest.mark.slow`)

## Notas de implementación
- Reutilizar patrones de tests de otros editores (Tiles/Entities) para fake pygame, UI blocker y toolbar.
- Preferir spies/monkeypatch sobre IO real; cuando se pruebe E2E, usar `tmp_path` y redirigir `BUILDINGS_DATA_PATH`.
- Documentar en cada test el ID (p.ej., `[EVT-001]`) para trazar cobertura con esta matriz.
