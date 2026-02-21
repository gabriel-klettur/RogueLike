# Editor de Entidades (Entities Editor)

El Editor de Entidades permite inspeccionar, crear y modificar entidades del juego (monstruos y jugadores), así como gestionar sus propiedades, assets y acciones de spawn/delete en el mapa. Está implementado con una arquitectura MVC por panel, con enrutamiento de eventos y un sistema de histórico (undo/redo).


## Objetivos
- Proveer una UI modular y extensible para trabajar con entidades sin reiniciar el juego.
- Integrarse con el runtime para reflejar cambios de inmediato (sprites, propiedades, caches).
- Mantener consistencia entre datos en memoria y JSON (persistencia explícita y nulos intencionales).


## Estructura del módulo
Directorio: `src/roguelike_editors/entities/`

- Núcleo del editor (MVC):
  - `entities_editor_controller.py`
  - `entities_editor_model.py`
  - `entities_editor_view.py`
  - `entities_editor_events.py`

- Panel de título:
  - `entities_title/`
    - `entities_title_controller.py`, `entities_title_model.py`, `entities_title_view.py`, `entities_title_events.py`

- Toolbar (herramientas globales del editor):
  - `entities_tool_bar_panel/`
    - `entities_tool_bar_panel_controller.py`, `..._model.py`, `..._view.py`, `..._events.py`

- Panel Add/Remove (modos de spawn/delete):
  - `entities_add_remove_panel/`
    - `entities_add_remove_panel_controller.py`, `..._model.py`, `..._view.py`, `..._events.py`

- Picker de entidades (lista/grid de entidades):
  - `entities_picker_panel/`
    - `entities_picker_panel_controller.py`, `..._model.py`, `..._view.py`, `..._events.py`

- Panel de Propiedades (datos y assets de la entidad seleccionada):
  - `entities_properties_panel/`
    - `entities_properties_panel_controller.py`, `..._model.py`, `..._view.py`, `..._events.py`
    - Submódulos:
      - `entities_properties_info_panel/` (resumen/metadata)
      - `entities_state_tabs/` (pestañas por secciones/estado)
      - `entities_type_assets/` (selección por tipo de asset)
      - `entities_assets_subtabs/` (subpestañas de assets)
      - `entities_assets_grid_panel/` (grid/miniaturas de assets)
    - Servicios internos del panel de propiedades:
      - `services/` → `assets_constants.py`, `assets_helpers.py`, `assets_maps.py`, `entity_flatten.py`, `entity_properties_service.py`, `state_tabs_helpers.py`, `stats_templates.py`

- Servicios compartidos del editor:
  - `services/` → `camera_helpers.py`, `commands.py` (undo/redo), `constants.py`, `ecs_snapshot.py`, `entity_lookup.py`, `history.py`, `spawn_services.py`, `ui_helpers.py`


## Arquitectura y flujo
- __MVC por panel__: Cada panel tiene `model`, `view`, `controller` y `events` para aislar responsabilidades.
- __Enrutamiento de eventos__: `entities_editor_events.py` delega a toolbar, add/remove, picker y propiedades según el área del cursor y visibilidad.
- __UI Blockers__: Todos los paneles registran rectángulos de bloqueo para suprimir hovers/acciones del mundo bajo la UI.
- __Histórico (Undo/Redo)__: Implementado vía `services/history.py` y `services/commands.py` con comandos para edición de propiedades, rename, etc.
- __Integración ECS/Runtime__: Cambios se reflejan en caches y entidades en el mundo usando `services/ecs_snapshot.py`, `spawn_services.py`, y servicios de assets.


## Funcionalidades principales
- __Listado y selección de entidades__ (Picker):
  - Grid con hover/selección consistente y scroll.
  - Filtrado de entradas `__pending__` para no mostrar entidades nuevas hasta confirmar (flujo de "Add on system").

- __Propiedades de entidad__ (Properties Panel):
  - Render de propiedades agrupadas; soporte de claves punteadas para editar anidados (ej. `basic_trail.interval`).
  - Plantillas de stats por tipo: `PLAYER_STATS_TEMPLATE` y `MONSTER_STATS_TEMPLATE`; se muestran llaves con `None` cuando faltan.
  - Integración de assets por tipo y subpestañas con grid de miniaturas.
  - Renombrado de entidad (monstruos): actualiza JSON, selección y caches sin reordenar erróneamente el archivo.

- __Assets y miniaturas__:
  - Carga robusta con placeholders si faltan assets; evita errores cuando una entidad es nueva y pendiente.
  - Tras cambios de assets se refresca el grid y se limpian cachés de thumbnails para ver el resultado de inmediato.

- __Spawn/Delete (Add/Remove panel)__:
  - Modos para colocar o eliminar entidades en el mapa mediante `spawn_services.py` y helpers de UI.
  - Al entrar a spawn/delete se ocultan paneles no pertinentes (p. ej., picker de assets) para enfocar la acción.

- __Hover en el mundo y feedback visual__:
  - Resaltado de entidades al pasar el cursor desde el editor.
  - Correcciones para no activar hover fuera/encima de paneles.

- __Persistencia y validación__:
  - Escritura "null-intencional" en JSON (relleno de esqueletos) para distinguir campos ausentes de valores por defecto.
  - Sincronización inmediata de datos en memoria y disco; las vistas se actualizan al confirmar.

- __Atajos__:
  - Toggle del editor: F5.
  - Navegación estándar en el picker (mouse y rueda). [Atajos adicionales específicos se documentan en los eventos del picker/properties].


## Flujo de trabajo típico
1. __Abrir el editor__ con F5.
2. __Seleccionar__ una entidad en el `entities_picker_panel/`.
3. __Editar propiedades__ en `entities_properties_panel/` (stats, assets, subtabs).
4. __Confirmar__ para persistir; el runtime refleja cambios (sprites, caches, instancias en mapa si aplica).
5. __Usar undo/redo__ para revertir/aplicar cambios recientes.
6. Opcional: __Spawn/Delete__ desde `entities_add_remove_panel/`.


## Tests
- Suite en `tests/roguelike_editors/entities/`:
  - Picker Panel: eventos (tabs, selección/hover, drag, teclas, clicks fuera).
  - Title Panel: render y handle_event.
  - Servicios: `history.py`, `camera_helpers.py`.


## Convenciones y buenas prácticas
- __No mezclar lógica de UI con modelo__: use `controller` para orquestar y `events` para mapear Pygame a acciones.
- __Siempre registrar UI blockers__ cuando un panel es visible.
- __Usar comandos__ (`services/commands.py`) para acciones que deben poder deshacerse.
- __Mantener claves punteadas y plantillas__ al añadir nuevas propiedades para una experiencia consistente en el panel.


## Extensión
Para añadir un nuevo panel:
1. Crear carpeta `nuevo_panel/` con `..._model.py`, `..._view.py`, `..._controller.py`, `..._events.py`.
2. Integrar enrutamiento en `entities_editor_events.py` y render en `entities_editor_view.py`.
3. Registrar su rectángulo en el sistema de UI blockers.
4. Si edita datos, exponer comandos undo/redo y actualizar caches/servicios correspondientes.


## Entradas relacionadas en el código
- Top-level servicios del editor: `entities/services/`
- Servicios del panel de propiedades: `entities/entities_properties_panel/services/`
- Comandos de edición y rename: ver `services/commands.py` y controladores del Properties Panel.
- Refrescos de assets/grid: controladores bajo `entities_properties_panel/` y `entities_assets_grid_panel/`.


## Notas
- Este editor convive con otros editores (Tiles, Items, FSM). Se comparte la política de __bloqueo de UI__ para evitar fugas de hover/click al mapa cuando el cursor está sobre paneles.
- Cambios relevantes históricos (resumen): hover estable, grid de assets resistente a faltantes, rename consistente, refresco inmediato de miniaturas, y persistencia con nulos explícitos.
