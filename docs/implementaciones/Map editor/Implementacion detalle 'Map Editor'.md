**Desarrollando Editor de**

Pensó durante un par de segundos

A continuación describo con detalle cómo podríamos implementar ese nuevo “Map Editor” siguiendo la misma filosofía MVC que ya tienes para tiles y edificios. La idea es crear un módulo independiente en `src/roguelike_game/systems/editor/map/` y luego integrarlo en el ciclo de juego (Game, RendererManager, input, etc.). Dividiré la propuesta en varias secciones:

---

## 1. Estructura de archivos y carpetas

```text
src/
└── roguelike_game/
    ├── game/
    │   ├── game.py
    │   ├── tiles_editor_manager.py
    │   ├── buildings_editor_manager.py
    │   └── map_editor_manager.py        <-- Nuevo
    └── systems/
        └── editor/
            ├── tiles/
            │   ├── tile_editor_state.py
            │   ├── tile_editor_controller.py
            │   ├── tile_editor_events.py
            │   └── tile_editor_view.py
            ├── buildings/
            │   ├── building_editor_state.py
            │   ├── building_editor_controller.py
            │   ├── building_editor_events.py
            │   └── building_editor_view.py
            └── map/
                ├── map_editor_state.py
                ├── map_editor_controller.py
                ├── map_editor_events.py
                └── map_editor_view.py
```

* `map_editor_manager.py`: punto de entrada desde `Game` (igual que `TilesEditorManager` y `BuildingsEditorManager`).
* Dentro de `systems/editor/map/` el patrón MVC:

  * **State**: define flags y datos persistentes del editor.
  * **Controller**: lógica para crear/zona, duplicar, borrar, mover, cargar/guardar JSON.
  * **Events**: captura de teclas y ratón, obtiene eventos de Pygame y traduce a acciones de controller.
  * **View**: dibuja en pantalla rectángulos que representan “zonas” (usando offsets predefinidos), nombres, resaltados, handles de arrastre, etc.

---

## 2. `map_editor_state.py`

Este archivo contendrá la clase que mantiene el estado del editor de mapa. Por ejemplo:

```python
# Path: src/roguelike_game/systems/editor/map/map_editor_state.py

from typing import Dict, Tuple, Optional

class MapEditorState:
    """
    Estado del Map Editor:
      - active: si el editor está activo o no.
      - selected_zone: nombre (o id) de la zona actualmente seleccionada.
      - zones: diccionario de zonas cargadas, mapeado por nombre → datos de la zona.
      - hidden_zones: set/listado de nombres de zonas ocultas.
      - dragging: flag si se está arrastrando una zona con el ratón.
      - drag_offset: desplazamiento interno cuando iniciamos un drag (dx, dy).
    """

    def __init__(self):
        self.active: bool = False
        self.selected_zone: Optional[str] = None

        # Estructura principal: cada zona tiene un nombre único (string) y un offset (col, row)
        # P. ej. "zona1" → (offset_x, offset_y)
        self.zones: Dict[str, Tuple[int, int]] = {}  

        # Zonas ocultas (no se dibujarán salvo que se hagan visibles)
        self.hidden_zones: set[str] = set()

        # Para arrastre con el ratón: 
        #  - dragging: si estamos en proceso de arrastrar una zona
        #  - drag_offset: la diferencia (mouse_x - zona_x, mouse_y - zona_y) al iniciar drag
        self.dragging: bool = False
        self.drag_offset: Tuple[int, int] = (0, 0)

        # Para distinguir entre crear nueva zona vs. editar offset manual:
        # al pulsar N, generamos un nombre temporal como "zone_1", "zone_2", etc.
        self.next_zone_id: int = 1

    def reset_selection(self):
        self.selected_zone = None
        self.dragging = False
        self.drag_offset = (0, 0)

    def generate_new_zone_name(self) -> str:
        """
        Devuelve un nombre único para una nueva zona, p. ej. "zone_1", "zone_2", ...
        e incrementa el contador interno.
        """
        name = f"zone_{self.next_zone_id}"
        self.next_zone_id += 1
        return name
```

**Notas importantes:**

* `zones` guarda el offset (col, row) para cada “zona” según tu `global_map_settings.zone_offsets`.
* `hidden_zones` es un conjunto de strings de zonas que no se quieren ver (atajo “H”).
* `dragging` y `drag_offset` nos ayudan a implementar el “click/drag” para mover la zona en pantalla.
* `selected_zone` es el string que identifica cuál zona está activa para operaciones tipo duplicar, borrar, etc.

---

## 3. `map_editor_controller.py`

Aquí va la “lógica” pura: crear zona, duplicar, borrar, mover offsets, cargar/guardar JSON, etc. Usaremos internamente la información del estado (`MapEditorState`) y delegaremos la persistencia a, por ejemplo, un módulo aparte o directamente a funciones JSON. A modo de esqueleto:

```python
# Path: src/roguelike_game/systems/editor/map/map_editor_controller.py

import json
import os
from typing import Tuple
from roguelike_game.systems.editor.map.map_editor_state import MapEditorState
from roguelike_game.config.global_map_settings import ZONES_JSON_PATH, ZONE_DEFAULT_SIZE

class MapEditorController:
    """
    Controlador del Map Editor:
      - expand_zone: crea una nueva zona con nombre único.
      - duplicate_zone: duplica el offset de la zona seleccionada bajo un nuevo nombre.
      - delete_zone: borra zona seleccionada.
      - move_zone: actualiza el offset de una zona concreta.
      - hide_show_zone: alterna visibilidad de una zona.
      - load_zones: carga desde JSON todos los offsets a state.zones.
      - save_zones: guarda el diccionario de offsets (y eventualmente otros metadatos) a JSON.
    """

    def __init__(self, state: MapEditorState):
        self.state = state

    def expand_zone(self) -> None:
        """
        Crea una nueva zona con nombre único, en posición (0,0) por defecto.
        Luego marca esa zona como seleccionada.
        """
        name = self.state.generate_new_zone_name()
        # Por defecto, la nueva zona arranca en (0,0)
        self.state.zones[name] = (0, 0)
        self.state.selected_zone = name
        print(f"[MapEditor] Zona creada: {name} en offset (0,0)")

    def duplicate_zone(self) -> None:
        """
        Duplica la zona actualmente seleccionada, bajo un nuevo nombre único,
        copiando su offset.
        """
        sel = self.state.selected_zone
        if not sel or sel not in self.state.zones:
            print("[MapEditor] No hay zona seleccionada para duplicar.")
            return

        new_name = self.state.generate_new_zone_name()
        self.state.zones[new_name] = self.state.zones[sel]
        self.state.selected_zone = new_name
        print(f"[MapEditor] Zona '{sel}' duplicada como '{new_name}'")

    def delete_zone(self) -> None:
        """
        Borra la zona seleccionada del estado.
        """
        sel = self.state.selected_zone
        if not sel or sel not in self.state.zones:
            print("[MapEditor] No hay zona seleccionada para borrar.")
            return

        del self.state.zones[sel]
        # También eliminar de hidden si estaba oculta
        self.state.hidden_zones.discard(sel)
        print(f"[MapEditor] Zona borrada: {sel}")
        self.state.selected_zone = None

    def move_zone(self, zone_name: str, new_offset: Tuple[int, int]) -> None:
        """
        Actualiza el offset de la zona 'zone_name' a new_offset = (col, row).
        """
        if zone_name not in self.state.zones:
            return
        self.state.zones[zone_name] = new_offset
        # No cambiamos selección; el view se refrescará automáticamente.
        # Si queremos, podemos imprimir o loguear:
        print(f"[MapEditor] Zona '{zone_name}' movida a offset {new_offset}")

    def hide_show_zone(self) -> None:
        """
        Si la zona seleccionada está visible, la mueve a hidden_zones. Si ya está oculta, la remueve de hidden_zones.
        """
        sel = self.state.selected_zone
        if not sel or sel not in self.state.zones:
            return

        if sel in self.state.hidden_zones:
            self.state.hidden_zones.remove(sel)
            print(f"[MapEditor] Zona '{sel}' mostrada nuevamente.")
        else:
            self.state.hidden_zones.add(sel)
            print(f"[MapEditor] Zona '{sel}' ocultada.")

    def load_zones(self) -> None:
        """
        Lee ZONES_JSON_PATH (por ejemplo 'data/zones/zones.json') y carga en state.zones.
        Se espera un formato simple: { "zone_name": [offset_x, offset_y], ... }
        """
        if not os.path.isfile(ZONES_JSON_PATH):
            print(f"[MapEditor] No existe el fichero de zonas en '{ZONES_JSON_PATH}'.")
            return

        try:
            with open(ZONES_JSON_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
            # Sobreescribimos state.zones
            self.state.zones.clear()
            for name, ofs in data.items():
                # Validar que ofs sea lista de longitud 2
                if isinstance(ofs, list) and len(ofs) == 2:
                    self.state.zones[name] = (int(ofs[0]), int(ofs[1]))
            print("[MapEditor] Zonas cargadas desde JSON.")
        except Exception as e:
            print(f"[MapEditor][Error] Al cargar zonas: {e}")

    def save_zones(self) -> None:
        """
        Escribe el diccionario state.zones en ZONES_JSON_PATH con formato:
          { "zone1": [x, y], "zone2": [x,y], ... }
        """
        try:
            os.makedirs(os.path.dirname(ZONES_JSON_PATH), exist_ok=True)
            serializable = {name: [ofs[0], ofs[1]] for name, ofs in self.state.zones.items()}
            with open(ZONES_JSON_PATH, "w", encoding="utf-8") as f:
                json.dump(serializable, f, indent=2)
            print("[MapEditor] Zonas guardadas a JSON.")
        except Exception as e:
            print(f"[MapEditor][Error] Al guardar zonas: {e}")
```

**Constantes auxiliares** (ejemplo en `global_map_settings.py`):

```python
# Path: src/roguelike_game/config/global_map_settings.py

# Ruta donde guardaremos offsets de zonas
ZONES_JSON_PATH = "data/zones/zones.json"

# Tamaño en tiles de cada “zona” (ancho × alto),
# si necesitas dibujar un rectángulo del mismo tamaño para el overlay.
ZONE_DEFAULT_SIZE = (16, 16)  # ejemplo: cada zona ocupa 16×16 tiles
```

---

## 4. `map_editor_events.py`

En este archivo capturamos los eventos de teclado y ratón (F11, N, L, Ctrl+S, D, Delete, H, click, drag) y los traducimos a llamados al `controller`. La firma suele ser igual a `TileEditorEventHandler`: recibe `game_state`, `editor_state` y `controller`. Por simplicidad vamos a acceder desde la instancia del manager a `editor_state` y `controller`.

```python
# Path: src/roguelike_game/systems/editor/map/map_editor_events.py

import pygame
from roguelike_game.systems.editor.map.map_editor_state import MapEditorState
from roguelike_game.systems.editor.map.map_editor_controller import MapEditorController
from roguelike_game.config.global_map_settings import ZONE_DEFAULT_SIZE

class MapEditorEventHandler:
    """
    Captura eventos de teclado y ratón para el Map Editor.
    Debe llamarse desde Game.handle_events(), sólo cuando state.map_editor.active == True.
    """

    def __init__(self, editor_state: MapEditorState, controller: MapEditorController):
        self.state = editor_state
        self.controller = controller

    def handle(self, event: pygame.event.Event, camera, game_map):
        """
        Procesa eventos Pygame según atajos definidos:
          - N: crear nueva zona
          - L: cargar zonas
          - Ctrl+S: guardar zonas
          - D: duplicar zona
          - Delete: eliminar zona
          - H: ocultar/mostrar zona
          - Click/Drag: seleccionar, arrastrar y soltar zona
        Parametros:
          - event: objeto pygame.event.Event
          - camera: para convertir coords pantalla→mundo
          - game_map: para conocer tamaño del mapa y offsets (si es relevante)
        """

        # Solo procesar si editor activo
        if not self.state.active:
            return

        # --- TECLAS ---
        if event.type == pygame.KEYDOWN:
            # N: crear nueva zona
            if event.key == pygame.K_n:
                self.controller.expand_zone()
                return

            # L: cargar zonas desde JSON
            if event.key == pygame.K_l:
                self.controller.load_zones()
                return

            # Ctrl+S: guardar zonas
            if event.key == pygame.K_s and (pygame.key.get_mods() & pygame.KMOD_CTRL):
                self.controller.save_zones()
                return

            # D: duplicar zona seleccionada
            if event.key == pygame.K_d:
                self.controller.duplicate_zone()
                return

            # Delete: borrar zona seleccionada
            if event.key == pygame.K_DELETE:
                self.controller.delete_zone()
                return

            # H: ocultar/mostrar zona
            if event.key == pygame.K_h:
                self.controller.hide_show_zone()
                return

        # --- ROGUE: click / selección / arrastre ---
        # Convertir posición del mouse a coordenadas de “zona” (offset col/row)
        # Suponemos que cada zona se dibuja como un rect de tamaño ZONE_DEFAULT_SIZE * TILE_SIZE,
        # y su posición en pantalla se calcula con camera.apply((offset_x*TILE_SIZE, offset_y*TILE_SIZE)).
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Convertir a coordenadas del “mundo”
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y

            # Buscar si el click cae dentro de alguna zona visible
            clicked_zone = None
            for name, (ofs_x, ofs_y) in self.state.zones.items():
                if name in self.state.hidden_zones:
                    continue
                # Dimensiones en pixeles
                tile_w, tile_h = ZONE_DEFAULT_SIZE
                pixel_w = tile_w * game_map.TILE_SIZE * camera.zoom
                pixel_h = tile_h * game_map.TILE_SIZE * camera.zoom
                # Calculamos rect en coords WORLD: 
                zone_world_x = ofs_x * game_map.TILE_SIZE
                zone_world_y = ofs_y * game_map.TILE_SIZE
                # Convertir a pantalla
                sx, sy = camera.apply((zone_world_x, zone_world_y))
                # Objeto rect en pantalla (aproximación)
                rect = pygame.Rect(sx, sy, pixel_w, pixel_h)
                if rect.collidepoint(mx, my):
                    clicked_zone = name
                    # calcular offset interno para arrastre
                    # esperanza: el ratón dentro de la zona
                    # offset drag = (wx - zone_world_x, wy - zone_world_y) en coordenadas WORLD
                    dx = wx - zone_world_x
                    dy = wy - zone_world_y
                    self.state.dragging = True
                    self.state.drag_offset = (dx, dy)
                    break

            # Si hicimos click en una zona, la seleccionamos; si no, deseleccionamos
            if clicked_zone:
                self.state.selected_zone = clicked_zone
            else:
                # Click fuera de una zona: deseleccionamos
                self.state.reset_selection()
            return

        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            # Soltar el botón: terminar drag
            if self.state.dragging:
                self.state.dragging = False
                self.state.drag_offset = (0, 0)
            return

        if event.type == pygame.MOUSEMOTION and self.state.dragging:
            # Arrastrar zona
            # Obtenemos mouse en coords WORLD
            mx, my = event.pos
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y

            sel = self.state.selected_zone
            if sel:
                # Nueva posición WORLD: (wx - drag_offset_x, wy - drag_offset_y)
                dx_off, dy_off = self.state.drag_offset
                new_world_x = wx - dx_off
                new_world_y = wy - dy_off
                # Convertir a OFFSET en tiles (col, row)
                col = int(new_world_x // game_map.TILE_SIZE)
                row = int(new_world_y // game_map.TILE_SIZE)
                # Actualizar en state y controller
                self.controller.move_zone(sel, (col, row))
            return
```

**Notas:**

* Cuando se pulsa con el botón izquierdo, buscamos si el ratón está dentro del rectángulo de alguna zona “no oculta”. Si lo está, iniciamos `dragging=True` y guardamos el desplazamiento interno. Si no está sobre ninguna, damos “deseleccionar”.
* Al mover el ratón (`MOUSEMOTION`) con `dragging=True`, recalculamos el offset en tiles y llamamos a `controller.move_zone()`.
* Al soltar (`MOUSEBUTTONUP`), terminamos el drag.
* Para simplificar, hemos asumido que `game_map.TILE_SIZE` existe y es el mismo TILE\_SIZE del mapa global. Si tu estructura de módulos coloca TILE\_SIZE en otro sitio, basta con importarlo desde `roguelike_engine.config.config_tiles`.
* El método de detección de clic/drag necesita ajustar el cálculo de tamaño en pixeles del rectángulo de zona según `camera.zoom`. Yo lo he hecho multiplicando `tile_w * TILE_SIZE * camera.zoom` en lugar de convertir primero a “world coords” y luego a “screen”. Ajusta según tu implementación de `camera.apply`.

---

## 5. `map_editor_view.py`

El view se encarga de pintar en pantalla cada zona (un rectángulo, su nombre, resaltado de la zona seleccionada y no dibujar las ocultas). Algo así:

```python
# Path: src/roguelike_game/systems/editor/map/map_editor_view.py

import pygame
from roguelike_game.config.global_map_settings import ZONE_DEFAULT_SIZE

class MapEditorView:
    """
    Dibuja un overlay sobre el mapa, pintando cada zona como un rectángulo semi-transparente,
    con su nombre encima. Resalta la zona seleccionada con un color diferente.
    """

    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        # Colores para dibujar: 
        self.zone_color = (0, 200, 0, 80)      # verde semitransparente
        self.selected_color = (255, 200, 0, 120)  # amarillo más opaco
        self.hidden_color = (100, 100, 100, 50)   # gris semitransparente
        self.font = pygame.font.SysFont("Arial", 14)

    def render(self, screen: pygame.Surface, camera, game_map):
        """
        Se llama en cada frame, sólo si el editor está activo.
        Dibuja todas las zonas en state.zones. Las ocultas se dibujan con color gris oscuro (opcional) o no se dibujan.
        Resalta la zona seleccionada.
        """
        tile_size = game_map.TILE_SIZE
        tile_w, tile_h = ZONE_DEFAULT_SIZE  # e.g. (16,16) tiles

        for name, (ofs_x, ofs_y) in self.state.zones.items():
            # Convertir offset en tiles a posición WORLD en píxeles
            world_x = ofs_x * tile_size
            world_y = ofs_y * tile_size
            # Convertir a coords de pantalla
            screen_x, screen_y = camera.apply((world_x, world_y))
            # Dimensiones en pantalla (teniendo en cuenta zoom)
            pixel_w = tile_w * tile_size * camera.zoom
            pixel_h = tile_h * tile_size * camera.zoom

            # Elegir color según si está seleccionada u oculta
            if name == self.state.selected_zone:
                color = self.selected_color
            elif name in self.state.hidden_zones:
                # Opción: no dibujar (skip) o dibujar con color “oculto”
                # Vamos a dibujar con gris, pero muy transparente
                color = self.hidden_color
            else:
                color = self.zone_color

            # Crear superficie temporal semi-transparente para rectángulo
            surf = pygame.Surface((pixel_w, pixel_h), flags=pygame.SRCALPHA)
            surf.fill(color)

            # Dibujar borde más oscuro para distinguir
            border_rect = pygame.Rect(0, 0, pixel_w, pixel_h)
            pygame.draw.rect(surf, (0, 0, 0, 200), border_rect, width=2)

            # Blitear en pantalla
            screen.blit(surf, (screen_x, screen_y))

            # Dibujar el nombre de la zona en la esquina superior izquierda del rect
            text_surf = self.font.render(name, True, (255, 255, 255))
            screen.blit(text_surf, (screen_x + 4, screen_y + 4))
```

**Puntos clave:**

* Cada zona es un rectángulo de tamaño `(zona_width tiles × TILE_SIZE` pixeles) × `(zona_height tiles × TILE_SIZE` pixeles). En el ejemplo asumo un tamaño constante `ZONE_DEFAULT_SIZE`.
* Para dibujar con transparencia, se utiliza una superficie intermedia creada con `SRCALPHA` y luego se blitea sobre `screen`.
* Se diferencia la zona seleccionada con un color amarillo/ámbar semitransparente más intenso.
* Las zonas “ocultas” se dibujan con un gris muy transparente (o, si prefieres, puedes hacer un `continue` para no dibujarlas en absoluto).
* El texto con el nombre se pinta en blanco sobre esa superficie, ligeramente desplazado (márgenes).

---

## 6. `map_editor_manager.py`

Este archivo es el equivalente a `TilesEditorManager` o `BuildingsEditorManager`: instancia el state, controller, view y handler, y expone métodos de “toggle” (activar/desactivar) si hace falta.

```python
# Path: src/roguelike_game/game/map_editor_manager.py

from roguelike_game.systems.editor.map.map_editor_state import MapEditorState
from roguelike_game.systems.editor.map.map_editor_controller import MapEditorController
from roguelike_game.systems.editor.map.map_editor_view import MapEditorView
from roguelike_game.systems.editor.map.map_editor_events import MapEditorEventHandler

class MapEditorManager:
    """
    Manager que agrupa State, Controller, View y EventHandler para el Map Editor.
    Se crea durante la inicialización de Game.
    """

    def __init__(self, game):
        # Recibimos la referencia al objeto Game por si necesitamos algo (como map_manager, etc.)
        self.game = game

        # 1) Estado
        self.editor_state = MapEditorState()

        # 2) Lógica
        self.controller = MapEditorController(self.editor_state)

        # 3) Vista
        self.view = MapEditorView(self.controller, self.editor_state)

        # 4) Handler de eventos
        # Pasamos state y controller. El game_map y camera se reciben en handle()
        self.handler = MapEditorEventHandler(self.editor_state, self.controller)

    def toggle(self):
        """
        Activa/desactiva el editor de mapa (por ejemplo cuando pulsan F11).
        También debe desactivar otros editores (tiles, buildings).
        """
        active = not self.editor_state.active
        self.editor_state.active = active

        # Si lo activamos, limpiar selección previa
        if active:
            self.editor_state.reset_selection()

        print("🗺️ Map Editor ON" if active else "🛑 Map Editor OFF")
```

No necesita métodos complicados, porque toda la lógica está en el `controller` y el `handler`. Simplemente agrupamos las cuatro piezas y brindamos un método `toggle()`.

---

## 7. Integración en `Game` (src/roguelike\_game/game/game.py)

Debemos:

1. En el constructor de `Game`, llamar a `_init_map_editor()` para crear la instancia `self.map_editor`.
2. Modificar `handle_events()` para priorizar el `map_editor` cuando esté activo.
3. Modificar el teclado global (en `roguelike_engine/input/keyboard.py`) para que F11 invoque `map_editor.toggle()` (y desactive los demás editores).
4. Incluir el renderizado del overlay del editor de mapa en `RendererManager.render_game()`.

Voy a mostrar fragmentos concretos.

### 7.1. Añadir `_init_map_editor()` en `Game.__init__`

```python
# Path: src/roguelike_game/game/game.py

import pygame
from roguelike_game.game.tiles_editor_manager import TilesEditorManager
from roguelike_game.game.buildings_editor_manager import BuildingsEditorManager
from roguelike_game.game.map_editor_manager import MapEditorManager
# ... demás imports ...

class Game:
    def __init__(self, screen, perf_log, map_name=None, loading_bg=None):
        # (Código existente que inicializa self.screen, self.camera, self.map, self.entities, etc.)

        # Editor de tiles y buildings ya los inicializabas algo así:
        self.tiles_editor = TilesEditorManager(self)
        self.buildings_editor = BuildingsEditorManager(self)

        # 1) Inicializar el Map Editor
        self._init_map_editor()

        # Resto de la inicialización...
        #   - instanciar RendererManager, UpdateManager, EffectsManager, etc.

    def _init_map_editor(self):
        """
        Creación de MapEditorManager y guardarlo en self.map_editor.
        """
        self.map_editor = MapEditorManager(self)
```

### 7.2. Modificar `Game.handle_events()`

Supongamos que tenías algo como esto antes:

```python
def handle_events(self):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            self.state.running = False
        # ...
        # Aquí ibas procesando cada editor por separado:
        if self.tiles_editor.editor_state.active:
            self.tiles_editor.handler.handle(camera, map)
        elif self.buildings_editor.editor_state.active:
            self.buildings_editor.handler.handle(camera, map)
        else:
            # Lógica normal de inputs + movimientos del jugador + disparos, etc.
```

Debes insertar un bloque para `map_editor` **antes** de la lógica normal, y también antes de Tiles/Buildings. Por ejemplo:

```python
def handle_events(self):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            self.state.running = False
            return

        # 1) Si map editor activo, enviarle el evento y saltar el resto
        if self.map_editor.editor_state.active:
            self.map_editor.handler.handle(event, self.camera, self.map)
            # Evitamos que otras partes lo procesen
            continue

        # 2) Si tiles editor activo
        if self.tiles_editor.editor_state.active:
            self.tiles_editor.handler.handle(self.camera, self.map)
            continue

        # 3) Si buildings editor activo
        if self.buildings_editor.editor_state.active:
            self.buildings_editor.handler.handle(self.camera)
            continue

        # 4) Aquí va el manejo “normal” de entrada (mover jugador, disparar, abrir menú, etc.)
        self._handle_gameplay_input(event)
```

**Importante:** el `MapEditorEventHandler.handle` recibe el evento, la cámara y el mapa (`game.map`). Asegúrate de pasar esos objetos correctamente.

### 7.3. Captura de F11 en el teclado global

Suponiendo que en `roguelike_engine/input/keyboard.py` o similar tenías algo así:

```python
# Path: roguelike_engine/input/keyboard.py

import pygame

def handle_keyboard(event, game):
    """
    Función que centraliza el manejo de teclas globales.
    'game' es la instancia de Game.
    """
    if event.type == pygame.KEYDOWN:
        if event.key == pygame.K_F8:
            # Toggle tiles editor
            game.tiles_editor.toggle()
            return

        if event.key == pygame.K_F10:
            # Toggle buildings editor
            game.buildings_editor.toggle()
            return

        # AÑADIR aquí F11 para Map Editor:
        if event.key == pygame.K_F11:
            # Desactivar otros editores
            game.tiles_editor.editor_state.active = False
            game.buildings_editor.editor_state.active = False
            # Toggle map editor
            game.map_editor.toggle()
            return

        # Resto de atajos globales (menú, etc.)
        # ...
```

De ese modo, cuando pulses F11, se apaguen Tiles y Buildings y se active/desactive Map Editor. En `game.map_editor.toggle()` ya imprimimos “🗺️ Map Editor ON” o “OFF”.

---

## 8. Modificar `RendererManager.render_game()` para incluir overlay del Map Editor

En `render_manager.py`, tras los bloques de renderizado de tiles-editor y buildings-editor, podemos insertar:

```python
# Path: src/roguelike_game/game/render_manager.py

    def render_game(
        self,
        state,
        screen,
        camera,
        perf_log=None,
        menu=None,
        map=None,
        entities=None,
        systems=None,
    ):

        # (render previo: limpia pantalla, dibuja mapa, entidades, HUD, etc.)

        # 7) Menú (ya existente)
        @benchmark(perf_log, "3.7. menu")
        def _bench_menu():
            self._render_menu(screen, menu)
        _bench_menu()

        # 8) Minimap
        @benchmark(perf_log, "3.8. minimap")
        def _bench_minimap():
            self._render_minimap(screen)
        _bench_minimap()

        # 9) Otros sistemas
        @benchmark(perf_log, "3.9. systems")
        def _bench_systems():
            systems.render(screen, camera)
        _bench_systems()

        # 10) Editores existentes: tiles y buildings
        @benchmark(perf_log, "3.10. editors")
        def _bench_editors():
            self._render_editors()
        _bench_editors()

        # ————— NUEVO BLOQUE: Map Editor Overlay —————
        # Si el map editor está activo, se lo dibujamos encima
        if hasattr(self, "map_editor") and self.map_editor.editor_state.active:
            # Llamamos a view.render pasándole el screen, camera y el mapa
            self.map_editor.view.render(screen, camera, self.game.map)
        # Nota: si tu RendererManager no tiene acceso directo a la instancia Game, 
        # podrías pasarle el objeto map en render_game() y aquí usarlo: “map” en lugar de “self.game.map”.

        # Debug: overlay y bordes
        render_debug_overlay(self.debug_overlay, screen, state, camera, self.map, entities, show_borders=True)
        # Mostrar ayuda de controles según el modo
        self._render_help_overlay(state)

        return self._dirty_rects
```

**Precauciones:**

* Asegúrate de que `RendererManager` tenga acceso a `self.map_editor` y a `self.game.map` (o bien modifica la firma de `render_game()` para recibir un parámetro extra `map_editor` y `map`). Por ejemplo, en `Game.run()` cuando llames a `renderer.render_game(...)`, pásale también `self.map_editor` y `self.map`.
* El bloque debe ejecutarse **después** de que el mapa base se dibuje, pero **antes** de que se haga el flip o update de pantalla, para que las zonas aparezcan encima.

---

## 9. Resumen de atajos definitivos

A continuación recojo la lista de atajos que propusimos y qué métodos llaman internamente:

| Tecla         | Acción                                                             | Método invocado                        |
| ------------- | ------------------------------------------------------------------ | -------------------------------------- |
| **F11**       | Toggle Map Editor (ON/OFF), desactiva Tiles y Buildings            | `MapEditorManager.toggle()`            |
| **N**         | Crear nueva zona                                                   | `MapEditorController.expand_zone()`    |
| **L**         | Cargar zonas desde JSON                                            | `MapEditorController.load_zones()`     |
| **Ctrl + S**  | Guardar zonas a `data/zones/zones.json`                            | `MapEditorController.save_zones()`     |
| **D**         | Duplicar zona seleccionada                                         | `MapEditorController.duplicate_zone()` |
| **Delete**    | Eliminar zona seleccionada                                         | `MapEditorController.delete_zone()`    |
| **H**         | Ocultar/Mostrar zona seleccionada (añade/remueve de hidden\_zones) | `MapEditorController.hide_show_zone()` |
| **Click Izq** | Seleccionar zona bajo el ratón y empezar drag si se arrastra       | `MapEditorEventHandler` (ver lógica)   |
| **Drag Izq**  | Mover la zona seleccionada (actualiza offset: `move_zone`)         | `MapEditorController.move_zone(...)`   |
| **Supr**      | (en algunos teclados Delete, en otros Supr) equivale a “Delete”    |                                        |

---

## 10. Posibles consideraciones extra

1. **Validar límites del mapa**

   * Cuando movemos la zona con el ratón, podemos querer impedir que el offset col/row sea negativo o se salga del área del mapa. En `move_zone()`, antes de asignar `(col, row)`, podríamos truncar:

     ```python
     col = max(0, min(col, map_width - zone_width))
     row = max(0, min(row, map_height - zone_height))
     ```

     obteniendo `map_width` y `map_height` de `game_map.tiles` o directamente desde `game_map.width`/`height`.

2. **Mostrar el contorno del mapa completo**

   * Podrías dibujar un borde que represente el “límite” del mapa completo para ayudar al usuario a orientarse, antes de dibujar las zonas. Por ejemplo, un rectángulo delimitando `(0,0)` a `(map_width*TILE_SIZE, map_height*TILE_SIZE)`.

3. **Edición avanzada de propiedades**

   * Si cada zona va a tener más metadatos (ej. “tipo de zona”, “banda sonora asociada”, etc.), podrías ampliar `MapEditorState.zones` para almacenar no solo `(offset_x, offset_y)`, sino un objeto/dict con más campos.
   * En `view`, al hacer clic derecho sobre una zona, podrías abrir un pequeño menú (por ejemplo con texto) para editar propiedades adicionales.

4. **Feedback visual al guardar/cargar**

   * En `save_zones()` y `load_zones()`, hemos impreso mensajes por consola. Podrías mostrar en pantalla un texto fugaz tipo “Zonas guardadas” durante 2 segundos. Para ello, necesitarías un pequeño temporizador en `MapEditorState` que se dispare al guardar y en `view.render()` muestre un mensaje en un rincón de la pantalla mientras no haya expirado.

5. **Atajos de teclado adicionales**

   * Si quieres acelerar el flujo, puedes añadir:

     * **F12** para “Guardar y salir del Map Editor”.
     * **Shift + N** para “crear nueva zona con valores de offset copiados de la última zona” (en lugar de (0,0)).
   * Pero esto ya depende de tus necesidades.

6. **Synchronización con `global_map_settings.zone_offsets`**

   * Si ya tienes definido un módulo `global_map_settings.zone_offsets` (quizá un diccionario global), asegúrate de que, tras `load_zones()`, asignas `global_map_settings.zone_offsets = state.zones.copy()`. Y antes de entrar al editor, quizás inicialices `state.zones = global_map_settings.zone_offsets.copy()`. En ese caso, la persistencia de las zonas en juego real usa ese diccionario global. Un ejemplo breve:

     ```python
     # En MapEditorController.load_zones()
     global global_map_settings
     # ... tras cargar en state.zones ...
     global_map_settings.zone_offsets = self.state.zones.copy()
     ```

     Y en `save_zones()` haces justo lo contrario: tomas `global_map_settings.zone_offsets` para escribir JSON. Ajusta según tengas definido el módulo.

---

## 11. Ejemplo de flujo de usuario

1. El jugador está en pantalla normal. Decide abrir el Map Editor: pulsa **F11**.

   * `MapEditorManager.toggle()` activa `editor_state.active = True`.
   * En la consola aparece “🗺️ Map Editor ON”.

2. Aparece el overlay: el `RendererManager` detecta `map_editor.active` y llama a `map_editor.view.render(...)`. Con ello ve el contorno de todas las zonas (si existen), o si está vacío, solo el borde del mapa.

3. Pulsa **N**:

   * `MapEditorEventHandler` recibe el `KEYDOWN`→`event.key == K_n` → `controller.expand_zone()`.
   * `expand_zone()` genera un nombre “zone\_1” con offset `(0,0)` y lo marca como `selected_zone`.
   * En la siguiente pasada de `view.render(...)`, se dibuja un rectángulo verde semitransparente en la esquina superior izquierda, con la etiqueta “zone\_1”.

4. Mueve el ratón dentro de ese rectángulo y arrastra:

   * Al pulsar click izquierdo, `MOUSEBUTTONDOWN` detecta la zona “zone\_1”, guarda `drag_offset` y pone `state.dragging = True`.
   * Mientras mueve el ratón (`MOUSEMOTION`), el handler recalcula `(col, row)` en base a `(mouse_world - drag_offset)` y llama a `move_zone("zone_1", (col, row))`.
   * `state.zones["zone_1"]` se actualiza continuamente.
   * El `view.render(...)` dibuja el rectángulo en la nueva posición.

5. Si pulsa **D**, se duplica la zona:

   * `controller.duplicate_zone()` crea “zone\_2” con el mismo offset que “zone\_1” y selecciona “zone\_2”.
   * Ahora en pantalla aparece otro rectángulo idéntico etiquetado “zone\_2”.

6. Pulsa **H**:

   * `hide_show_zone()` añade “zone\_2” a `state.hidden_zones`.
   * En el siguiente frame, “zone\_2” se dibuja con color gris semitransparente en lugar de verde (o desaparece, si así lo definimos).

7. Pulsa **Ctrl + S**:

   * `controller.save_zones()` escribe un JSON en `data/zones/zones.json`, p. ej.:

     ```json
     {
       "zone_1": [3, 5],
       "zone_2": [3, 5]
     }
     ```

8. Pulsa **L** (o reinicia el juego y cae en que hay zonas guardadas).

   * `load_zones()` lee el JSON e inicializa `state.zones` con esos valores.
   * `view` dibuja los rectángulos en las posiciones guardadas.

9. Para salir del editor, pulsa **F11** nuevamente:

   * `MapEditorManager.toggle()` desactiva `editor_state.active = False`.
   * El overlay desaparece y vuelve el control normal del juego.

---

## 12. Recapitulación final

Con esto tendrás un **Map Editor** completamente separado del resto de editores, que cumple con las siguientes características:

* Siguiendo patrón **MVC**:

  * **State** (`MapEditorState`) almacena flags y offsets de zonas.
  * **Controller** (`MapEditorController`) realiza operaciones CRUD sobre zonas y persistencia JSON.
  * **View** (`MapEditorView`) dibuja un overlay con rectángulos y nombres de zonas, resalta la seleccionada.
  * **Events** (`MapEditorEventHandler`) captura teclas y ratón para invocar métodos del controller.

* **MapEditorManager** une esos cuatro componentes para el ciclo de vida del editor.

* En **Game**:

  * Se inicializa con `_init_map_editor()`.
  * Se modifica `handle_events()` para darle prioridad cuando esté activo.
  * Se mueve la lógica de atajo **F11** al manejador de teclado global y se invoca `map_editor.toggle()`.

* En **RendererManager**:

  * Se añade un bloque que dibuja `map_editor.view.render(...)` si `editor_state.active` es True.

* Se definen atajos concretos:

  * **N** → nueva zona
  * **L** → cargar JSON
  * **Ctrl+S** → guardar JSON
  * **D** → duplicar
  * **Delete** → eliminar
  * **H** → ocultar/mostrar
  * **Click/Drag** → mover zonas


