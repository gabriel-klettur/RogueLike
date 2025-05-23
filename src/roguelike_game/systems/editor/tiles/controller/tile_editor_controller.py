# Path: src/roguelike_game/systems/editor/tiles/controller/tile_editor_controller.py
from pathlib import Path
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.config.map_config import global_map_settings

from roguelike_game.systems.editor.tiles.controller.tools.tile_picker_controller import TilePickerController
from roguelike_game.systems.editor.tiles.controller.tools.tile_toolbar_controller import TileToolbarController

from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer
from typing import Dict, List



class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, editor_state, picker_state):        
        self.editor  = editor_state     # instancia de TileEditorControllerState
        self.picker = TilePickerController(editor_state, picker_state)
        self.toolbar = TileToolbarController(editor_state)

    def select_tile_at(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if tile:
            self.editor.selected_tile = tile
            # Abrimos la paleta en el estado del picker
            self.picker.picker_state.open = True
            self.picker.picker_state.current_choice = None
            self.editor.scroll_offset = 0

    def apply_brush(self, mouse_pos, camera, map):
        """
        Pinta el sprite seleccionado sobre el tile bajo el ratón
        y persiste el código de overlay (quitando el prefijo "tiles/").
        """
        # Collision editing when in collision mode
        if (self.editor.show_collisions or self.editor.show_collisions_overlay) and self.editor.collision_choice:
            tile = self._tile_under_mouse(mouse_pos, camera, map)
            if tile:
                # Set collision state
                solid = True if self.editor.collision_choice == '#' else False
                tile.solid = solid
                # Update matrix and invalidate view
                row = tile.y // TILE_SIZE
                col = tile.x // TILE_SIZE
                try:
                    map.matrix[row][col] = self.editor.collision_choice
                except Exception:
                    pass
                map.view.invalidate_cache()
            return

        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile or not self.editor.current_choice:
            return

        # 2) Carga el nuevo sprite
        sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 3) Calcula el código de overlay sin el prefijo "tiles/"
        full = Path(self.editor.current_choice).with_suffix('')  # ej. "tiles/dungeon/dungeon_1"
        try:
            code = full.relative_to("tiles").as_posix()            # "dungeon/dungeon_1"
        except ValueError:
            code = full.as_posix()                                 # si no empieza por "tiles/"

        tile.overlay_code = code

        # 4) Actualizar en memoria y persistir solo la zona
        layer = self.editor.current_layer
        row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
        # determinar zona y offsets
        for zn,(ox,oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name, offx, offy = zn, ox, oy
                break
        else:
            zone_name, offx, offy = 'no_zone', 0, 0
        # 4.1) actualizar map.layers y map.tiles_by_layer
        try:
            map.layers[layer][row][col] = code
        except Exception:
            pass
        grid = map.tiles_by_layer.get(layer)
        if grid and 0 <= row < len(grid) and 0 <= col < len(grid[0]):
            t = grid[row][col]
            if t:
                t.sprite = sprite
                t.scaled_cache.clear()
                t.overlay_code = code
        # 4.2) extraer subgrids de map.layers para la zona
        zone_layers: dict[Layer, list[list[str]]] = {}
        if zone_name != 'no_zone':
            zh, zw = global_map_settings.zone_height, global_map_settings.zone_width
        else:
            zh, zw = len(map.tiles), len(map.tiles[0]) if map.tiles else 0
        for l, full in map.layers.items():
            sub = []
            for ry in range(zh):
                y = offy + ry
                if 0 <= y < len(full):
                    sub.append(full[y][offx:offx+zw])
                else:
                    sub.append([''] * zw)
            zone_layers[l] = sub
        save_layers(zone_name, zone_layers)
        print(f"[Tile][Persist] Zona '{zone_name}' actualizada: capa {layer.name}, pos ({row},{col})")
        # Debug for brush
        local_r = row - offy
        local_c = col - offx
        print(f"[Tile][Brush] 📝 Overlay aplicado: global ({row},{col}), local ({local_r},{local_c}) en zona '{zone_name}', capa: {layer.name}")
        map.view.invalidate_cache()


    def apply_eyedropper(self, mouse_pos, camera, map):
        """
        Selecciona el sprite bajo el cursor, lo aplica al tile y guarda el overlay igual que el brush.
        """
        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return

        # 2) Determinar código de overlay o tipo base
        code = tile.overlay_code or tile.tile_type

        # 3) Mapear código a nombre de asset en OVERLAY_CODE_MAP o DEFAULT_TILE_MAP
        if code in OVERLAY_CODE_MAP:
            asset_name = OVERLAY_CODE_MAP[code]
        else:
            asset_name = DEFAULT_TILE_MAP.get(code)
        if not asset_name:
            return

        # 4) Ruta relativa para el picker y brush (sin prefijo 'assets/')
        choice_path = f"tiles/{asset_name}.png"
        self.editor.current_choice = choice_path
        self.editor.current_tool = "brush"

        # 5) Cargar y asignar sprite al tile
        sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 6) Fijar overlay_code al código original
        tile.overlay_code = code

        # 7) Debug only: EyeDropper, no persistence
        layer = self.editor.current_layer
        row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
        # Determine zone for debug
        zone_name = 'no_zone'
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name = zn
                break
        print(f"[Tile][EyeDroper] Zona '{zone_name}', capa '{layer.name}', pos ({row},{col})")
        map.view.invalidate_cache()


    def _tile_under_mouse(self, mouse_pos, camera, map):
        mx, my = mouse_pos
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(map.tiles) and 0 <= col < len(map.tiles[0]):
            return map.tiles[row][col]
        return None


    def update(self, camera, game_map):
        """
        1) Actualiza el tile bajo el cursor (hover).
        2) Si el editor está activo y el picker está cerrado, aplica
           pintado continuo, borrado y relleno en bucket.
        """
        # --- 1) Hover del cursor ---
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        col = int(wx) // TILE_SIZE
        row = int(wy) // TILE_SIZE
        # Guardamos la celda ‘hovered’ para la vista
        self.editor.hovered_tile = (col, row)

        # --- 2) Si no estamos editando o el picker está abierto, salimos ---
        # Nota: el flag en tu estado se llama `picker_open`
        if not self.editor.active or getattr(self.editor, 'picker_open', False):
            return

        # --- 3) Botones del ratón ---
        left, middle, right = pygame.mouse.get_pressed()

        # Tile actualmente seleccionado en el picker
        selected = getattr(self.editor, 'selected_tile', None)

        # --- 4) PINTAR con botón izquierdo ---
        if left and selected is not None:
            # Ajusta según tu API de game_map:
            # por ejemplo, si accedes directo a la matriz:
            try:
                game_map.matrix[row][col] = selected
            except Exception:
                # o bien: game_map.set_tile(col, row, selected)
                pass

        # --- 5) BORRAR con botón derecho ---
        elif right:
            try:
                game_map.matrix[row][col] = None
            except Exception:
                # o bien: game_map.set_tile(col, row, None)
                pass

        # --- 6) BUCKET FILL con Shift + click izquierdo ---
        keys = pygame.key.get_pressed()
        if left and (keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]) and selected is not None:
            # Obtenemos el valor actual para reemplazar
            try:
                target = game_map.matrix[row][col]
            except Exception:
                # o: target = game_map.get_tile(col, row)
                target = None

            if target != selected:
                self._bucket_fill(game_map, row, col, target, selected)

    def _bucket_fill(self, game_map, start_row, start_col, target, replacement):
        """
        Flood-fill 4-direccional iterativo.
        """
        stack = [(start_row, start_col)]
        visited = set()

        while stack:
            r, c = stack.pop()
            if (r, c) in visited:
                continue
            visited.add((r, c))

            # Limites y coincidencia
            if r < 0 or c < 0:
                continue
            try:
                current = game_map.matrix[r][c]
            except Exception:
                # si usas get_tile:
                # current = game_map.get_tile(c, r)
                continue

            if current != target:
                continue

            # Reemplazamos
            try:
                game_map.matrix[r][c] = replacement
            except Exception:
                # o: game_map.set_tile(c, r, replacement)
                pass

            # Vecinos
            stack.extend([
                (r + 1, c),
                (r - 1, c),
                (r, c + 1),
                (r, c - 1),
            ])