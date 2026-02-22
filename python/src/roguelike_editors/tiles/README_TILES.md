# Editor de Tiles (Tiles Editor)

El Editor de Tiles permite visualizar, seleccionar y configurar tiles/tilesets, con enfoque en la edición de colisiones por tile y herramientas auxiliares de capas, tamaño/zoom y vista. Está implementado en Pygame con arquitectura MVC por panel, enrutamiento de eventos y bloqueo de UI para evitar interferencias con el mundo del juego mientras se interactúa con la interfaz.


## Objetivos
- Proveer una UI para trabajar con tiles y sus atributos (p. ej., colisiones por tile) sin reiniciar el juego.
- Integrarse con la UI común del proyecto (toolbar, pickers, panels) y respetar bloqueos de entrada.
- Facilitar la extensibilidad mediante módulos claramente separados (MVC por panel).


## Estructura del módulo
Directorio: `src/roguelike_editors/tiles/`

- Núcleo del editor (MVC):
  - `tile_editor_controller.py`
  - `tile_editor_view.py`
  - `tile_editor_events.py`
  - `tile_editor_state.py`
  - `tile_outline_view.py` (previsualización/contorno del brush/selección)
  - `tiles_editor_config.py` (ajustes del editor)

- Base común reutilizable:
  - `common/`
    - `controller.py`, `events.py`, `state.py`, `view.py`

- Panel de título:
  - `tiles_title/`
    - `tiles_tiles_controller.py`, `tiles_tiles_view.py`, `tiles_tiles_events.py`, `tiles_tiles_states.py`

- Toolbar (herramientas globales del editor):
  - `tiles_toolbar_panel/`
    - `tile_toolbar_controller.py`, `tile_toolbar_view.py`, `tile_toolbar_events.py`, `tile_toolbar_state.py`

- Picker de tiles (selección en grid/lista):
  - `tiles_picker_panel/`
    - `tile_picker_controller.py`, `tile_picker_view.py`, `tile_picker_events.py`, `tile_picker_state.py`

- Panel de Vista (canvas del editor de tiles):
  - `tiles_view_panel/`
    - `tiles_view_controller.py`, `tiles_view_view.py`, `tiles_view_events.py`, `tiles_view_state.py`

- Panel de Capas (visibilidad/orden de capas de visualización):
  - `layers_panel/`
    - `layers_panel_controller.py`, `layers_panel_view.py`, `layers_panel_events.py`, `layers_panel_states.py`

- Panel de Tamaño/Zoom (parámetros de visualización):
  - `size_panel/`
    - `size_panel_controller.py`, `size_panel_view.py`, `size_panel_events.py`, `size_panel_state.py`

- Panel de Colisiones por Tile:
  - `tiles_collision_panel/`
    - `tiles_collision_panel_controller.py`, `tiles_collision_panel_view.py`, `tiles_collision_panel_events.py`, `tiles_collision_panel_states.py`


## Arquitectura y flujo
- **MVC por panel**: Cada panel define su `model`, `view`, `controller` y `events` para aislar responsabilidades y facilitar pruebas.
- **Enrutamiento de eventos**: `tile_editor_events.py` delega los eventos Pygame a toolbar, picker, vista, y paneles auxiliares según el área del cursor y visibilidad.
- **UI Blockers**: Todos los paneles registran rectángulos de bloqueo; la vista y el `tile_outline_view` respetan estos bloqueos y no muestran previsualización/hover cuando el cursor está sobre la UI.
- **Configuración centralizada**: Parámetros del editor en `tiles_editor_config.py` y estados por panel en sus respectivos `..._state.py`.


## Funcionalidades principales
- **Selección de tiles** (Picker):
  - Grid con hover/selección consistente y scroll (ver `tiles_picker_panel/`).
  - Sincronización con la vista para resaltar/previsualizar el tile activo.

- **Vista del tileset/canvas** (View Panel):
  - Render del área de trabajo y overlays de ayuda (rejilla, contornos), gestionados por `tiles_view_panel/` y `tile_outline_view.py`.
  - La previsualización del brush/selección se oculta si el cursor está sobre paneles UI (respeta UI blockers).

- **Colisiones por tile** (Collision Panel):
  - Herramientas para definir/editar colisiones por tile desde `tiles_collision_panel/`.
  - Estados dedicados para modos de edición (ver `tiles_collision_panel_states.py`).

- **Capas** (Layers Panel):
  - Controla la visibilidad/orden de capas de la vista (p. ej., rejillas, overlays de colisión) desde `layers_panel/`.

- **Tamaño/Zoom** (Size Panel):
  - Ajuste de parámetros de visualización (tamaño de celda, zoom y/o escala) desde `size_panel/`.

- **Toolbar**:
  - Conjunto de herramientas y toggles globales del editor definidos en `tiles_toolbar_panel/`.


## Controles y atajos
Los controles y atajos dependen del panel en foco. Consulte los manejadores de eventos para el mapeo exacto:
- Editor: `tile_editor_events.py`
- Toolbar: `tiles_toolbar_panel/tile_toolbar_events.py`
- Picker: `tiles_picker_panel/tile_picker_events.py`
- Vista: `tiles_view_panel/tiles_view_events.py`
- Colisiones: `tiles_collision_panel/tiles_collision_panel_events.py`
- Capas/Tamaño: `layers_panel/layers_panel_events.py`, `size_panel/size_panel_events.py`


## Flujo de trabajo típico
1. Abrir el editor de Tiles desde la UI/atajo configurado por el juego.
2. Seleccionar un tile en el `tiles_picker_panel/`.
3. Ajustar la vista (capas, tamaño/zoom) según necesidad.
4. Entrar en modo de colisión y editar las colisiones del tile si aplica.
5. Confirmar/guardar según el flujo de persistencia definido por el proyecto.


## Tests
- No se identificó una suite específica para el Tiles Editor en `tests/roguelike_editors/` al momento de esta documentación.
  - Sugerido: añadir pruebas de eventos por panel (picker, vista, toolbar), y de reglas de bloqueo de UI en `tile_outline_view.py`.


## Convenciones y buenas prácticas
- **Separación estricta** entre lógica (controller), render (view), estado (model/state) y mapeo de eventos (events).
- **Registrar UI blockers** al mostrar paneles y consultarlos antes de dibujar previsualizaciones en la vista.
- **Evitar duplicar lógica común** reutilizando `tiles/common/` en nuevos paneles.
- **Mantener configuraciones** en `tiles_editor_config.py` y exponer valores necesarios vía estados/controladores.


## Extensión
Para añadir un nuevo panel/herramienta:
1. Crear carpeta `nuevo_panel/` con `..._model.py`, `..._view.py`, `..._controller.py`, `..._events.py` (y `..._state.py` si aplica).
2. Integrar enrutamiento en `tile_editor_events.py` y render en `tile_editor_view.py`.
3. Registrar su rectángulo en el sistema de UI blockers para que la vista no reciba hovers/clicks por debajo.
4. Si modifica datos persistentes (p. ej., máscaras de colisión), añadir comandos/servicios y puntos de guardado según las convenciones del proyecto.


## Entradas relacionadas en el código
- Núcleo del editor: `tile_editor_controller.py`, `tile_editor_view.py`, `tile_editor_events.py`, `tile_editor_state.py`, `tile_outline_view.py`.
- Paneles: `tiles_picker_panel/`, `tiles_view_panel/`, `tiles_toolbar_panel/`, `tiles_collision_panel/`, `layers_panel/`, `size_panel/`, `tiles_title/`.
- Base común: `tiles/common/`.
- Configuración: `tiles_editor_config.py`.


## Notas
- La vista respeta los **UI blockers** de todos los paneles para evitar fugas de hover/previsualización sobre la toolbar u otros paneles.
- Este editor convive con otros (Map, Entities, Items, FSM) y comparte las mismas políticas de bloqueo de entrada y modularidad MVC.
