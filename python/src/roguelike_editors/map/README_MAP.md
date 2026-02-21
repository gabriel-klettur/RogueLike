# Editor de Mapas (Map Editor)

El Editor de Mapas permite gestionar zonas (areas lógicas del mundo), pintar tiles en overlays, y editar colisiones/capas sin reiniciar el juego. Está implementado en Pygame con una arquitectura MVC por panel, enrutamiento de eventos y ejecución asíncrona para operaciones costosas (como pintura masiva de tiles) con soporte de histórico (undo/redo) cuando aplica.


## Objetivos
- Proveer herramientas para: gestión de zonas, pintura de tiles, edición/limpieza de colliders y visibilidad de capas.
- Integración con el runtime del mapa para ver cambios de inmediato (actualización de chunks/overlays mientras se pinta).
- Mantener persistencia explícita de zonas y capas con helpers de carga/guardado.


## Estructura del módulo
Directorio: `src/roguelike_editors/map/`

- Núcleo del editor:
  - `map_editor_controller.py`
  - `map_editor_view.py`
  - `map_editor_events.py`
  - `map_editor_state.py`

- Panel de título:
  - `map_title_panel/`
    - `map_title_view.py`

- Toolbar (herramientas del editor): `map_tool_bar_panel/`
  - Núcleo MVC de la toolbar: `map_tool_bar_panel_controller.py`, `..._model.py`, `..._view.py`, `..._events.py`
  - Herramientas específicas (cada una con su mini-MVC):
    - `add_zone/` (crear zonas)
    - `delete_zone/` (eliminar zonas con confirmación)
    - `view_layers/` (visibilidad de capas)
    - `paint_tiles/` (pintar tiles en overlays, ejecución asíncrona)
    - `paint_colliders/` (pintar colliders)
    - `clear_colliders/` (limpiar colliders)

- Servicios y comandos:
  - `services/overlay_service.py` → utilidades como `set_overlay_cell(...)`, `merge_zone_to_world(...)`.
  - `commands/paint_tiles_command.py` → comando undoable para pintura de tiles.


## Arquitectura y flujo
- **MVC por panel**: el núcleo del editor (controller/view/events/state) y la toolbar están desacoplados. Cada herramienta de la toolbar es un submódulo con su propio ciclo MVC.
- **Enrutamiento de eventos**: `map_editor_events.py` captura eventos Pygame y delega en orden: zoom/panning → diálogos/renombrado → toolbar → selección de zona → ejecución de herramientas.
- **Ejecución asíncrona**: herramientas como `paint_tiles` se ejecutan en lotes utilizando `TILE_PAINT_BATCH` y `TILE_PAINT_TICK` (ver `roguelike_engine.config.config_editor`). La vista actualiza chunks de forma incremental y muestra progreso.
- **Histórico (Undo/Redo)**: atajos globales invocan `_perform_undo/_perform_redo`; la pintura de tiles usa `PaintTilesCommand` para deshacer/rehacer cuando corresponde.
- **UI Blockers**: se respetan zonas UI (toolbar, diálogos) usando `roguelike_ui.ui_blocker.is_blocked(...)` para evitar que el mundo reciba eventos mientras se interactúa con la UI.


## Funcionalidades principales
- **Gestión de Zonas**:
  - Selección de zona con clic izquierdo y soporte de doble clic para interacciones avanzadas (ver `_handle_zone_selection(...)`).
  - Duplicar zona: tecla `N` (la nueva zona queda seleccionada).
  - Ocultar/mostrar zona seleccionada: tecla `H`.
  - Eliminar zona: tecla `D` abre confirmación por la herramienta `delete_zone`.
  - Renombrado: modo específico con edición por teclado (Enter para confirmar, Backspace para borrar; ver `_handle_renaming_keys(...)` y `_handle_renaming_click(...)`).

- **Pintura de Tiles (Overlays)**:
  - Herramienta `paint_tiles/` con ejecución asíncrona por lotes y barra de progreso en `map_editor_view.py`.
  - Actualización incremental de chunks mientras se pinta para feedback inmediato.
  - Integración con `overlay_service.py` (`set_overlay_cell`, `merge_zone_to_world`).

- **Colliders**:
  - `paint_colliders/` para aplicar colisiones.
  - `clear_colliders/` para limpiar colisiones.

- **Capas**:
  - `view_layers/` permite alternar visibilidad de capas (`roguelike_engine.map.model.layer.Layer`).

- **Persistencia**:
  - Cargar zonas: tecla `L` (`controller.load_zones()`).
  - Guardar zonas: `Ctrl+S` (`controller.save_zones()`).
  - Carga/guardado de overlays/capas: `load_layers(...)` / `save_layers(...)`.


## Controles y atajos
- **Zoom**: rueda del mouse (`MOUSEWHEEL`).
- **Panning**: mantener botón medio o derecho y arrastrar; también con flechas (←↑→↓) moviendo la cámara a paso constante corregido por zoom.
- **Undo/Redo**: `Ctrl+Z` / `Ctrl+Y`.
- **Mostrar/Ocultar Editor**: `F11` (`manager.toggle()`).
- **Salir de la app**: `Esc` (persiste cámara si el editor está activo antes de cerrar).
- **Zonas**: `N` duplicar, `H` ocultar/mostrar, `D` borrar (confirmación), `L` cargar, `Ctrl+S` guardar.


## Flujo de trabajo típico
1. **Abrir el editor** con `F11`.
2. **Seleccionar una zona** con clic izquierdo.
3. **Usar la toolbar** para elegir la acción: crear/eliminar zona, pintar tiles, colliders, ver capas.
4. Si pinta tiles, observar **progreso** y actualización incremental de chunks; usar **Undo/Redo** si es necesario.
5. **Guardar** con `Ctrl+S`.


## Tests
- No se identificó una suite específica para el Map Editor en `tests/roguelike_editors/` al momento de esta documentación.
  - Sugerido: añadir pruebas de eventos (zoom, panning, atajos), de selección/renombrado de zonas y de comandos (`PaintTilesCommand`).


## Convenciones y buenas prácticas
- **Separación de responsabilidades**: lógica en `controller`, render en `view`, mapeo Pygame→acciones en `events`, estado en `state`.
- **Usar comandos** para acciones que deben poder deshacerse (ej.: pintura de tiles).
- **Respetar UI blockers** al añadir nuevos paneles o diálogos.
- **Persistencia explícita**: exponer acciones claras de cargar/guardar y mantener sincronizados memoria↔disco.


## Extensión
Para añadir una nueva herramienta en la toolbar:
1. Crear carpeta `map_tool_bar_panel/nueva_herramienta/` con `..._model.py`, `..._view.py`, `..._controller.py`, `..._events.py`.
2. Integrar enrutamiento en `map_tool_bar_panel_*` y exponer botones en `map_tool_bar_panel_view.py`.
3. Si modifica el mapa, considerar un **comando undo/redo**.
4. Añadir feedback visual en `map_editor_view.py` (overlays, barras de progreso, etiquetas).


## Entradas relacionadas en el código
- Núcleo del editor: `map_editor_controller.py`, `map_editor_view.py`, `map_editor_events.py`, `map_editor_state.py`.
- Toolbar: `map_tool_bar_panel/` y submódulos (`add_zone/`, `delete_zone/`, `view_layers/`, `paint_tiles/`, `paint_colliders/`, `clear_colliders/`).
- Comandos y servicios: `commands/paint_tiles_command.py`, `services/overlay_service.py`.
- Configuración: `roguelike_engine.config.config_editor` (batch/tick), `roguelike_engine.config.config_tiles` (TILE_SIZE), `roguelike_engine.config.map_config` (ajustes globales).


## Notas
- La pintura asíncrona de tiles utiliza lotes y ticks para mantener la UI fluida y refrescar chunks de forma incremental.
- El editor persiste y restaura cámara al salir si está activo, mejorando la continuidad del trabajo.
